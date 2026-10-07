using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using PhoneMonitor.Server.Touch;

namespace PhoneMonitor.Server.Capture
{
    public class CapturedFrameEventArgs : EventArgs
    {
        public byte[] BgraBuffer { get; }
        public int Width { get; }
        public int Height { get; }
        public long Timestamp { get; }

        public CapturedFrameEventArgs(byte[] bgraBuffer, int width, int height, long timestamp)
        {
            BgraBuffer = bgraBuffer;
            Width = width;
            Height = height;
            Timestamp = timestamp;
        }
    }

    public class ScreenCapture : IDisposable
    {
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private readonly MonitorInfo _monitor;
        private readonly int _targetFps;

        // DXGI fields
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _stagingTexture;
        private bool _useDxgi = true;

        // Native Fast GDI fields
        private IntPtr _hScreenDc = IntPtr.Zero;
        private IntPtr _hMemDc = IntPtr.Zero;
        private IntPtr _hDibBitmap = IntPtr.Zero;
        private IntPtr _hOldBitmap = IntPtr.Zero;
        private IntPtr _pDibBits = IntPtr.Zero;

        public event EventHandler<CapturedFrameEventArgs>? FrameCaptured;
        public event EventHandler<string>? LogMessageReceived;
        public bool IsRunning { get; private set; }

        public ScreenCapture(MonitorInfo monitor, int targetFps = 60)
        {
            _monitor = monitor;
            _targetFps = targetFps;
        }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();

            TryInitDxgi();

            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _cts?.Cancel();
            try
            {
                _captureTask?.Wait(1000);
            }
            catch { }
            DisposeDxgi();
            DisposeFastGdi();
        }

        private void TryInitDxgi()
        {
            try
            {
                DisposeDxgi();

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                IDXGIAdapter1? matchedAdapter = null;
                IDXGIOutput1? matchedOutput = null;

                // Search across all graphics adapters for the selected display
                for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
                {
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                    {
                        var desc = output.Description;
                        if (string.Equals(desc.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedAdapter = adapter;
                            matchedOutput = output.QueryInterface<IDXGIOutput1>();
                            output.Dispose();
                            break;
                        }
                        output.Dispose();
                    }

                    if (matchedOutput != null)
                    {
                        break;
                    }
                    adapter.Dispose();
                }

                if (matchedAdapter != null && matchedOutput != null)
                {
                    D3D11.D3D11CreateDevice(
                        matchedAdapter,
                        DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                        out _device,
                        out _context).CheckError();

                    matchedAdapter.Dispose();

                    _duplication = matchedOutput.DuplicateOutput(_device);
                    matchedOutput.Dispose();

                    var textureDesc = new Texture2DDescription
                    {
                        Width = (uint)_monitor.Width,
                        Height = (uint)_monitor.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };

                    _stagingTexture = _device.CreateTexture2D(textureDesc);
                    _useDxgi = true;
                    LogMessageReceived?.Invoke(this, $"DXGI Desktop Duplication active for {_monitor.DeviceName} ({_monitor.Width}x{_monitor.Height})");
                    return;
                }
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke(this, $"DXGI Duplication not available for {_monitor.DeviceName} ({ex.Message}). Using Ultra-Fast Native GDI BitBlt engine.");
            }

            _useDxgi = false;
            DisposeDxgi();
            InitFastGdi();
        }

        private void DisposeDxgi()
        {
            _duplication?.Dispose();
            _duplication = null;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }

        #region Native Fast GDI (Win32 DIBSection + Direct DWM BitBlt)
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private void InitFastGdi()
        {
            DisposeFastGdi();
            try
            {
                _hScreenDc = GetDC(IntPtr.Zero);
                _hMemDc = CreateCompatibleDC(_hScreenDc);

                int w = _monitor.Width;
                int h = _monitor.Height;

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h; // Negative indicates top-down bitmap
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB
                bmi.bmiHeader.biSizeImage = (uint)(w * h * 4);

                _hDibBitmap = CreateDIBSection(_hMemDc, ref bmi, 0, out _pDibBits, IntPtr.Zero, 0);
                _hOldBitmap = SelectObject(_hMemDc, _hDibBitmap);

                LogMessageReceived?.Invoke(this, $"Ultra-Fast Native BitBlt engine initialized for {_monitor.DeviceName} ({w}x{h}) - 0ms allocation pipeline.");
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke(this, $"Fast GDI init error: {ex.Message}");
            }
        }

        private void DisposeFastGdi()
        {
            if (_hMemDc != IntPtr.Zero && _hOldBitmap != IntPtr.Zero)
            {
                SelectObject(_hMemDc, _hOldBitmap);
                _hOldBitmap = IntPtr.Zero;
            }
            if (_hDibBitmap != IntPtr.Zero)
            {
                DeleteObject(_hDibBitmap);
                _hDibBitmap = IntPtr.Zero;
            }
            if (_hMemDc != IntPtr.Zero)
            {
                DeleteDC(_hMemDc);
                _hMemDc = IntPtr.Zero;
            }
            if (_hScreenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, _hScreenDc);
                _hScreenDc = IntPtr.Zero;
            }
            _pDibBits = IntPtr.Zero;
        }

        private bool CaptureFastGdiFrame(ref byte[]? buffer)
        {
            try
            {
                if (_hMemDc == IntPtr.Zero || _pDibBits == IntPtr.Zero)
                {
                    InitFastGdi();
                    if (_hMemDc == IntPtr.Zero || _pDibBits == IntPtr.Zero) return false;
                }

                int w = _monitor.Width;
                int h = _monitor.Height;
                int size = w * h * 4;

                if (buffer == null || buffer.Length != size)
                {
                    buffer = new byte[size];
                }

                const int SRCCOPY = 0x00CC0020;
                bool ok = BitBlt(_hMemDc, 0, 0, w, h, _hScreenDc, _monitor.Bounds.Left, _monitor.Bounds.Top, SRCCOPY);
                if (!ok) return false;

                unsafe
                {
                    byte* srcPtr = (byte*)_pDibBits.ToPointer();
                    fixed (byte* dstPtr = buffer)
                    {
                        Buffer.MemoryCopy(srcPtr, dstPtr, size, size);
                    }
                }

                FrameCaptured?.Invoke(this, new CapturedFrameEventArgs(buffer, w, h, Stopwatch.GetTimestamp()));
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        private void CaptureLoop(CancellationToken ct)
        {
            int targetDelayMs = 1000 / _targetFps;
            var sw = Stopwatch.StartNew();

            byte[]? gdiBuffer = null;
            byte[]? dxgiBuffer = null;

            while (!ct.IsCancellationRequested)
            {
                long startLoop = sw.ElapsedMilliseconds;

                bool frameOk = false;
                if (_useDxgi && _duplication != null)
                {
                    frameOk = CaptureDxgiFrame(ref dxgiBuffer);
                    if (!frameOk)
                    {
                        frameOk = CaptureFastGdiFrame(ref gdiBuffer);
                    }
                }
                else
                {
                    frameOk = CaptureFastGdiFrame(ref gdiBuffer);
                }

                long elapsed = sw.ElapsedMilliseconds - startLoop;
                int sleepTime = (int)(targetDelayMs - elapsed);
                if (sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }

        private bool CaptureDxgiFrame(ref byte[]? buffer)
        {
            try
            {
                if (_duplication == null || _stagingTexture == null || _context == null) return false;

                var res = _duplication.AcquireNextFrame(100, out var frameInfo, out var desktopResource);
                if (res.Failure)
                {
                    if (res.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
                    {
                        // Screen is static; unchanged frame, do not reinit
                        return true;
                    }
                    if (res.Code == Vortice.DXGI.ResultCode.AccessLost.Code ||
                        res.Code == Vortice.DXGI.ResultCode.DeviceReset.Code ||
                        res.Code == Vortice.DXGI.ResultCode.DeviceRemoved.Code)
                    {
                        TryInitDxgi();
                    }
                    return false;
                }

                using (desktopResource)
                {
                    using var desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    _context.CopyResource(_stagingTexture, desktopTexture);
                }
                _duplication.ReleaseFrame();

                var mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    int w = _monitor.Width;
                    int h = _monitor.Height;
                    int expectedSize = w * h * 4;

                    if (buffer == null || buffer.Length != expectedSize)
                    {
                        buffer = new byte[expectedSize];
                    }

                    int rowPitch = (int)mapped.RowPitch;
                    unsafe
                    {
                        byte* srcPtr = (byte*)mapped.DataPointer.ToPointer();
                        fixed (byte* dstPtr = buffer)
                        {
                            for (int y = 0; y < h; y++)
                            {
                                Buffer.MemoryCopy(srcPtr + (y * rowPitch), dstPtr + (y * w * 4), w * 4, w * 4);
                            }
                        }
                    }

                    FrameCaptured?.Invoke(this, new CapturedFrameEventArgs(buffer, w, h, Stopwatch.GetTimestamp()));
                    return true;
                }
                finally
                {
                    _context.Unmap(_stagingTexture, 0);
                }
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
            DisposeDxgi();
            DisposeFastGdi();
        }
    }
}
