package com.phonemonitor.client.ui

import android.os.Build
import android.os.Bundle
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import com.phonemonitor.client.decoder.DecoderStatsListener
import com.phonemonitor.client.decoder.ScaleMode
import com.phonemonitor.client.decoder.StreamReceiver
import com.phonemonitor.client.touch.TouchHandler
import com.phonemonitor.client.touch.TouchTransmitter

class MonitorActivity : ComponentActivity(), SurfaceHolder.Callback, DecoderStatsListener {

    private var host: String = "127.0.0.1"
    private var videoPort: Int = 59100
    private var touchPort: Int = 59101

    private var surfaceView: SurfaceView? = null
    private var receiver: StreamReceiver? = null
    private var touchTransmitter: TouchTransmitter? = null
    private var touchHandler: TouchHandler? = null

    private var txtStats: TextView? = null
    private var btnAspect: TextView? = null
    private var hudOverlay: FrameLayout? = null
    private var currentScaleMode: ScaleMode = ScaleMode.FIT

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        try {
            // Keep screen awake
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

            host = intent.getStringExtra(EXTRA_HOST) ?: "127.0.0.1"
            videoPort = intent.getIntExtra(EXTRA_VIDEO_PORT, 59100)
            touchPort = intent.getIntExtra(EXTRA_TOUCH_PORT, 59101)

            val rootLayout = FrameLayout(this)

            surfaceView = SurfaceView(this).apply {
                holder.addCallback(this@MonitorActivity)
            }
            rootLayout.addView(surfaceView, FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT)

            // Stats Overlay HUD
            hudOverlay = FrameLayout(this).apply {
                setPadding(32, 32, 32, 32)
            }
            txtStats = TextView(this).apply {
                text = "Connecting to $host..."
                setTextColor(0xFF10B981.toInt()) // Emerald Green
                textSize = 13f
                setBackgroundColor(0x99000000.toInt())
                setPadding(20, 10, 20, 10)
            }
            val hudParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                leftMargin = 40
                topMargin = 40
            }
            hudOverlay?.addView(txtStats, hudParams)

            // Aspect Ratio Mode Toggle Button (Fit: maintain ratio vs Fill: stretch)
            btnAspect = TextView(this).apply {
                text = "Aspect: Fit (Original)"
                setTextColor(0xFFF8FAFC.toInt())
                textSize = 12f
                setBackgroundColor(0xCC1E293B.toInt())
                setPadding(24, 12, 24, 12)
                setOnClickListener {
                    currentScaleMode = if (currentScaleMode == ScaleMode.FIT) ScaleMode.STRETCH else ScaleMode.FIT
                    receiver?.scaleMode = currentScaleMode
                    text = if (currentScaleMode == ScaleMode.FIT) "Aspect: Fit (Original)" else "Aspect: Fill (Stretched)"
                }
            }
            val aspectParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                gravity = android.view.Gravity.TOP or android.view.Gravity.END
                rightMargin = 40
                topMargin = 40
            }
            hudOverlay?.addView(btnAspect, aspectParams)

            rootLayout.addView(hudOverlay, FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT)

            setContentView(rootLayout)
            hideSystemBars()
            initTouchTransmitter()

        } catch (e: Throwable) {
            val errorView = TextView(this).apply {
                text = "Error starting monitor:\n${e.message}\n\nTap to return."
                setTextColor(0xFFEF4444.toInt())
                textSize = 16f
                setPadding(40, 40, 40, 40)
                setOnClickListener { finish() }
            }
            setContentView(errorView)
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            hideSystemBars()
        }
    }

    private fun hideSystemBars() {
        try {
            androidx.core.view.WindowCompat.setDecorFitsSystemWindows(window, false)
            val controller = androidx.core.view.WindowCompat.getInsetsController(window, window.decorView)
            controller.hide(androidx.core.view.WindowInsetsCompat.Type.systemBars())
            controller.systemBarsBehavior = androidx.core.view.WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        } catch (_: Throwable) {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = (
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                or View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            )
        }
    }

    private fun initTouchTransmitter() {
        try {
            val transmitter = TouchTransmitter(host, touchPort)
            transmitter.start()
            touchTransmitter = transmitter

            val handler = TouchHandler(transmitter)
            touchHandler = handler
            surfaceView?.setOnTouchListener(handler)
        } catch (e: Throwable) {
            txtStats?.text = "Touch init error: ${e.message}"
        }
    }

    override fun surfaceCreated(holder: SurfaceHolder) {
        startDecoder(holder)
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        stopDecoder()
    }

    private fun startDecoder(holder: SurfaceHolder) {
        stopDecoder()
        try {
            receiver = StreamReceiver(host, videoPort, holder, this).apply {
                scaleMode = currentScaleMode
                onViewportChanged = { left, top, width, height ->
                    touchHandler?.updateViewport(left, top, width, height)
                }
            }
            receiver?.start()
        } catch (e: Throwable) {
            txtStats?.text = "Receiver start error: ${e.message}"
        }
    }

    private fun stopDecoder() {
        receiver?.close()
        receiver = null
    }

    override fun onStatsUpdated(fps: Int, mbps: Double) {
        if (isFinishing || isDestroyed) return
        runOnUiThread {
            txtStats?.text = "⚡ $fps FPS | ${"%.1f".format(mbps)} Mbps | Host: $host"
        }
    }

    private var wasConnected: Boolean = false

    override fun onConnectionState(connected: Boolean, errorMsg: String?) {
        if (isFinishing || isDestroyed) return
        runOnUiThread {
            if (connected) {
                wasConnected = true
                // Hide all app-related displays: only computer screen is shown
                hudOverlay?.visibility = View.GONE
            } else {
                if (wasConnected) {
                    // PC stopped streaming: return immediately to mobile app home screen
                    finish()
                } else {
                    txtStats?.text = "Connecting to $host..."
                    hudOverlay?.visibility = View.VISIBLE
                }
            }
        }
    }

    override fun onDestroy() {
        stopDecoder()
        touchTransmitter?.close()
        touchTransmitter = null
        super.onDestroy()
    }

    companion object {
        const val EXTRA_HOST = "extra_host"
        const val EXTRA_VIDEO_PORT = "extra_video_port"
        const val EXTRA_TOUCH_PORT = "extra_touch_port"
    }
}
