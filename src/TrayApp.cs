using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Copyless
{
    class TrayApp : ApplicationContext
    {
        readonly Control ui;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly HotkeyWindow hotkeys;
        readonly Toast toast;
        readonly OcrBox ocrBox;
        readonly System.Windows.Forms.Timer ocrTimer;
        Icon iconOn, iconOff;
        readonly List<string> recentApps = new List<string>();
        public readonly CopyEngine Engine;
        public readonly InputWatcher Watcher;
        bool snipping;
        System.Threading.Timer updateTimer;
        volatile bool updating;
        string notifiedTag;

        public TrayApp(bool firstRun)
        {
            ui = new Control();
            ui.CreateControl();
            IntPtr force = ui.Handle;

            LoadIcons();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => PostToUi(() => { LoadIcons(); UpdateTray(); });
            toast = new Toast();
            ocrBox = new OcrBox();
            ocrTimer = new System.Windows.Forms.Timer { Interval = 15 };
            ocrTimer.Tick += (s, e) => UpdateOcrBox();

            menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };
            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            UpdateTray();

            hotkeys = new HotkeyWindow(this);
            RegisterHotkeys();

            Engine = new CopyEngine(this);
            Watcher = new InputWatcher(Engine, this);
            Engine.Watcher = Watcher;
            Watcher.Start();
            Engine.Start();

            ListenForSignals();

            if (firstRun)
                tray.ShowBalloonTip(6000, "Copyless is on",
                    "Select text anywhere and it's copied. " + Settings.OcrDragModifiers + "+drag (or " + Settings.OcrHotkey +
                    ") grabs text from games and images. Click this icon to pause.", ToolTipIcon.Info);
            else if (Settings.LastVersion.Length > 0 && Settings.LastVersion != Updater.CurrentText)
                tray.ShowBalloonTip(4000, "Copyless updated", "Now running version " + Updater.CurrentText + ".", ToolTipIcon.Info);
            if (Settings.LastVersion != Updater.CurrentText)
            {
                Settings.LastVersion = Updater.CurrentText;
                Settings.Save();
            }

            updateTimer = new System.Threading.Timer(s => AutoUpdate(), null, TimeSpan.FromSeconds(45), TimeSpan.FromHours(6));
        }

        // Updates

        void AutoUpdate()
        {
            Updater.CleanUp();
            if (!Settings.AutoUpdate || updating || !Installer.IsInstalledCopy()) return;
            try
            {
                ReleaseInfo info = Updater.GetLatest();
                if (!Updater.IsNewer(info)) return;
                if (!Startup.IsAdmin())
                {
                    // Installing would need a UAC prompt; tell the user once instead of interrupting them.
                    if (notifiedTag != info.Tag)
                    {
                        notifiedTag = info.Tag;
                        PostToUi(() => tray.ShowBalloonTip(5000, "Copyless " + info.Version.ToString(3) + " is available",
                            "Right-click the tray icon and choose Check for updates to install it.", ToolTipIcon.Info));
                    }
                    return;
                }
                updating = true;
                Log.Write("updating to " + info.Tag);
                Updater.Install(Updater.Download(info));
            }
            catch (Exception ex) { Log.Always("update: " + ex.Message); }
            finally { updating = false; }
        }

        void CheckForUpdates()
        {
            if (updating) return;
            var t = new Thread(() =>
            {
                ReleaseInfo info;
                try { info = Updater.GetLatest(); }
                catch (Exception ex)
                {
                    PostToUi(() => toast.Pop("Couldn't check for updates", ex.Message, true));
                    return;
                }
                PostToUi(() =>
                {
                    if (!Updater.IsNewer(info))
                    {
                        toast.Pop("Copyless is up to date", "Version " + Updater.CurrentText, false);
                        return;
                    }
                    if (MessageBox.Show("Copyless " + info.Version.ToString(3) + " is available. You have " + Updater.CurrentText + ".\n\nInstall it now?",
                            "Copyless", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        InstallUpdate(info);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void InstallUpdate(ReleaseInfo info)
        {
            updating = true;
            var t = new Thread(() =>
            {
                try { Updater.Install(Updater.Download(info)); }
                catch (Exception ex) { PostToUi(() => toast.Pop("Update failed", ex.Message, true)); }
                finally { updating = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        public void RunOnUi(Action a)
        {
            try { if (ui.InvokeRequired) ui.Invoke(a); else a(); }
            catch (Exception ex) { Log.Always("ui: " + ex.Message); }
        }

        public void PostToUi(Action a)
        {
            try { ui.BeginInvoke(a); } catch { }
        }

        void ListenForSignals()
        {
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            var quit = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
            ThreadPool.RegisterWaitForSingleObject(show, (o, t) => PostToUi(() =>
                tray.ShowBalloonTip(3000, "Copyless is already running",
                    "It lives in the tray (^ next to the clock). " + (Settings.Enabled ? "It's ON." : "It's paused - click the icon."),
                    ToolTipIcon.Info)), null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(quit, (o, t) => PostToUi(Quit), null, -1, true);
        }

        // Rebuilt when the Windows theme changes so the glyph stays visible on the taskbar.
        void LoadIcons()
        {
            Icon on = iconOn, off = iconOff;
            iconOn = Icons.Tray(true);
            iconOff = Icons.Tray(false);
            if (tray != null) tray.Icon = Settings.Enabled ? iconOn : iconOff;
            if (on != null) on.Dispose();
            if (off != null) off.Dispose();
        }

        void UpdateTray()
        {
            tray.Icon = Settings.Enabled ? iconOn : iconOff;
            tray.Text = Settings.Enabled ? "Copyless: ON (" + Settings.ToggleHotkey + ")" : "Copyless: paused";
        }

        public void Toggle()
        {
            Settings.Enabled = !Settings.Enabled;
            Settings.Save();
            UpdateTray();
            toast.Pop(Settings.Enabled ? "Copyless ON" : "Copyless paused", Settings.ToggleHotkey + " to switch", !Settings.Enabled);
        }

        public void NoteApp(string proc)
        {
            lock (recentApps)
            {
                recentApps.Remove(proc);
                recentApps.Insert(0, proc);
                if (recentApps.Count > 10) recentApps.RemoveAt(recentApps.Count - 1);
            }
        }

        void BuildMenu()
        {
            try { Settings.Load(); } catch { }
            RegisterHotkeys();
            UpdateTray();
            menu.Items.Clear();

            var title = new ToolStripMenuItem("Copyless " + Updater.CurrentText) { Enabled = false };
            title.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(title);
            Check("Copy selected text automatically", Settings.ToggleHotkey, Settings.Enabled, v => { Settings.Enabled = !v; Toggle(); });
            menu.Items.Add(new ToolStripMenuItem("Grab text from screen (OCR)...", null, (s, e) => StartSnip()) { ShortcutKeyDisplayString = Settings.OcrHotkey });
            menu.Items.Add(new ToolStripSeparator());

            Check("Copy keyboard selections (Shift+arrows)", null, Settings.KeyboardSelection, v => Settings.KeyboardSelection = v);
            Check("Copy when pressing Ctrl+A", null, Settings.SelectAll, v => Settings.SelectAll = v);
            Check("Smart paste over a selection", null, Settings.SmartPaste, v => Settings.SmartPaste = v)
                .ToolTipText = "Select text and press Ctrl+V right away: it's replaced with what you copied before.";
            Check("Middle-click pastes (Linux style)", null, Settings.MiddleClickPaste, v => Settings.MiddleClickPaste = v);
            Check("Live OCR with " + DragLabel() + "+drag", null, Settings.ParseModifiers(Settings.OcrDragModifiers) != null,
                v => Settings.OcrDragModifiers = v ? "Ctrl+Alt" : "None");

            var lang = new ToolStripMenuItem("OCR language");
            AddLang(lang, "auto", "Auto (" + string.Join(" / ", Ocr.Languages()) + ")");
            foreach (string tag in Ocr.Languages()) AddLang(lang, tag, tag);
            lang.DropDownItems.Add(new ToolStripSeparator());
            lang.DropDownItems.Add(new ToolStripMenuItem("Add more languages (Windows settings)...", null,
                (s, e) => Shell("ms-settings:regionlanguage")));
            menu.Items.Add(lang);

            var excl = new ToolStripMenuItem("Don't copy in...");
            List<string> recent;
            lock (recentApps) recent = recentApps.ToList();
            foreach (string p in recent)
            {
                string proc = p;
                var it = new ToolStripMenuItem(proc + ".exe") { Checked = Settings.IsExcluded(proc), CheckOnClick = true };
                it.Click += (s, e) => Settings.SetExcluded(proc, it.Checked);
                excl.DropDownItems.Add(it);
            }
            if (recent.Count == 0) excl.DropDownItems.Add(new ToolStripMenuItem("(apps you select text in show up here)") { Enabled = false });
            excl.DropDownItems.Add(new ToolStripSeparator());
            excl.DropDownItems.Add(new ToolStripMenuItem("Edit full list / all settings...", null, (s, e) => Shell(Settings.FilePath)));
            menu.Items.Add(excl);

            menu.Items.Add(new ToolStripSeparator());
            bool startup = Startup.IsEnabled();
            var st = Check("Start with Windows", null, startup, v => { if (v) Startup.Enable(Application.ExecutablePath); else Startup.Disable(); });
            st.ToolTipText = Startup.IsAdmin() ? "Runs with admin rights so it also works in admin windows and games." : null;
            Check("Update automatically", null, Settings.AutoUpdate, v => Settings.AutoUpdate = v)
                .ToolTipText = "Checks for a new release every 6 hours and installs it.";
            menu.Items.Add(new ToolStripMenuItem(updating ? "Updating..." : "Check for updates...", null, (s, e) => CheckForUpdates()) { Enabled = !updating });
            menu.Items.Add(new ToolStripMenuItem("How to use...", null, (s, e) => ShowHelp()));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => Quit()));
        }

        string DragLabel()
        {
            string m = Settings.OcrDragModifiers;
            return Settings.ParseModifiers(m) == null ? "Ctrl+Alt" : m;
        }

        ToolStripMenuItem Check(string text, string keys, bool value, Action<bool> set)
        {
            var it = new ToolStripMenuItem(text) { Checked = value, CheckOnClick = true };
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            it.Click += (s, e) => { set(it.Checked); Settings.Save(); UpdateTray(); };
            menu.Items.Add(it);
            return it;
        }

        void AddLang(ToolStripMenuItem parent, string tag, string label)
        {
            var it = new ToolStripMenuItem(label) { Checked = Settings.OcrLanguage.Equals(tag, StringComparison.OrdinalIgnoreCase) };
            it.Click += (s, e) => { Settings.OcrLanguage = tag; Settings.Save(); };
            parent.DropDownItems.Add(it);
        }

        static void Shell(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        void ShowHelp()
        {
            MessageBox.Show(
                "SELECT = COPY\n" +
                "  - Drag over text, double-click a word, triple-click a line, Shift+click - it's copied.\n" +
                "  - Keyboard: Shift+arrows / Shift+Home/End / Ctrl+A - copied when you let go of Shift / Ctrl.\n" +
                "  - Paste anywhere with Ctrl+V. Ctrl+C still works normally.\n\n" +
                "TEXT IN GAMES, IMAGES AND VIDEOS\n" +
                "  - Hold " + DragLabel() + " and drag a box over it: the text is read with Windows OCR and copied.\n" +
                "    The game doesn't receive that click.\n" +
                "  - Or press " + Settings.OcrHotkey + " to freeze the screen and draw a box.\n" +
                "  - Fullscreen-exclusive games can't be captured: use Borderless / Windowed mode.\n\n" +
                "SMART PASTE\n" +
                "  - Select a word and press Ctrl+V right away: it's replaced with what you had copied before,\n" +
                "    like on Linux. Click or type first and Ctrl+V pastes the selection as usual.\n\n" +
                "ON / OFF\n" +
                "  - " + Settings.ToggleHotkey + " or left-click the tray icon. Grey icon = paused.\n" +
                "  - Right-click the tray icon for options and per-app exclusions.\n\n" +
                "Settings file: " + Settings.FilePath,
                "Copyless", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        string registered = "";
        void RegisterHotkeys()
        {
            string key = Settings.ToggleHotkey + "|" + Settings.OcrHotkey;
            if (key == registered) return;
            registered = key;
            Native.UnregisterHotKey(hotkeys.Handle, 1);
            Native.UnregisterHotKey(hotkeys.Handle, 2);
            var failed = new List<string>();
            if (!RegisterOne(1, Settings.ToggleHotkey)) failed.Add(Settings.ToggleHotkey);
            if (!RegisterOne(2, Settings.OcrHotkey)) failed.Add(Settings.OcrHotkey);
            if (failed.Count > 0)
                tray.ShowBalloonTip(5000, "Copyless", "Another app already uses " + string.Join(" and ", failed) +
                    ". Change it in the settings file.", ToolTipIcon.Warning);
        }

        bool RegisterOne(int id, string spec)
        {
            if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            uint mods = 0x4000; // MOD_NOREPEAT
            Keys k = Keys.None;
            foreach (string part in spec.Split('+'))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 2;
                else if (p == "alt") mods |= 1;
                else if (p == "shift") mods |= 4;
                else if (p == "win") mods |= 8;
                else
                {
                    Keys parsed;
                    if (Regex.IsMatch(p, "^[0-9]$")) p = "D" + p;
                    if (Enum.TryParse(p, true, out parsed)) k = parsed;
                }
            }
            if (k == Keys.None) return false;
            return Native.RegisterHotKey(hotkeys.Handle, id, mods, (uint)k);
        }

        public void OnHotkey(int id)
        {
            if (id == 1) Toggle();
            else if (id == 2) StartSnip();
        }

        class HotkeyWindow : NativeWindow
        {
            readonly TrayApp app;
            public HotkeyWindow(TrayApp app) { this.app = app; CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_HOTKEY) app.OnHotkey(m.WParam.ToInt32());
                base.WndProc(ref m);
            }
        }

        public void BeginOcrDrag() { PostToUi(() => ocrTimer.Start()); }

        public void EndOcrDrag(Native.POINT a, Native.POINT b)
        {
            PostToUi(() =>
            {
                ocrTimer.Stop();
                ocrBox.Hide();
                Rectangle r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
                if (r.Width < 6 || r.Height < 6) return;
                // wait for the overlay to disappear before capturing
                var t = new System.Windows.Forms.Timer { Interval = 60 };
                t.Tick += (s, e) =>
                {
                    t.Stop(); t.Dispose();
                    Bitmap shot = null;
                    try { shot = Ocr.CaptureScreen(r); } catch (Exception ex) { Log.Always("capture: " + ex.Message); }
                    if (shot != null) RunOcr(shot);
                };
                t.Start();
            });
        }

        void UpdateOcrBox()
        {
            if (!Watcher.OcrDragging) { ocrTimer.Stop(); ocrBox.Hide(); return; }
            Native.POINT a = Watcher.OcrStart, b = Watcher.OcrCurrent;
            Rectangle r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
            if (r.Width < 3 || r.Height < 3) return;
            ocrBox.ShowAt(r);
            ocrBox.Invalidate();
        }

        public void StartSnip()
        {
            if (snipping) return;
            snipping = true;
            try
            {
                Rectangle virt = SystemInformation.VirtualScreen;
                Bitmap shot = Ocr.CaptureScreen(virt);
                var f = new SnipForm(shot, virt, (crop, rect) => RunOcr(crop));
                f.FormClosed += (s, e) => snipping = false;
                f.Show();
                f.Activate();
            }
            catch (Exception ex) { snipping = false; Log.Always("snip: " + ex.Message); }
        }

        async void RunOcr(Bitmap img)
        {
            try
            {
                if (Ocr.LooksBlank(img))
                {
                    toast.Pop("Couldn't see anything there", "Fullscreen games block capture - switch the game to Borderless/Windowed.", true);
                    return;
                }
                if (Ocr.Languages().Length == 0)
                {
                    toast.Pop("No OCR language installed", "Add a language with OCR in Windows Settings > Language.", true);
                    return;
                }
                OcrOutcome res = await Ocr.Recognize(img);
                if (string.IsNullOrWhiteSpace(res.Text))
                {
                    toast.Pop("No text found there", "Try a bigger box around the text.", true);
                    return;
                }
                Clipboard.SetDataObject(res.Text, true, 10, 25);
            }
            catch (Exception ex)
            {
                Log.Always("ocr: " + ex);
                toast.Pop("OCR failed", ex.Message, true);
            }
            finally { img.Dispose(); }
        }

        public void Quit()
        {
            try { Watcher.Stop(); } catch { }
            if (updateTimer != null) updateTimer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }
}
