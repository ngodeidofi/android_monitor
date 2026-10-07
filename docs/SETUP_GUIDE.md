# PhoneMonitor - Setup & Quickstart Guide

This guide walks you through setting up PhoneMonitor on Windows and your Android device.

---

## 📋 Prerequisites

1. **Windows PC**: Windows 10 (version 1809+) or Windows 11.
2. **Android Phone or Tablet**: Android 8.0 (Oreo) or higher (Android 11+ recommended for lowest latency).
3. **USB Cable**: USB-C or Micro-USB cable connected to your PC.

---

## Step 1: Enable USB Debugging on Android

1. Open **Settings** on your Android device.
2. Go to **About Phone** (or **About Tablet**).
3. Tap **Build Number** 7 times until you see the message *"You are now a developer!"*.
4. Return to Settings, go to **System > Developer Options** (or find Developer Options in Settings).
5. Toggle on **USB Debugging**.
6. When you connect the phone to your PC, a popup will ask *"Allow USB debugging?"* - check **Always allow from this computer** and tap **Allow**.

---

## Step 2: Install Virtual Display Driver on Windows (Recommended)

To allow Windows to detect a true independent secondary monitor:

1. Right-click PowerShell and choose **Run as Administrator**.
2. Run the automated installation script:
   ```powershell
   cd "drivers\VirtualDisplay"
   .\Install-VirtualDisplay.ps1
   ```
3. Once installed, open **Windows Settings > System > Display**.
4. You will see an extra display titled **PhoneMonitor Virtual Display**.
5. Set Multiple Displays to **Extend these displays**, and drag it to your preferred position (e.g. to the right of your primary monitor).

> [!NOTE]
> If you prefer not to install the virtual driver right now, PhoneMonitor can also mirror or capture any existing monitor on your PC for testing!

---

## Step 3: Run Windows PhoneMonitor Server

1. Open a terminal in `windows\PhoneMonitor.Server`.
2. Run:
   ```powershell
   dotnet run
   ```
3. In the PhoneMonitor window:
   - Select your target monitor from the **Target Monitor Selection** dropdown.
   - Verify your Android phone appears in the **Connected Devices** list.
   - Click **⚡ Setup USB Tunnel (ADB Reverse)**.
   - Click **▶ Start Streaming**.

---

## Step 4: Run Android Client

1. Open the Android project in Android Studio, or build the APK via command line:
   ```powershell
   cd android
   .\gradlew.bat installDebug
   ```
2. Launch **PhoneMonitor** on your Android device.
3. Select **🔌 USB Cable (ADB)**.
4. Tap **Connect to Monitor**.
5. Your Android screen will immediately turn into your Windows secondary monitor!

---

## 👆 Touch Screen Gestures on Android

PhoneMonitor translates your fingers into native Windows capacitive touch events:

| Gesture | Action in Windows |
|---|---|
| **Single Finger Tap** | Left Click / Focus window |
| **Single Finger Drag** | Select text, drag windows, draw |
| **Two Finger Tap** | Right Click / Open Context Menu |
| **Two Finger Pinch / Spread** | Pinch-to-zoom (Websites, Photos, Maps) |
| **Two Finger Drag Up / Down** | Smooth scrolling |
| **Tap Text Field** | Windows On-Screen Touch Keyboard automatically appears |
