package com.phonemonitor.client.touch

import android.util.Log
import java.io.BufferedOutputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.atomic.AtomicBoolean

class TouchTransmitter(
    private val host: String,
    private val port: Int = 59101
) {
    private val isRunning = AtomicBoolean(false)
    private var socket: Socket? = null
    private var outputStream: OutputStream? = null
    private val sendQueue = LinkedBlockingQueue<List<RawTouchPoint>>(64)
    private var workerThread: Thread? = null

    fun start() {
        if (isRunning.getAndSet(true)) return

        workerThread = Thread({
            runNetworkLoop()
        }, "TouchTransmitterThread").apply {
            priority = (Thread.NORM_PRIORITY + 2).coerceAtMost(Thread.MAX_PRIORITY)
            start()
        }
    }

    private fun runNetworkLoop() {
        val buffer = ByteBuffer.allocate(256).order(ByteOrder.LITTLE_ENDIAN)

        while (isRunning.get() && !Thread.currentThread().isInterrupted) {
            try {
                Log.d(TAG, "Connecting touch socket to $host:$port...")
                val s = Socket()
                s.tcpNoDelay = true
                s.connect(InetSocketAddress(host, port), 3000)
                socket = s
                outputStream = BufferedOutputStream(s.getOutputStream(), 1024)
                Log.d(TAG, "Touch socket connected successfully.")

                while (isRunning.get() && !Thread.currentThread().isInterrupted) {
                    val points = sendQueue.take()
                    if (points.isEmpty()) continue

                    val count = points.size.coerceAtMost(10)
                    buffer.clear()
                    buffer.put(0x54.toByte()) // 'T'
                    buffer.put(count.toByte())

                    for (i in 0 until count) {
                        val pt = points[i]
                        buffer.putInt(pt.pointerId)
                        buffer.put(pt.action)
                        buffer.putFloat(pt.normX)
                        buffer.putFloat(pt.normY)
                        buffer.putFloat(pt.pressure)
                        buffer.put(0.toByte()) // flags
                    }

                    outputStream?.write(buffer.array(), 0, buffer.position())
                    outputStream?.flush()
                }
            } catch (e: Exception) {
                Log.w(TAG, "Touch connection error: ${e.message}")
                closeSocket()
                if (isRunning.get()) {
                    try {
                        Thread.sleep(2000)
                    } catch (_: InterruptedException) {
                        break
                    }
                }
            } finally {
                closeSocket()
            }
        }
    }

    private fun closeSocket() {
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null
        outputStream = null
    }

    fun queueTouchPoints(points: List<RawTouchPoint>) {
        if (!isRunning.get()) return
        // Drop oldest if queue is full to avoid input latency build-up
        if (sendQueue.remainingCapacity() == 0) {
            sendQueue.poll()
        }
        sendQueue.offer(points)
    }

    fun close() {
        isRunning.set(false)
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null
        outputStream = null
        workerThread?.interrupt()
        workerThread = null
    }

    companion object {
        private const val TAG = "TouchTransmitter"
    }
}
