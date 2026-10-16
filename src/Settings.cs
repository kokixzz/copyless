using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Copyless
{
    static class Settings
    {
        public static volatile bool Enabled = true;
        public static volatile bool KeyboardSelection = true;
        public static volatile bool SelectAll = true;
        public static volatile bool SmartPaste = true;
        public static volatile bool MiddleClickPaste = false;
        public static volatile bool UiaFallback = true;
        public static volatile bool Debug = false;
        public static string ToggleHotkey = "Ctrl+Alt+C";
        public static string OcrHotkey = "Ctrl+Alt+O";
        public static string OcrDragModifiers = "Ctrl+Alt";
        public static string OcrLanguage = "auto";
        public static int MinDragPixels = 6;

        // Graphics applications, where dragging draws instead of selecting.
        static readonly string[] DefaultExclude = {
            "mspaint", "paintdotnet", "photoshop", "illustrator", "afterfx", "gimp", "gimp-2.10", "krita",
            "inkscape", "blender", "clipstudiopaint", "aseprite", "affinity photo", "photo", "designer",
            "paintstudio.view", "mypaint", "pinta", "copyless"
        };
        // Applications with a custom I-beam cursor.
        static readonly string[] DefaultForce = { "winword", "powerpnt", "onenote", "acrord32", "acrobat", "foxitpdfreader", "foxitphantompdf" };

        public static HashSet<string> Exclude = new HashSet<string>(DefaultExclude, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> ForceKeystroke = new HashSet<string>(DefaultForce, StringComparer.OrdinalIgnoreCase);

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Copyless"); }
        }
        public static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static bool IsExcluded(string proc) { return proc != null && Exclude.Contains(proc); }
        public static bool IsForced(string proc) { return proc != null && ForceKeystroke.Contains(proc); }

        public static void SetExcluded(string proc, bool excluded)
        {
            var set = new HashSet<string>(Exclude, StringComparer.OrdinalIgnoreCase);
            if (excluded) set.Add(proc); else set.Remove(proc);
            Exclude = set;
            Save();
        }

        /// <summary>Returns true when the file did not exist yet (first run).</summary>
        public static bool Load()
        {
            if (!File.Exists(FilePath)) { Save(); return true; }
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "enabled": Enabled = Bool(v, Enabled); break;
                    case "keyboardselection": KeyboardSelection = Bool(v, KeyboardSelection); break;
                    case "copyonctrla": SelectAll = Bool(v, SelectAll); break;
                    case "smartpaste": SmartPaste = Bool(v, SmartPaste); break;
                    case "middleclickpaste": MiddleClickPaste = Bool(v, MiddleClickPaste); break;
                    case "accessibilityfallback": UiaFallback = Bool(v, UiaFallback); break;
                    case "debuglog": Debug = Bool(v, Debug); break;
                    case "togglehotkey": ToggleHotkey = v; break;
                    case "ocrhotkey": OcrHotkey = v; break;
                    case "ocrdragmodifiers": OcrDragModifiers = v; break;
                    case "ocrlanguage": OcrLanguage = v.Length == 0 ? "auto" : v; break;
                    case "mindragpixels": int n; if (int.TryParse(v, out n) && n >= 2) MinDragPixels = n; break;
                    case "exclude": Exclude = List(v); break;
                    case "forcectrlc": ForceKeystroke = List(v); break;
                }
            }
            return false;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# Copyless settings. Edit and save; changes apply when you reopen the tray menu.");
                sb.AppendLine("Enabled=" + Enabled);
                sb.AppendLine("# Shift+Arrow / Shift+Home / Ctrl+Shift+End selections are copied when Shift is released.");
                sb.AppendLine("KeyboardSelection=" + KeyboardSelection);
                sb.AppendLine("CopyOnCtrlA=" + SelectAll);
                sb.AppendLine("# Select text and press Ctrl+V right away = replace it with what you had copied before.");
                sb.AppendLine("SmartPaste=" + SmartPaste);
                sb.AppendLine("# Linux-style: middle-click over a text field pastes.");
                sb.AppendLine("MiddleClickPaste=" + MiddleClickPaste);
                sb.AppendLine("# Read selections through Windows UI Automation when Ctrl+C can't be used (terminals, odd apps).");
                sb.AppendLine("AccessibilityFallback=" + UiaFallback);
                sb.AppendLine("ToggleHotkey=" + ToggleHotkey);
                sb.AppendLine("# Frozen-screen OCR snip. Works on games, images, videos, anything on screen.");
                sb.AppendLine("OcrHotkey=" + OcrHotkey);
                sb.AppendLine("# Hold these and drag with the left mouse button to OCR a box live (games). 'None' disables.");
                sb.AppendLine("OcrDragModifiers=" + OcrDragModifiers);
                sb.AppendLine("# auto, or a language tag such as en-US or bg");
                sb.AppendLine("OcrLanguage=" + OcrLanguage);
                sb.AppendLine("MinDragPixels=" + MinDragPixels);
                sb.AppendLine("# Process names (without .exe), comma separated. Selection is never copied in these.");
                sb.AppendLine("Exclude=" + string.Join(", ", Exclude.OrderBy(s => s)));
                sb.AppendLine("# Apps with their own I-beam cursor: always send Ctrl+C after a drag.");
                sb.AppendLine("ForceCtrlC=" + string.Join(", ", ForceKeystroke.OrderBy(s => s)));
                sb.AppendLine("DebugLog=" + Debug);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static bool Bool(string v, bool def)
        {
            v = v.ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "on") return true;
            if (v == "false" || v == "0" || v == "no" || v == "off") return false;
            return def;
        }

        static HashSet<string> List(string v)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in v.Split(','))
            {
                string s = p.Trim();
                if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
                if (s.Length > 0) set.Add(s);
            }
            return set;
        }

        /// <summary>Virtual keys that must all be held, from "Ctrl+Alt" etc. Null = disabled.</summary>
        public static int[] ParseModifiers(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
            var vks = new List<int>();
            foreach (string p in spec.Split('+'))
            {
                string s = p.Trim().ToLowerInvariant();
                if (s == "ctrl" || s == "control") vks.Add(Native.VK_CONTROL);
                else if (s == "alt") vks.Add(Native.VK_MENU);
                else if (s == "shift") vks.Add(Native.VK_SHIFT);
                else if (s == "win") vks.Add(Native.VK_LWIN);
            }
            return vks.Count == 0 ? null : vks.ToArray();
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static void Write(string msg)
        {
            if (!Settings.Debug) return;
            Always(msg);
        }
        public static void Always(string msg)
        {
            try
            {
                lock (gate)
                {
                    string path = Path.Combine(Settings.Dir, "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 2000000) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff ") + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
