using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneMonitor.Server.Network
{
    public class AdbDevice
    {
        public string Serial { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty; // "device", "unauthorized", "offline"
        public string Model { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;

        public bool IsAuthorized => State.Equals("device", StringComparison.OrdinalIgnoreCase);

        public override string ToString()
        {
            string friendly = !string.IsNullOrWhiteSpace(Model) ? Model : Serial;
            return $"{friendly} [{State}]";
        }
    }

    public class AdbManager
    {
        private string? _adbPath;
        private Timer? _pollTimer;
        public event EventHandler<List<AdbDevice>>? DevicesChanged;

        public string? AdbPath => _adbPath;
        public bool IsAdbAvailable => !string.IsNullOrEmpty(_adbPath) && File.Exists(_adbPath);

        public AdbManager()
        {
            LocateAdb();
        }

        public void StartDeviceMonitoring(int intervalMs = 2500)
        {
            _pollTimer?.Dispose();
            _pollTimer = new Timer(async _ =>
            {
                if (IsAdbAvailable)
                {
                    var devices = await GetConnectedDevicesAsync();
                    DevicesChanged?.Invoke(this, devices);
                }
            }, null, 0, intervalMs);
        }

        public void StopDeviceMonitoring()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        public void LocateAdb()
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe"),
                @"C:\Users\AHNi-HI\AppData\Local\Android\Sdk\platform-tools\adb.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe")
            };

            var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
            if (!string.IsNullOrEmpty(androidHome))
            {
                candidates.Insert(0, Path.Combine(androidHome, "platform-tools", "adb.exe"));
            }

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    _adbPath = path;
                    return;
                }
            }

            // Check PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var segment in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(segment.Trim(), "adb.exe");
                    if (File.Exists(full))
                    {
                        _adbPath = full;
                        return;
                    }
                }
                catch { }
            }
        }

        public async Task<List<AdbDevice>> GetConnectedDevicesAsync()
        {
            var list = new List<AdbDevice>();
            if (!IsAdbAvailable) return list;

            string output = await RunAdbCommandAsync("devices -l");
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices") || line.StartsWith("*")) continue;

                var match = Regex.Match(line, @"^(\S+)\s+(\S+)(.*)$");
                if (match.Success)
                {
                    string serial = match.Groups[1].Value;
                    string state = match.Groups[2].Value;
                    string extra = match.Groups[3].Value;

                    string model = "";
                    string product = "";

                    var modelMatch = Regex.Match(extra, @"model:(\S+)");
                    if (modelMatch.Success) model = modelMatch.Groups[1].Value.Replace('_', ' ');

                    var productMatch = Regex.Match(extra, @"product:(\S+)");
                    if (productMatch.Success) product = productMatch.Groups[1].Value;

                    list.Add(new AdbDevice
                    {
                        Serial = serial,
                        State = state,
                        Model = model,
                        Product = product
                    });
                }
            }

            return list;
        }

        public async Task<bool> SetupUsbReverseTunnelAsync(string serial, int videoPort = 59100, int touchPort = 59101)
        {
            if (!IsAdbAvailable) return false;

            string out1 = await RunAdbCommandAsync($"-s {serial} reverse tcp:{videoPort} tcp:{videoPort}");
            string out2 = await RunAdbCommandAsync($"-s {serial} reverse tcp:{touchPort} tcp:{touchPort}");

            Debug.WriteLine($"ADB reverse video: {out1.Trim()}, touch: {out2.Trim()}");
            return !out1.Contains("error") && !out2.Contains("error");
        }

        public async Task<bool> SetupUsbForwardTunnelAsync(string serial, int videoPort = 59100, int touchPort = 59101)
        {
            if (!IsAdbAvailable) return false;

            string out1 = await RunAdbCommandAsync($"-s {serial} forward tcp:{videoPort} tcp:{videoPort}");
            string out2 = await RunAdbCommandAsync($"-s {serial} forward tcp:{touchPort} tcp:{touchPort}");

            return !out1.Contains("error") && !out2.Contains("error");
        }

        public async Task<bool> LaunchMonitorAppAsync(string serial, string host = "127.0.0.1", int videoPort = 59100, int touchPort = 59101)
        {
            if (!IsAdbAvailable) return false;

            // Wake up display if asleep
            await RunAdbCommandAsync($"-s {serial} shell input keyevent KEYCODE_WAKEUP");

            // Direct intent launch into MonitorActivity with connection parameters
            string cmd = $"-s {serial} shell am start -n com.phonemonitor.client/com.phonemonitor.client.ui.MonitorActivity " +
                         $"--es extra_host {host} --ei extra_video_port {videoPort} --ei extra_touch_port {touchPort}";
            string output = await RunAdbCommandAsync(cmd);
            return !output.Contains("Error", StringComparison.OrdinalIgnoreCase) && !output.Contains("Exception", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> ReturnToHomeAsync(string serial)
        {
            if (!IsAdbAvailable) return false;

            // Bring MainActivity to the front with clear-top flag or simulate home
            string cmd = $"-s {serial} shell am start -n com.phonemonitor.client/com.phonemonitor.client.MainActivity --activity-clear-top";
            await RunAdbCommandAsync(cmd);
            return true;
        }

        public async Task<string> RunAdbCommandAsync(string arguments)
        {
            if (!IsAdbAvailable) return string.Empty;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _adbPath!,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;

                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return output;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Adb command error: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
