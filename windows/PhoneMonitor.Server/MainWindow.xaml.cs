using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PhoneMonitor.Server.Capture;
using PhoneMonitor.Server.Encoding;
using PhoneMonitor.Server.Network;
using PhoneMonitor.Server.Touch;

namespace PhoneMonitor.Server
{
    public partial class MainWindow : Window
    {
        private readonly AdbManager _adbManager;
        private readonly TouchManager _touchManager;
        private readonly ServerStreamManager _streamManager;
        private ScreenCapture? _screenCapture;
        private VideoEncoder? _encoder;
        private DispatcherTimer? _uiUpdateTimer;

        private List<MonitorInfo> _monitors = new();
        private MonitorInfo? _selectedMonitor;
        private bool _isStreaming = false;

        public MainWindow()
        {
            InitializeComponent();

            _touchManager = new TouchManager();
            _streamManager = new ServerStreamManager(_touchManager);
            _adbManager = new AdbManager();

            SetupEvents();
            InitServer();
        }

        private void SetupEvents()
        {
            _streamManager.LogMessageReceived += (s, msg) =>
            {
                Dispatcher.Invoke(() => AppendLog(msg));
            };

            _streamManager.ClientConnectionChanged += (s, connected) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (connected)
                    {
                        TxtStatusBadge.Text = "CLIENT STREAMING";
                        TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                        AppendLog("Android Client connected and streaming active.");
                    }
                    else
                    {
                        TxtStatusBadge.Text = _isStreaming ? "LISTENING" : "READY";
                        TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        AppendLog("Android Client disconnected.");
                    }
                });
            };

            _adbManager.DevicesChanged += (s, devices) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ComboDevices.ItemsSource = null;
                    ComboDevices.ItemsSource = devices;
                    if (devices.Count > 0 && ComboDevices.SelectedIndex < 0)
                    {
                        ComboDevices.SelectedIndex = 0;
                    }
                });
            };
        }

        private void InitServer()
        {
            RefreshMonitors();
            RefreshAdbStatus();

            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _uiUpdateTimer.Tick += (s, e) =>
            {
                TxtFpsMetric.Text = $"{_streamManager.CurrentFps} FPS";
                TxtBitrateMetric.Text = $"{_streamManager.CurrentMbps:F1} Mbps";
                TxtTouchStats.Text = $"Injected Touch Events: {_touchManager.InjectedTouchCount:N0}";
            };
            _uiUpdateTimer.Start();

            _adbManager.StartDeviceMonitoring();
            AppendLog("PhoneMonitor Server initialized. Ready for USB connection.");
        }

        private void RefreshMonitors()
        {
            _monitors = DisplayManager.GetMonitors();
            ComboMonitors.ItemsSource = null;
            ComboMonitors.ItemsSource = _monitors;

            if (_monitors.Count > 0)
            {
                // Prefer virtual display if detected, otherwise secondary, then primary
                var preferred = _monitors.FirstOrDefault(m => m.IsVirtual)
                             ?? _monitors.FirstOrDefault(m => !m.IsPrimary)
                             ?? _monitors[0];

                ComboMonitors.SelectedItem = preferred;
            }
        }

        private void RefreshAdbStatus()
        {
            if (_adbManager.IsAdbAvailable)
            {
                TxtAdbStatus.Text = $"ADB: Ready ({Path.GetFileName(_adbManager.AdbPath)})";
                TxtAdbStatus.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                TxtAdbStatus.Text = "ADB: Not found in default SDK paths. Place adb.exe in app folder or install Android platform-tools.";
                TxtAdbStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }

        private void ComboMonitors_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboMonitors.SelectedItem is MonitorInfo monitor)
            {
                _selectedMonitor = monitor;
                _touchManager.TargetMonitorRect = monitor.Bounds;

                string typeDesc = monitor.IsVirtual ? "Virtual Display Driver" : (monitor.IsPrimary ? "Primary Display" : "Secondary Display");
                TxtDisplayDetails.Text = $"Bounds: {monitor.Bounds.Left},{monitor.Bounds.Top} ({monitor.Width}x{monitor.Height}) | Type: {typeDesc}";

                if (_isStreaming)
                {
                    RestartStream();
                }
            }
        }

        private void BtnRefreshDisplays_Click(object sender, RoutedEventArgs e)
        {
            RefreshMonitors();
            AppendLog("Refreshed system monitors.");
        }

        private void BtnToggleServer_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStreaming)
            {
                StartStreaming();
            }
            else
            {
                StopStreaming();
            }
        }

        private void StartStreaming()
        {
            if (_selectedMonitor == null)
            {
                MessageBox.Show("Please select a target display to stream.", "Display Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int targetFps = ComboFps.SelectedIndex switch
            {
                0 => 30,
                1 => 60,
                2 => 90,
                3 => 120,
                _ => 60
            };

            int quality = ComboBitrate.SelectedIndex switch
            {
                0 => 60,
                1 => 75,
                2 => 85,
                3 => 92,
                _ => 75
            };

            _streamManager.Start();

            _encoder = new VideoEncoder(_selectedMonitor.Width, _selectedMonitor.Height, targetFps, quality);
            _encoder.FrameEncoded += (s, frame) =>
            {
                _streamManager.SendVideoFrame(frame.Data, frame.IsKeyFrame, frame.Timestamp);
            };

            _screenCapture = new ScreenCapture(_selectedMonitor, targetFps);
            _screenCapture.LogMessageReceived += (s, msg) => Dispatcher.Invoke(() => AppendLog(msg));
            _screenCapture.FrameCaptured += (s, frame) =>
            {
                _encoder.EncodeBgraFrame(frame.BgraBuffer, frame.Timestamp);
            };

            _screenCapture.Start();

            _isStreaming = true;
            BtnToggleServer.Content = "⏹ Stop Streaming";
            BtnToggleServer.Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)); // Rose red
            TxtStatusBadge.Text = "LISTENING";
            TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber

            AppendLog($"Streaming started on {_selectedMonitor.FriendlyName} ({_selectedMonitor.Width}x{_selectedMonitor.Height}) at {targetFps} FPS.");

            // Auto-launch monitor app on connected USB device without requiring manual button click
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_adbManager.IsAdbAvailable)
                    {
                        var devices = await _adbManager.GetConnectedDevicesAsync();
                        var target = devices.FirstOrDefault(d => d.IsAuthorized);
                        if (target != null)
                        {
                            await _adbManager.SetupUsbReverseTunnelAsync(target.Serial);
                            bool launched = await _adbManager.LaunchMonitorAppAsync(target.Serial);
                            if (launched)
                            {
                                Dispatcher.Invoke(() => AppendLog($"Auto-launched PhoneMonitor on device ({target.Model})."));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"Auto-launch notice: {ex.Message}"));
                }
            });
        }

        private void StopStreaming()
        {
            _screenCapture?.Stop();
            _screenCapture?.Dispose();
            _screenCapture = null;

            _encoder?.Dispose();
            _encoder = null;

            _streamManager.Stop();

            _isStreaming = false;
            BtnToggleServer.Content = "▶ Start Streaming";
            BtnToggleServer.Background = (SolidColorBrush)FindResource("AccentColor");
            TxtStatusBadge.Text = "READY";
            TxtStatusBadge.Foreground = (SolidColorBrush)FindResource("SuccessColor");

            AppendLog("Streaming stopped.");

            // Return mobile app to home screen on stop streaming
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_adbManager.IsAdbAvailable)
                    {
                        var devices = await _adbManager.GetConnectedDevicesAsync();
                        var target = devices.FirstOrDefault(d => d.IsAuthorized);
                        if (target != null)
                        {
                            await _adbManager.ReturnToHomeAsync(target.Serial);
                        }
                    }
                }
                catch { }
            });
        }

        private void RestartStream()
        {
            if (_isStreaming)
            {
                StopStreaming();
                StartStreaming();
            }
        }

        private async void BtnAutoSetupUsb_Click(object sender, RoutedEventArgs e)
        {
            if (!_adbManager.IsAdbAvailable)
            {
                MessageBox.Show("ADB could not be found. Please ensure Android platform-tools are installed.", "ADB Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (ComboDevices.SelectedItem is not AdbDevice device)
            {
                MessageBox.Show("No Android device detected over USB. Please ensure USB Debugging is enabled on your phone and plug in your USB cable.", "Device Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppendLog($"Setting up USB reverse port tunnel for {device.Model} ({device.Serial})...");
            bool ok = await _adbManager.SetupUsbReverseTunnelAsync(device.Serial);
            if (ok)
            {
                AppendLog("✅ USB reverse port tunnel established successfully! The phone can now connect to 127.0.0.1:59100 over USB.");
            }
            else
            {
                AppendLog("⚠️ Could not establish reverse tunnel. Please verify USB debugging permissions on your device.");
            }
        }

        private void BtnSetupVirtualDisplay_Click(object sender, RoutedEventArgs e)
        {
            string driverScript = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\drivers\VirtualDisplay\Install-VirtualDisplay.ps1"));
            if (!File.Exists(driverScript))
            {
                driverScript = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"drivers\VirtualDisplay\Install-VirtualDisplay.ps1"));
            }

            if (File.Exists(driverScript))
            {
                try
                {
                    var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{driverScript}\"",
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    AppendLog("Launched Virtual Display Driver installer with Administrator privileges.");

                    if (proc != null)
                    {
                        Task.Run(async () =>
                        {
                            await proc.WaitForExitAsync();
                            await Task.Delay(1500);
                            Dispatcher.Invoke(() =>
                            {
                                RefreshMonitors();
                                AppendLog("Refreshed displays after driver installation.");
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Error launching driver installer: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"Driver install script not found at {driverScript}.", "Script Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnExtendDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog("Switching Windows display topology to EXTEND (Win + P)...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DisplaySwitch.exe"),
                    Arguments = "/extend",
                    UseShellExecute = true
                });

                Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    Dispatcher.Invoke(() =>
                    {
                        RefreshMonitors();
                        AppendLog("Refreshed displays. Desktop is now in Extend mode.");
                    });
                });
            }
            catch (Exception ex)
            {
                AppendLog($"Error executing DisplaySwitch: {ex.Message}");
            }
        }

        private void BtnOpenDisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:display",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"Error opening Display Settings: {ex.Message}");
            }
        }

        private void TouchConfig_Changed(object sender, RoutedEventArgs e)
        {
            if (_touchManager != null)
            {
                _touchManager.InvertX = ChkInvertX.IsChecked == true;
                _touchManager.InvertY = ChkInvertY.IsChecked == true;
            }
        }

        private void ComboFps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isStreaming) RestartStream();
        }

        private void AppendLog(string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            TxtConsoleLog.AppendText(entry);
            TxtConsoleLog.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopStreaming();
            _uiUpdateTimer?.Stop();
            _adbManager.StopDeviceMonitoring();
            base.OnClosed(e);
        }
    }
}