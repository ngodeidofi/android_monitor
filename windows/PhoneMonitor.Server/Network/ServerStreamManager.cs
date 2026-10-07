using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PhoneMonitor.Server.Touch;

namespace PhoneMonitor.Server.Network
{
    public class ServerStreamManager : IDisposable
    {
        private TcpListener? _videoListener;
        private TcpListener? _touchListener;
        private TcpClient? _videoClient;
        private TcpClient? _touchClient;
        private NetworkStream? _videoStream;
        private NetworkStream? _touchStream;

        private CancellationTokenSource? _cts;
        private readonly TouchManager _touchManager;

        public const int DEFAULT_VIDEO_PORT = 59100;
        public const int DEFAULT_TOUCH_PORT = 59101;

        public bool IsRunning { get; private set; }
        public bool IsClientConnected => _videoClient?.Connected == true;

        public event EventHandler<bool>? ClientConnectionChanged;
        public event EventHandler<string>? LogMessageReceived;

        public long TotalBytesSent { get; private set; }
        public int CurrentFps { get; private set; }
        public double CurrentMbps { get; private set; }

        private int _fpsCounter = 0;
        private long _bytesInCurrentSecond = 0;
        private Timer? _statsTimer;

        // Zero-Latency single-slot frame buffer: drops stale frames and prevents TCP queue bloat
        private class QueuedFrame
        {
            public byte[] Data = Array.Empty<byte>();
            public int Length;
            public bool IsKeyFrame;
            public long Timestamp;
        }

        private QueuedFrame? _pendingFrame;
        private readonly object _pendingFrameLock = new();
        private readonly AutoResetEvent _sendSignal = new(false);

        public ServerStreamManager(TouchManager touchManager)
        {
            _touchManager = touchManager;
        }

        public void Start(int videoPort = DEFAULT_VIDEO_PORT, int touchPort = DEFAULT_TOUCH_PORT)
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();

            _videoListener = new TcpListener(IPAddress.Any, videoPort);
            _videoListener.Server.NoDelay = true;
            _videoListener.Start();

            _touchListener = new TcpListener(IPAddress.Any, touchPort);
            _touchListener.Server.NoDelay = true;
            _touchListener.Start();

            LogMessageReceived?.Invoke(this, $"Listeners started on ports {videoPort} (Video) and {touchPort} (Touch).");

            _statsTimer = new Timer(_ =>
            {
                CurrentFps = Interlocked.Exchange(ref _fpsCounter, 0);
                long bytes = Interlocked.Exchange(ref _bytesInCurrentSecond, 0);
                CurrentMbps = (bytes * 8.0) / 1_000_000.0;
            }, null, 1000, 1000);

            Task.Run(() => AcceptVideoClientsLoop(_cts.Token));
            Task.Run(() => AcceptTouchClientsLoop(_cts.Token));
            Task.Run(() => SendVideoFramesLoop(_cts.Token));
        }

        private async Task AcceptVideoClientsLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _videoListener!.AcceptTcpClientAsync(ct);
                    client.NoDelay = true;
                    client.SendBufferSize = 1024 * 1024;

                    _videoClient?.Dispose();
                    _videoClient = client;
                    _videoStream = client.GetStream();

                    LogMessageReceived?.Invoke(this, $"Video client connected from {client.Client.RemoteEndPoint}");
                    ClientConnectionChanged?.Invoke(this, true);

                    // Send initial handshake header: [Magic 'P' 'M' 'O' 'N'] [Width: int32] [Height: int32] [TargetFps: int32]
                    var header = new byte[16];
                    header[0] = (byte)'P'; header[1] = (byte)'M'; header[2] = (byte)'O'; header[3] = (byte)'N';
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), _touchManager.TargetMonitorRect.Width);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), _touchManager.TargetMonitorRect.Height);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), 60);

                    await _videoStream.WriteAsync(header, ct);
                    await _videoStream.FlushAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke(this, $"Video listener error: {ex.Message}");
                }
            }
        }

        private async Task AcceptTouchClientsLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _touchListener!.AcceptTcpClientAsync(ct);
                    client.NoDelay = true;

                    _touchClient?.Dispose();
                    _touchClient = client;
                    _touchStream = client.GetStream();

                    LogMessageReceived?.Invoke(this, $"Touch client connected from {client.Client.RemoteEndPoint}");

                    _ = Task.Run(() => ReadTouchPacketsLoop(_touchStream, ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke(this, $"Touch listener error: {ex.Message}");
                }
            }
        }

        private async Task ReadTouchPacketsLoop(NetworkStream stream, CancellationToken ct)
        {
            byte[] headerBuf = new byte[2]; // [0x54, count]
            byte[] ptBuf = new byte[18]; // per pointer

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int read = await ReadExactAsync(stream, headerBuf, 0, 2, ct);
                    if (read < 2) break;

                    if (headerBuf[0] != 0x54) // 'T'
                    {
                        continue;
                    }

                    int count = headerBuf[1];
                    if (count <= 0 || count > 10) continue;

                    var points = new TouchPointData[count];
                    for (int i = 0; i < count; i++)
                    {
                        await ReadExactAsync(stream, ptBuf, 0, 18, ct);

                        int pointerId = BinaryPrimitives.ReadInt32LittleEndian(ptBuf.AsSpan(0, 4));
                        byte action = ptBuf[4];
                        float normX = BinaryPrimitives.ReadSingleLittleEndian(ptBuf.AsSpan(5, 4));
                        float normY = BinaryPrimitives.ReadSingleLittleEndian(ptBuf.AsSpan(9, 4));
                        float pressure = BinaryPrimitives.ReadSingleLittleEndian(ptBuf.AsSpan(13, 4));

                        points[i] = new TouchPointData
                        {
                            PointerId = pointerId,
                            Action = action,
                            NormX = normX,
                            NormY = normY,
                            Pressure = pressure
                        };
                    }

                    _touchManager.ProcessTouchPoints(points);
                }
            }
            catch (Exception ex)
            {
                LogMessageReceived?.Invoke(this, $"Touch read loop ended: {ex.Message}");
            }
        }

        private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int r = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), ct);
                if (r == 0) return total;
                total += r;
            }
            return total;
        }

        /// <summary>
        /// Posts an encoded frame for immediate delivery.
        /// Non-blocking: automatically replaces any pending unsent frame to maintain strictly 0-latency.
        /// </summary>
        public void SendVideoFrame(byte[] frameData, bool isKeyFrame, long timestamp)
        {
            if (_videoStream == null || _videoClient?.Connected != true) return;

            lock (_pendingFrameLock)
            {
                _pendingFrame ??= new QueuedFrame();
                if (_pendingFrame.Data.Length < frameData.Length)
                {
                    _pendingFrame.Data = new byte[frameData.Length + 16384];
                }
                Buffer.BlockCopy(frameData, 0, _pendingFrame.Data, 0, frameData.Length);
                _pendingFrame.Length = frameData.Length;
                _pendingFrame.IsKeyFrame = isKeyFrame;
                _pendingFrame.Timestamp = timestamp;
            }
            _sendSignal.Set();
        }

        private void SendVideoFramesLoop(CancellationToken ct)
        {
            byte[] header = new byte[20];
            header[0] = (byte)'V'; header[1] = (byte)'I'; header[2] = (byte)'D'; header[3] = (byte)'0';

            byte[] localDataBuffer = new byte[1024 * 1024];

            while (!ct.IsCancellationRequested)
            {
                _sendSignal.WaitOne(50);
                if (_videoStream == null || _videoClient?.Connected != true) continue;

                int payloadLen = 0;
                bool isKeyFrame = true;
                long timestamp = 0;

                lock (_pendingFrameLock)
                {
                    if (_pendingFrame != null && _pendingFrame.Length > 0)
                    {
                        payloadLen = _pendingFrame.Length;
                        isKeyFrame = _pendingFrame.IsKeyFrame;
                        timestamp = _pendingFrame.Timestamp;

                        if (localDataBuffer.Length < payloadLen)
                        {
                            localDataBuffer = new byte[payloadLen + 16384];
                        }
                        Buffer.BlockCopy(_pendingFrame.Data, 0, localDataBuffer, 0, payloadLen);
                        _pendingFrame.Length = 0; // Mark consumed
                    }
                }

                if (payloadLen == 0) continue;

                try
                {
                    // Packet header:
                    // [Magic: 4 bytes "VID0"]
                    // [Length: 4 bytes int32]
                    // [Flags: 1 byte (1 = keyframe)]
                    // [Reserved: 7 bytes]
                    // [Timestamp: 8 bytes int64]
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), payloadLen);
                    header[8] = (byte)(isKeyFrame ? 1 : 0);
                    header[9] = 0; header[10] = 0; header[11] = 0;
                    BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12, 8), timestamp);

                    _videoStream.Write(header, 0, 20);
                    _videoStream.Write(localDataBuffer, 0, payloadLen);
                    _videoStream.Flush();

                    Interlocked.Increment(ref _fpsCounter);
                    Interlocked.Add(ref _bytesInCurrentSecond, 20 + payloadLen);
                    TotalBytesSent += 20 + payloadLen;
                }
                catch (Exception ex)
                {
                    LogMessageReceived?.Invoke(this, $"Send frame error: {ex.Message}");
                    _videoClient = null;
                    ClientConnectionChanged?.Invoke(this, false);
                }
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _cts?.Cancel();
            _sendSignal.Set();

            _statsTimer?.Dispose();
            _videoStream?.Dispose();
            _touchStream?.Dispose();
            _videoClient?.Dispose();
            _touchClient?.Dispose();
            _videoListener?.Stop();
            _touchListener?.Stop();

            LogMessageReceived?.Invoke(this, "Server stopped.");
            ClientConnectionChanged?.Invoke(this, false);
        }

        public void Dispose()
        {
            Stop();
            _sendSignal.Dispose();
        }
    }
}
