package com.phonemonitor.client.decoder

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.util.Log
import android.view.Surface
import java.io.BufferedInputStream
import java.io.InputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicBoolean

interface DecoderStatsListener {
    fun onStatsUpdated(fps: Int, mbps: Double)
    fun onConnectionState(connected: Boolean, errorMsg: String? = null)
}

class MediaCodecDecoder(
    private val host: String,
    private val port: Int = 59100,
    private val surface: Surface,
    private val statsListener: DecoderStatsListener? = null
) {
    private val isRunning = AtomicBoolean(false)
    private var socket: Socket? = null
    private var decoder: MediaCodec? = null
    private var workerThread: Thread? = null

    fun start() {
        if (isRunning.getAndSet(true)) return

        workerThread = Thread({
            runDecodeLoop()
        }, "MediaCodecDecoderThread").apply {
            priority = (Thread.NORM_PRIORITY + 2).coerceAtMost(Thread.MAX_PRIORITY)
            start()
        }
    }

    private fun runDecodeLoop() {
        try {
            Log.d(TAG, "Connecting video socket to $host:$port...")
            val s = Socket()
            s.tcpNoDelay = true
            s.receiveBufferSize = 1024 * 1024
            s.connect(InetSocketAddress(host, port), 4000)
            socket = s

            val inputStream = BufferedInputStream(s.getInputStream(), 512 * 1024)
            Log.d(TAG, "Video socket connected. Reading handshake...")

            // Read Handshake: 16 bytes: ['P', 'M', 'O', 'N'] [width: int32] [height: int32] [fps: int32]
            val handshake = ByteArray(16)
            readExact(inputStream, handshake, 0, 16)

            val bb = ByteBuffer.wrap(handshake).order(ByteOrder.LITTLE_ENDIAN)
            val magic = String(handshake, 0, 4)
            if (magic != "PMON") {
                throw IllegalStateException("Invalid handshake magic: $magic")
            }

            val width = bb.getInt(4).coerceAtLeast(320)
            val height = bb.getInt(8).coerceAtLeast(240)
            val targetFps = bb.getInt(12).coerceAtLeast(30)

            Log.d(TAG, "Handshake received: ${width}x${height} @ ${targetFps} FPS")

            initMediaCodec(width, height)
            statsListener?.onConnectionState(true)

            // Video loop variables
            val headerBuf = ByteArray(20)
            val headerBb = ByteBuffer.wrap(headerBuf).order(ByteOrder.LITTLE_ENDIAN)
            var framePayload = ByteArray(1024 * 512)

            var frameCount = 0
            var byteCount = 0L
            var lastStatsTime = System.currentTimeMillis()

            val bufferInfo = MediaCodec.BufferInfo()

            while (isRunning.get() && !Thread.currentThread().isInterrupted) {
                // Read 20-byte packet header
                readExact(inputStream, headerBuf, 0, 20)
                if (headerBuf[0] != 'V'.code.toByte() || headerBuf[1] != 'I'.code.toByte() ||
                    headerBuf[2] != 'D'.code.toByte() || headerBuf[3] != '0'.code.toByte()) {
                    continue
                }

                val payloadLen = headerBb.getInt(4)
                val isKeyFrame = headerBuf[8].toInt() == 1
                val timestamp = headerBb.getLong(12)

                if (payloadLen <= 0 || payloadLen > 10 * 1024 * 1024) continue

                if (framePayload.size < payloadLen) {
                    framePayload = ByteArray(payloadLen + 65536)
                }

                readExact(inputStream, framePayload, 0, payloadLen)

                // Feed to MediaCodec
                val codec = decoder ?: break
                val inputIndex = codec.dequeueInputBuffer(10000)
                if (inputIndex >= 0) {
                    val inputBuffer = codec.getInputBuffer(inputIndex)
                    if (inputBuffer != null) {
                        inputBuffer.clear()
                        inputBuffer.put(framePayload, 0, payloadLen)

                        var flags = 0
                        if (isKeyFrame) {
                            flags = flags or MediaCodec.BUFFER_FLAG_KEY_FRAME
                        }
                        codec.queueInputBuffer(inputIndex, 0, payloadLen, timestamp, flags)
                    }
                }

                // Render output buffers
                var outIndex = codec.dequeueOutputBuffer(bufferInfo, 0)
                while (outIndex >= 0) {
                    codec.releaseOutputBuffer(outIndex, true)
                    frameCount++
                    outIndex = codec.dequeueOutputBuffer(bufferInfo, 0)
                }

                byteCount += 20 + payloadLen
                val now = System.currentTimeMillis()
                if (now - lastStatsTime >= 1000) {
                    val elapsedSec = (now - lastStatsTime) / 1000.0
                    val fps = (frameCount / elapsedSec).toInt()
                    val mbps = (byteCount * 8.0) / (elapsedSec * 1_000_000.0)

                    statsListener?.onStatsUpdated(fps, mbps)

                    frameCount = 0
                    byteCount = 0L
                    lastStatsTime = now
                }
            }

        } catch (e: Exception) {
            Log.e(TAG, "Video decode error: ${e.message}")
            statsListener?.onConnectionState(false, e.message)
        } finally {
            close()
        }
    }

    private fun initMediaCodec(width: Int, height: Int) {
        if (!surface.isValid) {
            Log.e(TAG, "Cannot initialize MediaCodec: Surface is not valid.")
            return
        }

        val format = MediaFormat.createVideoFormat("video/avc", width, height).apply {
            setInteger(MediaFormat.KEY_COLOR_FORMAT, android.media.MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            }
        }

        val codec = MediaCodec.createDecoderByType("video/avc")
        codec.configure(format, surface, null, 0)
        codec.start()
        decoder = codec
        Log.d(TAG, "MediaCodec started successfully with surface.")
    }

    private fun readExact(stream: InputStream, buffer: ByteArray, offset: Int, length: Int) {
        var total = 0
        while (total < length) {
            val count = stream.read(buffer, offset + total, length - total)
            if (count < 0) throw IllegalStateException("End of stream reached")
            total += count
        }
    }

    fun close() {
        isRunning.set(false)
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null

        try {
            decoder?.stop()
            decoder?.release()
        } catch (_: Exception) {}
        decoder = null

        workerThread?.interrupt()
        workerThread = null
    }

    companion object {
        private const val TAG = "MediaCodecDecoder"
    }
}
