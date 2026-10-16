using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Copyless
{
    /// <summary>
    /// Low-level mouse and keyboard hooks on a dedicated thread. Detected gestures are queued to the CopyEngine.
    /// </summary>
    class InputWatcher
    {
        readonly CopyEngine engine;
        readonly TrayApp app;
        Native.HookProc mouseProc, keyProc;
        IntPtr hMouse, hKey;
        uint threadId;

        // Incremented on every click and non-modifier key press; pending copies are dropped when it changes.
        public int Generation;

        // Mouse state
        Native.POINT downPt, lastDownPt;
        uint lastDownTime;
        int clickCount;
        bool ibeamAtDown, shiftAtDown, middleSwallowed;

        // Live OCR drag
        public volatile bool OcrDragging;
        public Native.POINT OcrStart, OcrCurrent;

        // Keyboard state
        bool kbSelecting, pendingSelectAll;

        public InputWatcher(CopyEngine engine, TrayApp app) { this.engine = engine; this.app = app; }

        public void Start()
        {
            var ready = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                threadId = Native.GetCurrentThreadId();
                mouseProc = MouseHook;
                keyProc = KeyHook;
                IntPtr mod = Native.GetModuleHandle(null);
                hMouse = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, mod, 0);
                hKey = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, mod, 0);
                if (hMouse == IntPtr.Zero || hKey == IntPtr.Zero) Log.Always("Hook install failed: " + Marshal.GetLastWin32Error());
                ready.Set();
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                Native.UnhookWindowsHookEx(hMouse);
                Native.UnhookWindowsHookEx(hKey);
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.Highest;
            t.Name = "hooks";
            t.Start();
            ready.WaitOne(3000);
        }

        public void Stop() { if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero); }

        static bool OcrModsHeld()
        {
            int[] mods = Settings.ParseModifiers(Settings.OcrDragModifiers);
            if (mods == null) return false;
            foreach (int vk in mods)
            {
                bool held = vk == Native.VK_LWIN ? (Native.KeyDown(Native.VK_LWIN) || Native.KeyDown(Native.VK_RWIN)) : Native.KeyDown(vk);
                if (!held) return false;
            }
            return true;
        }

        IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    if (m.dwExtraInfo != Native.Magic && Handle(wParam.ToInt32(), m)) return new IntPtr(1);
                }
                catch (Exception ex) { Log.Always("mouse hook: " + ex.Message); }
            }
            return Native.CallNextHookEx(hMouse, nCode, wParam, lParam);
        }

        /// <summary>Returns true to swallow the event.</summary>
        bool Handle(int msg, Native.MSLLHOOKSTRUCT m)
        {
            switch (msg)
            {
                case Native.WM_MOUSEMOVE:
                    if (OcrDragging) OcrCurrent = m.pt;
                    return false;

                case Native.WM_LBUTTONDOWN:
                {
                    Interlocked.Increment(ref Generation);
                    engine.Disarm();
                    if (OcrModsHeld())
                    {
                        OcrStart = OcrCurrent = m.pt;
                        OcrDragging = true;
                        app.BeginOcrDrag();
                        return true;
                    }
                    uint dct = Native.GetDoubleClickTime();
                    int cx = Math.Max(4, Native.GetSystemMetrics(36)), cy = Math.Max(4, Native.GetSystemMetrics(37));
                    if (unchecked(m.time - lastDownTime) <= dct && Math.Abs(m.pt.X - lastDownPt.X) <= cx && Math.Abs(m.pt.Y - lastDownPt.Y) <= cy)
                        clickCount++;
                    else
                        clickCount = 1;
                    lastDownTime = m.time;
                    lastDownPt = downPt = m.pt;
                    ibeamAtDown = Native.CursorIsIBeam();
                    shiftAtDown = Native.KeyDown(Native.VK_SHIFT);
                    return false;
                }

                case Native.WM_LBUTTONUP:
                {
                    if (OcrDragging)
                    {
                        OcrDragging = false;
                        OcrCurrent = m.pt;
                        app.EndOcrDrag(OcrStart, m.pt);
                        return true;
                    }
                    int dx = m.pt.X - downPt.X, dy = m.pt.Y - downPt.Y;
                    int min = Settings.MinDragPixels;
                    GestureKind kind;
                    if (dx * dx + dy * dy >= min * min) kind = GestureKind.Drag;
                    else if (clickCount >= 2) kind = GestureKind.MultiClick;
                    else if (shiftAtDown) kind = GestureKind.ShiftClick;
                    else return false;
                    engine.Enqueue(new Gesture
                    {
                        Kind = kind, Down = downPt, Up = m.pt, ClickCount = clickCount,
                        IBeam = ibeamAtDown || Native.CursorIsIBeam(),
                        Gen = Thread.VolatileRead(ref Generation)
                    });
                    return false;
                }

                case Native.WM_RBUTTONDOWN:
                case Native.WM_XBUTTONDOWN:
                    Interlocked.Increment(ref Generation);
                    engine.Disarm();
                    return false;

                case Native.WM_MBUTTONDOWN:
                    Interlocked.Increment(ref Generation);
                    engine.Disarm();
                    if (Settings.MiddleClickPaste && Settings.Enabled && Native.CursorIsIBeam())
                    {
                        middleSwallowed = true;
                        return true;
                    }
                    return false;

                case Native.WM_MBUTTONUP:
                    if (middleSwallowed)
                    {
                        middleSwallowed = false;
                        engine.Enqueue(new Gesture { Kind = GestureKind.MiddlePaste, Up = m.pt, Gen = Thread.VolatileRead(ref Generation) });
                        return true;
                    }
                    return false;
            }
            return false;
        }

        static bool IsModifier(uint vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
        }

        static bool IsNav(uint vk)
        {
            return (vk >= 0x21 && vk <= 0x28); // PgUp PgDn End Home Left Up Right Down
        }

        IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    if (k.dwExtraInfo != Native.Magic && HandleKey(wParam.ToInt32(), k.vkCode)) return new IntPtr(1);
                }
                catch (Exception ex) { Log.Always("key hook: " + ex.Message); }
            }
            return Native.CallNextHookEx(hKey, nCode, wParam, lParam);
        }

        bool HandleKey(int msg, uint vk)
        {
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            if (down)
            {
                if (IsModifier(vk)) return false;
                bool ctrl = Native.KeyDown(Native.VK_CONTROL), alt = Native.KeyDown(Native.VK_MENU);
                bool shift = Native.KeyDown(Native.VK_SHIFT), win = Native.KeyDown(Native.VK_LWIN) || Native.KeyDown(Native.VK_RWIN);

                if (vk == 'V' && ctrl && !alt && !win)
                {
                    pendingSelectAll = false;   // Ctrl+A followed by Ctrl+V
                    kbSelecting = false;
                    Interlocked.Increment(ref Generation);
                    if (engine.ShouldRestoreBeforePaste())
                    {
                        engine.RestoreAndPaste();
                        return true;
                    }
                    engine.Disarm();
                    return false;
                }

                Interlocked.Increment(ref Generation);
                engine.Disarm();
                kbSelecting = IsNav(vk) && shift && !alt && !win;
                pendingSelectAll = vk == 'A' && ctrl && !alt && !shift && !win;
                return false;
            }

            // key up
            if ((vk == 0xA0 || vk == 0xA1 || vk == 0x10) && kbSelecting)
            {
                kbSelecting = false;
                engine.Enqueue(new Gesture { Kind = GestureKind.Keyboard, Gen = Thread.VolatileRead(ref Generation) });
            }
            else if ((vk == 0xA2 || vk == 0xA3 || vk == 0x11) && pendingSelectAll)
            {
                pendingSelectAll = false;
                engine.Enqueue(new Gesture { Kind = GestureKind.SelectAll, Gen = Thread.VolatileRead(ref Generation) });
            }
            return false;
        }
    }
}
