# PhoneMonitor 📱🖥️

Turn your Android phone or tablet into an ultra-low latency second monitor for Windows over USB, with native Windows multi-touch digitizer support.

![Status](https://img.shields.io/badge/Windows-10%20%7C%2011-blue)
![Platform](https://img.shields.io/badge/Android-8.0%2B-green)
![Connection](https://img.shields.io/badge/Connection-USB%20ADB%20%7C%20Wi--Fi-orange)
![Latency](https://img.shields.io/badge/Latency-%3C25ms-brightgreen)

---

## ✨ Features

- **⚡ Zero-Lag USB Streaming**: Powered by DirectX 11 Desktop Duplication (`IDXGIOutputDuplication`) and hardware-accelerated H.264 encoding via Windows Media Foundation over USB 2.0 / 3.0 ADB tunnels.
- **👆 True Windows Touch Screen**: Uses native Win32 `InitializeTouchInjection` and `InjectTouchInput` APIs. Windows recognizes your phone as a genuine capacitive touch screen with support for pinch-to-zoom, two-finger scrolling, right-click, and Windows Ink.
- **🖥️ Genuine Secondary Monitor**: Integrates with the open-source Windows Indirect Display Driver (IddCx) so the phone acts as an independent extended display in Windows Display Settings (`Win + P`).
- **📱 Hardware Decoding**: Android client utilizes `MediaCodec` low-latency hardware decoding (`video/avc`) rendering directly to a fullscreen `SurfaceView`.
- **🔄 Auto-Configuration**: Automated ADB detection, one-click USB reverse tunneling, and automatic monitor discovery.

---

## 📂 Repository Structure

```
phone-monitor/
├── windows/
│   └── PhoneMonitor.Server/          # .NET 8 WPF Server application
│       ├── Capture/                   # DXGI Desktop Duplication & Display Enumerator
│       ├── Encoding/                  # Media Foundation H.264 Hardware Encoder
│       ├── Network/                   # ADB Manager & TCP Streaming Server
│       ├── Touch/                     # Win32 Native Touch Injection
│       └── MainWindow.xaml            # Modern Dark UI Control Panel
├── android/
│   └── app/src/main/java/.../         # Android Client (Kotlin & Jetpack Compose)
│       ├── decoder/                   # Low-Latency MediaCodec Video Decoder
│       ├── touch/                     # MotionEvent Multi-Touch Normalizer & TCP Transmitter
│       └── ui/                        # Fullscreen Monitor & Setup UI
├── drivers/
│   └── VirtualDisplay/                # IddCx Virtual Display Driver Installer & Settings
└── docs/
    ├── ARCHITECTURE.md                # Comprehensive Technical Architecture & Protocols
    └── SETUP_GUIDE.md                 # Step-by-Step Setup & Quickstart Guide
```

---

## 🚀 Quick Start

For detailed step-by-step instructions, see the [Setup Guide](docs/SETUP_GUIDE.md).

### 1. Windows Installer (Recommended)
Download and run the standalone installer:
- **Installer File**: [`installer/output/PhoneMonitor-Setup-1.0.0.exe`](installer/output/PhoneMonitor-Setup-1.0.0.exe)
- **Automatic Setup**:
  - Automatically copies all server binaries and bundled Android platform-tools (`adb`).
  - Automatically installs and activates the IddCx Virtual Secondary Display Driver locally.
  - Automatically configures Windows Firewall rules.
  - Creates Start Menu and Desktop shortcuts.
- **Uninstallation**:
  - Automatically registered in Windows **Settings &rarr; Apps &rarr; Installed apps** and **Control Panel &rarr; Programs and Features**.
  - Cleanly uninstalls the virtual driver from Device Manager, restores display mode, and deletes files.

To rebuild the installer from source at any time:
```powershell
.\installer\Build-Installer.ps1
```

### 2. Manual Development Mode
```powershell
cd windows\PhoneMonitor.Server
dotnet run
```

### 3. Android Client
Install [`installer/payload/apk/PhoneMonitor.apk`](installer/payload/apk/PhoneMonitor.apk) onto your device:
```powershell
adb install -r android\app\build\outputs\apk\debug\app-debug.apk
```

---

## 📄 License
MIT License.
