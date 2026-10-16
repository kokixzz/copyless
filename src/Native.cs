using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Copyless
{
    static class Native
    {
        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204,
            WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_XBUTTONDOWN = 0x020B, WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;
        public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_INSERT = 0x2D;
        public const uint LLMHF_INJECTED = 0x01;

        // dwExtraInfo marker for injected input, ignored by the hooks.
        public static readonly IntPtr Magic = new IntPtr(0x43534C31);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize; public uint flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion U; }

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200;
        public const int SW_SHOWNOACTIVATE = 4;

        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
            WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

        static readonly IntPtr IBeam = LoadCursor(IntPtr.Zero, new IntPtr(32513));

        /// <summary>True if the current system cursor is the I-beam.</summary>
        public static bool CursorIsIBeam()
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (!GetCursorInfo(ref ci)) return false;
            return (ci.flags & 1) != 0 && ci.hCursor == IBeam;
        }

        public static bool KeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        public static bool AnyModifierDown()
        {
            return KeyDown(VK_SHIFT) || KeyDown(VK_CONTROL) || KeyDown(VK_MENU) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN);
        }

        public static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool HasCaret(IntPtr hwnd)
        {
            uint pid;
            uint tid = GetWindowThreadProcessId(hwnd, out pid);
            var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return GetGUIThreadInfo(tid, ref gi) && gi.hwndCaret != IntPtr.Zero;
        }

        public static float ScaleAt(int x, int y)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT { X = x, Y = y }, 2);
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            return 1f;
        }

        static bool IsExtended(int vk)
        {
            return (vk >= 0x21 && vk <= 0x2E) || vk == 0xA3 || vk == 0xA5;
        }

        static INPUT Key(int vk, bool up)
        {
            var i = new INPUT { type = 1 };
            i.U.ki.wVk = (ushort)vk;
            i.U.ki.wScan = (ushort)MapVirtualKey((uint)vk, 0);
            i.U.ki.dwFlags = (up ? 2u : 0u) | (IsExtended(vk) ? 1u : 0u);
            i.U.ki.dwExtraInfo = Magic;
            return i;
        }

        static void Send(INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }

        public static void SendChord(int mod, int key)
        {
            Send(new[] { Key(mod, false), Key(key, false), Key(key, true), Key(mod, true) });
        }

        public static void SendTap(int key) { Send(new[] { Key(key, false), Key(key, true) }); }

        public static void LeftClickAt(int x, int y)
        {
            SetCursorPos(x, y);
            var down = new INPUT { type = 0 }; down.U.mi.dwFlags = 0x0002; down.U.mi.dwExtraInfo = Magic;
            var up = new INPUT { type = 0 }; up.U.mi.dwFlags = 0x0004; up.U.mi.dwExtraInfo = Magic;
            Send(new[] { down, up });
        }
    }
}
