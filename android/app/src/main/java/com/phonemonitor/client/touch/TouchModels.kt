package com.phonemonitor.client.touch

data class RawTouchPoint(
    val pointerId: Int,
    val action: Byte, // 0: DOWN, 1: MOVE, 2: UP, 3: CANCEL
    val normX: Float,
    val normY: Float,
    val pressure: Float
)
