using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PhoneMonitor.Server.Touch;

namespace PhoneMonitor.Server.Capture
{
    public class MonitorInfo
    {
        public int Index { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public bool IsVirtual { get; set; }
        public RECT Bounds { get; set; }
        public int Width => Bounds.Width;
        public int Height => Bounds.Height;

        public override string ToString()
        {
            string tag = IsVirtual ? " [VIRTUAL DISPLAY]" : (IsPrimary ? " [PRIMARY]" : "");
            return $"{Index}: {FriendlyName} ({Width}x{Height}){tag}";
        }
    }

    public static class DisplayManager
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            int index = 0;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    string devName = mi.szDevice ?? $"Display{index + 1}";
                    string friendlyName = devName;
                    bool isVirtual = false;

                    // Query display device info for friendly string and hardware ID
                    var dd = new DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                    if (EnumDisplayDevices(devName, 0, ref dd, 0))
                    {
                        if (!string.IsNullOrWhiteSpace(dd.DeviceString))
                        {
                            friendlyName = dd.DeviceString;
                        }

                        string idLower = (dd.DeviceID ?? "").ToLowerInvariant();
                        string strLower = (dd.DeviceString ?? "").ToLowerInvariant();

                        // Detect Virtual Display Drivers (IddCx, VDD, Amyuni, Parsec, Virtual)
                        if (idLower.Contains("idd") || idLower.Contains("vdd") || idLower.Contains("virtual") ||
                            strLower.Contains("virtual") || strLower.Contains("idd") || strLower.Contains("usb display"))
                        {
                            isVirtual = true;
                        }
                    }

                    list.Add(new MonitorInfo
                    {
                        Index = index++,
                        DeviceName = devName,
                        FriendlyName = friendlyName,
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        IsVirtual = isVirtual,
                        Bounds = mi.rcMonitor
                    });
                }
                return true;
            }, IntPtr.Zero);

            return list;
        }
    }
}
