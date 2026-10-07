using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhoneMonitor.Server.Touch
{
    public class TouchPointData
    {
        public int PointerId { get; set; }
        public byte Action { get; set; } // 0: DOWN, 1: MOVE, 2: UP, 3: CANCEL
        public float NormX { get; set; }
        public float NormY { get; set; }
        public float Pressure { get; set; }
    }

    public class TouchManager
    {
        private static readonly object _initLock = new();
        private static bool _isInitialized = false;

        public RECT TargetMonitorRect { get; set; } = new RECT(0, 0, 1920, 1080);
        public bool InvertX { get; set; } = false;
        public bool InvertY { get; set; } = false;

        public long InjectedTouchCount { get; private set; }

        public TouchManager()
        {
            EnsureInitialized();
        }

        public static bool EnsureInitialized()
        {
            if (_isInitialized) return true;

            lock (_initLock)
            {
                if (_isInitialized) return true;
                bool success = NativeTouchInjection.InitializeTouchInjection(
                    NativeTouchInjection.MAX_TOUCH_COUNT, 
                    NativeTouchInjection.TOUCH_FEEDBACK_DEFAULT);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"InitializeTouchInjection returned error: {error}");
                }
                else
                {
                    _isInitialized = true;
                    Debug.WriteLine("Windows Touch Injection initialized successfully.");
                }
                return _isInitialized;
            }
        }

        public bool ProcessTouchPoints(TouchPointData[] points)
        {
            if (points == null || points.Length == 0) return false;
            if (!_isInitialized && !EnsureInitialized()) return false;

            int count = Math.Min(points.Length, (int)NativeTouchInjection.MAX_TOUCH_COUNT);
            var contacts = new POINTER_TOUCH_INFO[count];

            for (int i = 0; i < count; i++)
            {
                var pt = points[i];
                float nx = Math.Clamp(pt.NormX, 0f, 1f);
                float ny = Math.Clamp(pt.NormY, 0f, 1f);

                if (InvertX) nx = 1f - nx;
                if (InvertY) ny = 1f - ny;

                int screenX = TargetMonitorRect.Left + (int)Math.Round(nx * (TargetMonitorRect.Width - 1));
                int screenY = TargetMonitorRect.Top + (int)Math.Round(ny * (TargetMonitorRect.Height - 1));

                POINTER_FLAGS flags = POINTER_FLAGS.NONE;
                switch (pt.Action)
                {
                    case 0: // DOWN
                        flags = POINTER_FLAGS.DOWN | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT;
                        break;
                    case 1: // MOVE / UPDATE
                        flags = POINTER_FLAGS.UPDATE | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT;
                        break;
                    case 2: // UP
                        flags = POINTER_FLAGS.UP;
                        break;
                    case 3: // CANCEL
                        flags = POINTER_FLAGS.UP | POINTER_FLAGS.CANCELLED;
                        break;
                    default:
                        flags = POINTER_FLAGS.UPDATE | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT;
                        break;
                }

                uint pointerId = (uint)Math.Clamp(pt.PointerId, 0, (int)NativeTouchInjection.MAX_TOUCH_COUNT - 1);

                contacts[i].pointerInfo.pointerType = POINTER_INPUT_TYPE.PT_TOUCH;
                contacts[i].pointerInfo.pointerId = pointerId;
                contacts[i].pointerInfo.pointerFlags = flags;
                contacts[i].pointerInfo.ptPixelLocation = new POINT(screenX, screenY);
                contacts[i].touchFlags = TOUCH_FLAGS.NONE;
                contacts[i].touchMask = TOUCH_MASK.PRESSURE | TOUCH_MASK.CONTACTAREA;

                // Contact area bounding box (simulating finger touch circle ~ 12px radius)
                int radius = 12;
                contacts[i].rcContact = new RECT(
                    screenX - radius,
                    screenY - radius,
                    screenX + radius,
                    screenY + radius);

                // Pressure scaled between 0 and 1024
                uint pressureVal = (uint)Math.Clamp((int)(pt.Pressure * 1024), 0, 1024);
                contacts[i].pressure = pressureVal > 0 ? pressureVal : 512;
            }

            bool result = NativeTouchInjection.InjectTouchInput((uint)count, contacts);
            if (result)
            {
                InjectedTouchCount += count;
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"InjectTouchInput failed with error code: {err}");
            }

            return result;
        }
    }
}
