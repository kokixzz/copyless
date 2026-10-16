using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace Copyless
{
    enum GestureKind { Drag, MultiClick, ShiftClick, Keyboard, SelectAll, MiddlePaste }

    class Gesture
    {
        public GestureKind Kind;
        public Native.POINT Down, Up;
        public bool IBeam;
        public int Gen, ClickCount;
    }

    /// <summary>
    /// Copies the selection for each gesture. When the cursor is an I-beam, Ctrl+C is sent and the clipboard
    /// sequence number is checked; otherwise the selection is read through UI Automation.
    /// </summary>
    class CopyEngine
    {
        readonly TrayApp app;
        public InputWatcher Watcher;
        readonly BlockingCollection<Gesture> queue = new BlockingCollection<Gesture>();

        // Clipboard contents from before the last automatic copy. Accessed on the UI thread only.
        DataObject snapshot;
        bool hasSnapshot;
        volatile bool armed;
        IntPtr armedHwnd;
        int armedTick;
        uint seqAfterCopy;

        string lastText;
        IntPtr lastHwnd;

        readonly Dictionary<uint, string> procNames = new Dictionary<uint, string>();
        static readonly string SelfName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        static readonly HashSet<string> TerminalClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "mintty", "PuTTY", "VirtualConsoleClass", "KiTTY", "Alacritty", "org.wezfurlong.wezterm"
        };

        public CopyEngine(TrayApp app) { this.app = app; }

        public void Start()
        {
            var t = new Thread(Loop) { IsBackground = true, Name = "copy" };
            t.Start();
        }

        public void Enqueue(Gesture g) { queue.Add(g); }

        void Loop()
        {
            foreach (Gesture first in queue.GetConsumingEnumerable())
            {
                Gesture g = first, next;
                while (queue.TryTake(out next)) g = next;   // coalesce
                try { Process(g); }
                catch (Exception ex) { Log.Always("copy: " + ex); }
            }
        }

        bool Stale(Gesture g) { return Thread.VolatileRead(ref Watcher.Generation) != g.Gen; }

        public string ProcessName(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return "";
            lock (procNames)
            {
                string name;
                if (procNames.TryGetValue(pid, out name)) return name;
                try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { name = ""; }
                if (procNames.Count > 500) procNames.Clear();
                procNames[pid] = name;
                return name;
            }
        }

        void Process(Gesture g)
        {
            if (g.Kind == GestureKind.MiddlePaste) { MiddlePaste(g); return; }
            if (!Settings.Enabled) return;
            if (g.Kind == GestureKind.Keyboard && !Settings.KeyboardSelection) return;
            if (g.Kind == GestureKind.SelectAll && !Settings.SelectAll) return;

            IntPtr hwnd = Native.GetForegroundWindow();
            string proc = ProcessName(hwnd);
            if (proc.Length == 0 || proc.Equals(SelfName, StringComparison.OrdinalIgnoreCase)) return;
            app.NoteApp(proc);
            if (Settings.IsExcluded(proc)) { Log.Write("excluded " + proc); return; }

            // Wait for modifiers to be released so the injected Ctrl+C is not combined with them.
            int t0 = Environment.TickCount;
            while (Native.AnyModifierDown())
            {
                if (Stale(g) || Environment.TickCount - t0 > 5000) return;
                Thread.Sleep(10);
            }
            Thread.Sleep(g.Kind == GestureKind.MultiClick ? 45 : 25);   // let the target finish updating its selection
            if (Stale(g)) return;

            string cls = Native.ClassName(hwnd);
            bool terminal = TerminalClasses.Contains(cls);
            bool keyboardKind = g.Kind == GestureKind.Keyboard || g.Kind == GestureKind.SelectAll;
            bool useKeys = !terminal && (g.IBeam || Settings.IsForced(proc) || (keyboardKind && Native.HasCaret(hwnd)));
            Log.Write(string.Format("{0} in {1} [{2}] ibeam={3} keys={4}", g.Kind, proc, cls, g.IBeam, useKeys));

            if (useKeys)
            {
                uint before = Native.GetClipboardSequenceNumber();
                app.RunOnUi(PrepareSnapshot);
                if (Stale(g) || Native.AnyModifierDown()) return;
                Native.SendChord(Native.VK_CONTROL, 'C');
                if (WaitClipboardChange(before, 500))
                {
                    string text = null;
                    app.RunOnUi(() => { try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { } });
                    Copied(hwnd, text, g);
                    return;
                }
                Log.Write("ctrl+c changed nothing");
            }

            if (!Settings.UiaFallback) return;
            // Without an I-beam, only the element under the pointer is queried.
            bool usePoint = !keyboardKind;
            bool useFocus = keyboardKind || g.IBeam || terminal || useKeys;
            string sel = Uia.GetSelectedText(g.Up, usePoint, useFocus, 1500);
            if (string.IsNullOrEmpty(sel) || Stale(g)) return;
            if (!useKeys && sel == lastText && hwnd == lastHwnd) return;   // unchanged selection
            bool ok = false;
            app.RunOnUi(() =>
            {
                PrepareSnapshot();
                try { Clipboard.SetDataObject(sel, true, 10, 25); ok = true; } catch (Exception ex) { Log.Always("set clipboard: " + ex.Message); }
            });
            if (ok) Copied(hwnd, sel, g);
        }

        static bool WaitClipboardChange(uint before, int timeoutMs)
        {
            int t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < timeoutMs)
            {
                if (Native.GetClipboardSequenceNumber() != before) { Thread.Sleep(15); return true; }
                Thread.Sleep(10);
            }
            return false;
        }

        void Copied(IntPtr hwnd, string text, Gesture g)
        {
            seqAfterCopy = Native.GetClipboardSequenceNumber();
            lastText = text;
            lastHwnd = hwnd;
            armedHwnd = hwnd;
            armedTick = Environment.TickCount;
            armed = !Stale(g);
        }

        public void Disarm() { armed = false; }

        /// <summary>
        /// True when Ctrl+V follows an automatic copy in the same window with no other input in between.
        /// The previous clipboard contents are then restored so the paste replaces the selection.
        /// </summary>
        public bool ShouldRestoreBeforePaste()
        {
            return Settings.SmartPaste && armed && hasSnapshot
                && Native.GetForegroundWindow() == armedHwnd
                && Environment.TickCount - armedTick < 10 * 60 * 1000
                && Native.GetClipboardSequenceNumber() == seqAfterCopy;
        }

        public void RestoreAndPaste()
        {
            armed = false;
            app.PostToUi(() =>
            {
                RestoreSnapshot();
                if (Native.KeyDown(Native.VK_CONTROL)) Native.SendTap('V');
                else Native.SendChord(Native.VK_CONTROL, 'V');
                Log.Write("smart paste: restored previous clipboard");
            });
        }

        static readonly string[] SnapshotFormats = {
            DataFormats.UnicodeText, DataFormats.Text, DataFormats.Html, DataFormats.Rtf,
            DataFormats.CommaSeparatedValue, DataFormats.Bitmap, DataFormats.FileDrop, "Preferred DropEffect"
        };

        // UI thread only.
        void PrepareSnapshot()
        {
            // Keep the existing snapshot if the clipboard still holds the last automatic copy.
            if (hasSnapshot && Native.GetClipboardSequenceNumber() == seqAfterCopy) return;
            var dst = new DataObject();
            try
            {
                IDataObject src = Clipboard.GetDataObject();
                if (src != null)
                    foreach (string f in SnapshotFormats)
                    {
                        try
                        {
                            if (!src.GetDataPresent(f, false)) continue;
                            object d = src.GetData(f, false);
                            if (d != null) dst.SetData(f, false, d);
                        }
                        catch { }
                    }
            }
            catch { }
            snapshot = dst;
            hasSnapshot = true;
        }

        // UI thread only.
        void RestoreSnapshot()
        {
            try
            {
                if (snapshot == null || snapshot.GetFormats().Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(snapshot, true, 10, 25);
            }
            catch (Exception ex) { Log.Always("restore: " + ex.Message); }
            hasSnapshot = false;
        }

        void MiddlePaste(Gesture g)
        {
            int t0 = Environment.TickCount;
            while (Native.AnyModifierDown() && Environment.TickCount - t0 < 2000) Thread.Sleep(10);
            Native.LeftClickAt(g.Up.X, g.Up.Y);
            Thread.Sleep(40);
            Native.SendChord(Native.VK_CONTROL, 'V');
        }
    }
}
