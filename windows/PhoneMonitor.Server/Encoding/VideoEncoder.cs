using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PhoneMonitor.Server.Encoding
{
    /// <summary>
    /// High-performance low-latency video encoder for screen streaming.
    /// Emits compressed frames with sub-frame encoding latency and zero heap thrashing.
    /// </summary>
    public class VideoEncoder : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private readonly int _quality;
        private readonly ImageCodecInfo _jpegEncoder;
        private readonly EncoderParameters _encoderParams;
        private readonly MemoryStream _memoryStream = new(512 * 1024);
        private readonly object _encodeLock = new();
        private readonly Bitmap _bitmap;
        private bool _isDisposed = false;

        public event EventHandler<EncodedVideoChunkEventArgs>? FrameEncoded;

        public VideoEncoder(int width, int height, int fps = 60, int quality = 75)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _quality = Math.Clamp(quality, 30, 95);

            _jpegEncoder = GetEncoder(ImageFormat.Jpeg) 
                ?? throw new InvalidOperationException("JPEG encoder not found.");

            _encoderParams = new EncoderParameters(1);
            _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);

            // Pre-allocate persistent bitmap to avoid per-frame GC allocations
            _bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);
        }

        public void EncodeBgraFrame(byte[] bgraData, long timestamp)
        {
            if (_isDisposed || bgraData == null || bgraData.Length == 0) return;

            // Non-blocking try-lock: if previous frame is still encoding, drop this frame!
            // This prevents encoder queue buildup and guarantees freshest possible frame.
            if (!Monitor.TryEnter(_encodeLock))
            {
                return;
            }

            try
            {
                _memoryStream.SetLength(0);

                var bmpData = _bitmap.LockBits(
                    new Rectangle(0, 0, _width, _height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppRgb);

                try
                {
                    Marshal.Copy(bgraData, 0, bmpData.Scan0, bgraData.Length);
                }
                finally
                {
                    _bitmap.UnlockBits(bmpData);
                }

                _bitmap.Save(_memoryStream, _jpegEncoder, _encoderParams);

                int payloadLen = (int)_memoryStream.Length;
                byte[] packet = new byte[payloadLen];
                Buffer.BlockCopy(_memoryStream.GetBuffer(), 0, packet, 0, payloadLen);

                FrameEncoded?.Invoke(this, new EncodedVideoChunkEventArgs(packet, true, timestamp));
            }
            catch
            {
                // Frame drop on transient error is safe for interactive streaming
            }
            finally
            {
                Monitor.Exit(_encodeLock);
            }
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                lock (_encodeLock)
                {
                    _encoderParams.Dispose();
                    _bitmap.Dispose();
                    _memoryStream.Dispose();
                }
            }
        }
    }
}
