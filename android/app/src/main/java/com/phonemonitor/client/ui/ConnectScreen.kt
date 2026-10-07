package com.phonemonitor.client.ui

import android.content.Context
import android.content.Intent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun ConnectScreen(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var isUsbMode by remember { mutableStateOf(true) }
    var ipAddress by remember { mutableStateOf("192.168.1.100") }
    var videoPort by remember { mutableStateOf("59100") }
    var touchPort by remember { mutableStateOf("59101") }

    val scrollState = rememberScrollState()

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(Color(0xFF0F1117))
            .padding(24.dp)
            .verticalScroll(scrollState),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(modifier = Modifier.height(16.dp))

        // App Logo & Header
        Text(
            text = "📱 PhoneMonitor",
            fontSize = 28.sp,
            fontWeight = FontWeight.Bold,
            color = Color.White
        )
        Text(
            text = "High-Performance USB Second Display & Touch Digitizer",
            fontSize = 13.sp,
            color = Color(0xFF94A3B8),
            modifier = Modifier.padding(top = 4.dp, bottom = 24.dp)
        )

        // Connection Mode Selector
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(Color(0xFF1A1D27), RoundedCornerShape(12.dp))
                .padding(4.dp),
            horizontalArrangement = Arrangement.SpaceEvenly
        ) {
            Button(
                onClick = { isUsbMode = true },
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isUsbMode) Color(0xFF4F46E5) else Color.Transparent,
                    contentColor = Color.White
                ),
                shape = RoundedCornerShape(8.dp),
                modifier = Modifier.weight(1f)
            ) {
                Text(text = "🔌 USB Cable (ADB)", fontWeight = FontWeight.SemiBold)
            }

            Button(
                onClick = { isUsbMode = false },
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (!isUsbMode) Color(0xFF4F46E5) else Color.Transparent,
                    contentColor = Color.White
                ),
                shape = RoundedCornerShape(8.dp),
                modifier = Modifier.weight(1f)
            ) {
                Text(text = "📶 Wi-Fi / IP", fontWeight = FontWeight.SemiBold)
            }
        }

        Spacer(modifier = Modifier.height(20.dp))

        if (isUsbMode) {
            // USB Guide Card
            Card(
                colors = CardDefaults.cardColors(containerColor = Color(0xFF1A1D27)),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(18.dp)) {
                    Text(
                        text = "USB Connection Instructions",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(0xFFE2E8F0)
                    )
                    Spacer(modifier = Modifier.height(12.dp))

                    InstructionStep(number = "1", text = "Connect phone to PC with USB cable.")
                    InstructionStep(number = "2", text = "Ensure USB Debugging is ON in Settings > Developer Options.")
                    InstructionStep(number = "3", text = "On Windows PhoneMonitor Server, click 'Setup USB Tunnel' and 'Start Streaming'.")
                    InstructionStep(number = "4", text = "Tap 'Connect to Monitor' below!")
                }
            }
        } else {
            // IP Settings Card
            Card(
                colors = CardDefaults.cardColors(containerColor = Color(0xFF1A1D27)),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(18.dp)) {
                    Text(
                        text = "Network Settings",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color(0xFFE2E8F0),
                        modifier = Modifier.padding(bottom = 12.dp)
                    )

                    OutlinedTextField(
                        value = ipAddress,
                        onValueChange = { ipAddress = it },
                        label = { Text("PC IP Address") },
                        modifier = Modifier.fillMaxWidth(),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedTextColor = Color.White,
                            unfocusedTextColor = Color.White,
                            focusedBorderColor = Color(0xFF4F46E5),
                            unfocusedBorderColor = Color(0xFF2E3446)
                        )
                    )

                    Spacer(modifier = Modifier.height(12.dp))

                    Row(modifier = Modifier.fillMaxWidth()) {
                        OutlinedTextField(
                            value = videoPort,
                            onValueChange = { videoPort = it },
                            label = { Text("Video Port") },
                            modifier = Modifier.weight(1f).padding(end = 8.dp),
                            colors = OutlinedTextFieldDefaults.colors(
                                focusedTextColor = Color.White,
                                unfocusedTextColor = Color.White,
                                focusedBorderColor = Color(0xFF4F46E5),
                                unfocusedBorderColor = Color(0xFF2E3446)
                            )
                        )

                        OutlinedTextField(
                            value = touchPort,
                            onValueChange = { touchPort = it },
                            label = { Text("Touch Port") },
                            modifier = Modifier.weight(1f).padding(start = 8.dp),
                            colors = OutlinedTextFieldDefaults.colors(
                                focusedTextColor = Color.White,
                                unfocusedTextColor = Color.White,
                                focusedBorderColor = Color(0xFF4F46E5),
                                unfocusedBorderColor = Color(0xFF2E3446)
                            )
                        )
                    }
                }
            }
        }

        Spacer(modifier = Modifier.height(24.dp))

        // Connect Button
        Button(
            onClick = {
                val targetHost = if (isUsbMode) "127.0.0.1" else ipAddress.trim()
                val vPort = videoPort.toIntOrNull() ?: 59100
                val tPort = touchPort.toIntOrNull() ?: 59101
                launchMonitor(context, targetHost, vPort, tPort)
            },
            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4F46E5)),
            shape = RoundedCornerShape(10.dp),
            modifier = Modifier
                .fillMaxWidth()
                .height(54.dp)
        ) {
            Text(
                text = "▶  Connect to Monitor",
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = Color.White
            )
        }

        Spacer(modifier = Modifier.height(20.dp))

        // Features badge card
        Card(
            colors = CardDefaults.cardColors(containerColor = Color(0xFF141722)),
            shape = RoundedCornerShape(12.dp),
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text(
                    text = "Capabilities",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(0xFF94A3B8),
                    modifier = Modifier.padding(bottom = 8.dp)
                )

                CapabilityItem("Native Windows Multi-Touch (Pinch-to-zoom, 10-finger gestures)")
                CapabilityItem("Low-Latency MediaCodec H.264 Hardware Acceleration")
                CapabilityItem("Direct USB 3.0 / 2.0 ADB Zero-Lag Tunnel")
                CapabilityItem("Virtual Display Driver (IddCx) genuine secondary screen")
            }
        }
    }
}

@Composable
fun InstructionStep(number: String, text: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp),
        verticalAlignment = Alignment.Top
    ) {
        Box(
            modifier = Modifier
                .size(22.dp)
                .background(Color(0xFF2E3446), RoundedCornerShape(11.dp)),
            contentAlignment = Alignment.Center
        ) {
            Text(text = number, color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold)
        }
        Spacer(modifier = Modifier.width(10.dp))
        Text(text = text, color = Color(0xFFCBD5E1), fontSize = 13.sp)
    }
}

@Composable
fun CapabilityItem(text: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = "✓",
            color = Color(0xFF10B981),
            fontWeight = FontWeight.Bold,
            fontSize = 14.sp
        )
        Spacer(modifier = Modifier.width(8.dp))
        Text(text = text, color = Color(0xFF94A3B8), fontSize = 12.sp)
    }
}

private fun launchMonitor(context: Context, host: String, videoPort: Int, touchPort: Int) {
    try {
        val intent = Intent(context, MonitorActivity::class.java).apply {
            putExtra(MonitorActivity.EXTRA_HOST, host)
            putExtra(MonitorActivity.EXTRA_VIDEO_PORT, videoPort)
            putExtra(MonitorActivity.EXTRA_TOUCH_PORT, touchPort)
        }
        context.startActivity(intent)
    } catch (e: Throwable) {
        android.widget.Toast.makeText(context, "Cannot open monitor: ${e.message}", android.widget.Toast.LENGTH_LONG).show()
    }
}
