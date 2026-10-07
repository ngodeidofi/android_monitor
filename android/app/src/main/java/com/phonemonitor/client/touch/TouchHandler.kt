package com.phonemonitor.client.touch

import android.view.MotionEvent
import android.view.View

class TouchHandler(
    private val touchTransmitter: TouchTransmitter
) : View.OnTouchListener {

    @Volatile
    private var viewportLeft: Float = 0f
    @Volatile
    private var viewportTop: Float = 0f
    @Volatile
    private var viewportWidth: Float = 0f
    @Volatile
    private var viewportHeight: Float = 0f

    fun updateViewport(left: Float, top: Float, width: Float, height: Float) {
        viewportLeft = left
        viewportTop = top
        viewportWidth = width
        viewportHeight = height
    }

    override fun onTouch(v: View, event: MotionEvent): Boolean {
        val totalWidth = v.width.toFloat()
        val totalHeight = v.height.toFloat()
        if (totalWidth <= 0 || totalHeight <= 0) return false

        val vLeft = viewportLeft
        val vTop = viewportTop
        val vWidth = if (viewportWidth > 0f) viewportWidth else totalWidth
        val vHeight = if (viewportHeight > 0f) viewportHeight else totalHeight

        val actionMasked = event.actionMasked
        val actionIndex = event.actionIndex
        val pointerCount = event.pointerCount

        // Helper to normalize coordinates relative to the actual video viewport
        fun normX(px: Float): Float = ((px - vLeft) / vWidth).coerceIn(0f, 1f)
        fun normY(py: Float): Float = ((py - vTop) / vHeight).coerceIn(0f, 1f)

        // For ACTION_DOWN or ACTION_POINTER_DOWN, check if touch begins inside letterbox padding
        if (actionMasked == MotionEvent.ACTION_DOWN || actionMasked == MotionEvent.ACTION_POINTER_DOWN) {
            val px = event.getX(actionIndex)
            val py = event.getY(actionIndex)
            if (px < vLeft || px > vLeft + vWidth || py < vTop || py > vTop + vHeight) {
                // Ignore taps on black letterbox/pillarbox bars
                return false
            }
        }

        val points = ArrayList<RawTouchPoint>(pointerCount)

        when (actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val pid = event.getPointerId(actionIndex)
                val nx = normX(event.getX(actionIndex))
                val ny = normY(event.getY(actionIndex))
                val p = event.getPressure(actionIndex).coerceIn(0f, 1f)
                points.add(RawTouchPoint(pid, 0.toByte() /* DOWN */, nx, ny, p))
            }

            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until pointerCount) {
                    val pid = event.getPointerId(i)
                    val nx = normX(event.getX(i))
                    val ny = normY(event.getY(i))
                    val p = event.getPressure(i).coerceIn(0f, 1f)
                    points.add(RawTouchPoint(pid, 1.toByte() /* MOVE */, nx, ny, p))
                }
            }

            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val pid = event.getPointerId(actionIndex)
                val nx = normX(event.getX(actionIndex))
                val ny = normY(event.getY(actionIndex))
                val p = event.getPressure(actionIndex).coerceIn(0f, 1f)
                points.add(RawTouchPoint(pid, 2.toByte() /* UP */, nx, ny, p))
            }

            MotionEvent.ACTION_CANCEL -> {
                for (i in 0 until pointerCount) {
                    val pid = event.getPointerId(i)
                    val nx = normX(event.getX(i))
                    val ny = normY(event.getY(i))
                    val p = event.getPressure(i).coerceIn(0f, 1f)
                    points.add(RawTouchPoint(pid, 3.toByte() /* CANCEL */, nx, ny, p))
                }
            }
        }

        if (points.isNotEmpty()) {
            touchTransmitter.queueTouchPoints(points)
        }

        return true
    }
}
