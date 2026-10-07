package com.phonemonitor.client.decoder

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.os.Build
import android.util.Log
import android.view.SurfaceHolder
import java.io.BufferedInputStream
import java.io.InputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicBoolean

class StreamReceiver(
    private val host: String,
    private val port: Int = 59100,
    private val surfaceHolder: SurfaceHolder,
    private val statsListener: DecoderStatsListener? = null
) {
    private val isRunning = AtomicBoolean(false)
    private var socket: Socket? = null
    private var workerThread: Thread? = null

    @Volatile
    var scaleMode: ScaleMode = ScaleMode.FIT

    var onViewportChanged: ((left: Float, top: Float, width: Float, height: Float) -> Unit)? = null

    fun start() {
        if (isRunning.getAndSet(true)) return

        workerThread = Thread({
            runAutoReconnectLoop()
        }, "StreamReceiverThread").apply {
            priority = (Thread.NORM_PRIORITY + 2).coerceAtMost(Thread.MAX_PRIORITY)
            start()
        }
    }

    private fun runAutoReconnectLoop() {
        var retryCount = 0

        while (isRunning.get() && !Thread.currentThread().isInterrupted) {
            try {
                retryCount++
                statsListener?.onConnectionState(false, "Connecting to $host:$port (Attempt #$retryCount)...")
                Log.d(TAG, "Connecting video socket to $host:$port (Attempt #$retryCount)...")

                val s = Socket()
                s.tcpNoDelay = true
                s.receiveBufferSize = 512 * 1024
                s.connect(InetSocketAddress(host, port), 3000)
                socket = s

                val inputStream = BufferedInputStream(s.getInputStream(), 256 * 1024)
                Log.d(TAG, "Video socket connected. Reading handshake...")

                // Handshake: 16 bytes: ['P', 'M', 'O', 'N'] [width: int32] [height: int32] [fps: int32]
                val handshake = ByteArray(16)
                readExact(inputStream, handshake, 0, 16)

                val bb = ByteBuffer.wrap(handshake).order(ByteOrder.LITTLE_ENDIAN)
                val magic = String(handshake, 0, 4)
                if (magic != "PMON") {
                    throw IllegalStateException("Invalid handshake magic: $magic")
                }

                val remoteWidth = bb.getInt(4).coerceAtLeast(320)
                val remoteHeight = bb.getInt(8).coerceAtLeast(240)
                val targetFps = bb.getInt(12).coerceAtLeast(30)

                Log.d(TAG, "Handshake OK: ${remoteWidth}x${remoteHeight} @ ${targetFps} FPS")
                statsListener?.onConnectionState(true, null)
                retryCount = 0 // Reset retry counter on success

                // Frame reception loop
                val headerBuf = ByteArray(20)
                val headerBb = ByteBuffer.wrap(headerBuf).order(ByteOrder.LITTLE_ENDIAN)
                var framePayload = ByteArray(256 * 1024)

                var frameCount = 0
                var byteCount = 0L
                var lastStatsTime = System.currentTimeMillis()

                val bitmapOptions = BitmapFactory.Options().apply {
                    inMutable = true
                    inPreferredConfig = Bitmap.Config.RGB_565
                }
                var reusableBitmap: Bitmap? = null
                val destRect = Rect()
                val paint = Paint(Paint.FILTER_BITMAP_FLAG)

                while (isRunning.get() && !Thread.currentThread().isInterrupted) {
                    readExact(inputStream, headerBuf, 0, 20)

                    if (headerBuf[0] != 'V'.code.toByte() || headerBuf[1] != 'I'.code.toByte() ||
                        headerBuf[2] != 'D'.code.toByte() || headerBuf[3] != '0'.code.toByte()) {
                        continue
                    }

                    val payloadLen = headerBb.getInt(4)
                    if (payloadLen <= 0 || payloadLen > 10 * 1024 * 1024) continue

                    if (framePayload.size < payloadLen) {
                        framePayload = ByteArray(payloadLen + 65536)
                    }

                    readExact(inputStream, framePayload, 0, payloadLen)

                    // Stale frame drop: if network has buffered newer frame data, skip decoding this frame
                    if (inputStream.available() >= 20) {
                        continue
                    }

                    // Decode frame
                    bitmapOptions.inBitmap = reusableBitmap
                    var decodedBitmap: Bitmap? = null
                    try {
                        decodedBitmap = BitmapFactory.decodeByteArray(framePayload, 0, payloadLen, bitmapOptions)
                    } catch (_: IllegalArgumentException) {
                        bitmapOptions.inBitmap = null
                        decodedBitmap = BitmapFactory.decodeByteArray(framePayload, 0, payloadLen, bitmapOptions)
                    }

                    if (decodedBitmap != null) {
                        reusableBitmap = decodedBitmap

                        // Render directly to surface
                        val surface = surfaceHolder.surface
                        if (surface.isValid) {
                            val canvas = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                                surfaceHolder.lockHardwareCanvas()
                            } else {
                                surfaceHolder.lockCanvas()
                            }

                            if (canvas != null) {
                                try {
                                    val canvasW = canvas.width.toFloat()
                                    val canvasH = canvas.height.toFloat()
                                    val bmpW = decodedBitmap.width.toFloat()
                                    val bmpH = decodedBitmap.height.toFloat()

                                    val drawLeft: Float
                                    val drawTop: Float
                                    val drawW: Float
                                    val drawH: Float

                                    if (scaleMode == ScaleMode.FIT && bmpW > 0f && bmpH > 0f) {
                                        val scale = minOf(canvasW / bmpW, canvasH / bmpH)
                                        drawW = bmpW * scale
                                        drawH = bmpH * scale
                                        drawLeft = (canvasW - drawW) / 2f
                                        drawTop = (canvasH - drawH) / 2f

                                        destRect.set(
                                            drawLeft.toInt(),
                                            drawTop.toInt(),
                                            (drawLeft + drawW).toInt(),
                                            (drawTop + drawH).toInt()
                                        )

                                        if (drawLeft > 0f || drawTop > 0f) {
                                            canvas.drawColor(Color.BLACK)
                                        }
                                    } else {
                                        drawLeft = 0f
                                        drawTop = 0f
                                        drawW = canvasW
                                        drawH = canvasH
                                        destRect.set(0, 0, canvas.width, canvas.height)
                                    }

                                    canvas.drawBitmap(decodedBitmap, null, destRect, paint)
                                    frameCount++

                                    onViewportChanged?.invoke(drawLeft, drawTop, drawW, drawH)
                                } finally {
                                    surfaceHolder.unlockCanvasAndPost(canvas)
                                }
                            }
                        }
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
                Log.w(TAG, "Video connection interrupted: ${e.message}")
                closeSocket()

                if (isRunning.get()) {
                    if (retryCount == 0) {
                        // Stream was active and now stopped by server: notify and exit
                        statsListener?.onConnectionState(false, "Disconnected")
                        break
                    } else {
                        statsListener?.onConnectionState(false, "Connecting to $host:$port (Attempt #$retryCount)...")
                        try {
                            Thread.sleep(2000)
                        } catch (_: InterruptedException) {
                            break
                        }
                    }
                }
            } finally {
                closeSocket()
            }
        }
    }

    private fun readExact(stream: InputStream, buffer: ByteArray, offset: Int, length: Int) {
        var total = 0
        while (total < length) {
            val count = stream.read(buffer, offset + total, length - total)
            if (count < 0) throw IllegalStateException("End of stream reached")
            total += count
        }
    }

    private fun closeSocket() {
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null
    }

    fun close() {
        isRunning.set(false)
        closeSocket()
        workerThread?.interrupt()
        workerThread = null
    }

    companion object {
        private const val TAG = "StreamReceiver"
    }
}
