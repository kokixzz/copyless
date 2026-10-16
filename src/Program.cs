using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Copyless
{
    static class Program
    {
        public const string ShowEventName = "Local\\Copyless.Show";
        public const string QuitEventName = "Local\\Copyless.Quit";
        const string MutexName = "Local\\Copyless.Instance";

        [STAThread]
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            bool quiet = args.Length > 1 && args[1].ToLowerInvariant() == "--quiet";
            try
            {
                switch (cmd)
                {
                    case "--install": return Installer.Install(quiet);
                    case "--uninstall": return Installer.Uninstall(quiet);
                    case "--write-icon": Icons.WriteIco(args[1]); return 0;
                    case "--quit": Signal(QuitEventName); return 0;
                    case "--ocr": return OcrFile(args[1], args[2]);
                    case "--launch":
                        // Start menu shortcut: run through the elevated task when available.
                        if (!Startup.IsAdmin() && Startup.TaskExists() && Startup.RunTask()) return 0;
                        break;
                    case "":
                        // Not running from the install location: offer to install.
                        if (!Installer.IsInstalledCopy())
                        {
                            DialogResult r = MessageBox.Show(
                                "Install Copyless on this PC?\n\n" +
                                "  - Selected text is copied automatically, everywhere.\n" +
                                "  - Ctrl+Alt+drag (or Ctrl+Alt+O) copies text from games and images.\n" +
                                "  - Starts with Windows, with admin rights so it also works in admin apps and games.\n" +
                                "  - Uninstall any time from Settings > Apps.\n\n" +
                                "Yes = install (or update)     No = just run it this once",
                                "Copyless", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                            if (r == DialogResult.Yes) return Installer.Install(false);
                            if (r == DialogResult.Cancel) return 0;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Copyless", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            bool created;
            var mutex = new Mutex(true, MutexName, out created);
            if (!created) { Signal(ShowEventName); return 0; }

            // High (not Realtime): input stays responsive under load without starving the rest of the system.
            try
            {
                Process self = Process.GetCurrentProcess();
                self.PriorityClass = ProcessPriorityClass.High;
                self.PriorityBoostEnabled = true;
            }
            catch { }
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log.Always("ui exception: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Always("fatal: " + e.ExceptionObject);
            bool firstRun = Settings.Load();
            Application.Run(new TrayApp(firstRun));
            GC.KeepAlive(mutex);
            return 0;
        }

        static void Signal(string name)
        {
            try { EventWaitHandle.OpenExisting(name).Set(); } catch { }
        }

        public static bool IsRunning()
        {
            try { using (Mutex.OpenExisting(MutexName)) return true; } catch { return false; }
        }

        public static void StopRunning()
        {
            Signal(QuitEventName);
            for (int i = 0; i < 40 && IsRunning(); i++) Thread.Sleep(100);
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("Copyless"))
                if (p.Id != self) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
        }

        // Recognizes text in an image file and writes it to a file.
        static int OcrFile(string image, string output)
        {
            using (var bmp = new Bitmap(image))
            {
                OcrOutcome r = Ocr.Recognize(bmp).Result;
                File.WriteAllText(output, r.Language + "\n" + r.Text, new UTF8Encoding(false));
            }
            return 0;
        }
    }

    static class Startup
    {
        const string TaskName = "Copyless";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static int Schtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(15000); return p.ExitCode; }
        }

        public static bool TaskExists() { return Schtasks("/query /tn " + TaskName) == 0; }
        public static bool RunTask() { return Schtasks("/run /tn " + TaskName) == 0; }

        static bool RunKeyExists()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(TaskName) != null;
        }

        public static bool IsEnabled() { return RunKeyExists() || TaskExists(); }

        public static void Enable(string exe)
        {
            if (IsAdmin()) { CreateTask(exe); RemoveRunKey(); }
            else using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(TaskName, "\"" + exe + "\"");
        }

        public static void Disable()
        {
            RemoveRunKey();
            if (IsAdmin()) Schtasks("/delete /tn " + TaskName + " /f");
            else if (TaskExists())
                MessageBox.Show("The startup task was created with admin rights. Remove it from Apps & features (uninstall), " +
                    "or run Copyless as administrator once and untick this again.", "Copyless");
        }

        static void RemoveRunKey()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null && k.GetValue(TaskName) != null) k.DeleteValue(TaskName);
        }

        /// <summary>Registers a logon task that runs with highest privileges.</summary>
        public static void CreateTask(string exe)
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string xml =
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.3"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Copyless: selected text is copied to the clipboard automatically.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Esc(user) + @"</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>" + Esc(user) + @"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>1</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>""" + Esc(exe) + @"""</Command></Exec></Actions>
</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "Copyless.task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            int rc = Schtasks("/create /tn " + TaskName + " /xml \"" + tmp + "\" /f");
            try { File.Delete(tmp); } catch { }
            if (rc != 0) throw new Exception("Could not create the startup task (schtasks exit " + rc + ").");
        }

        static string Esc(string s) { return System.Security.SecurityElement.Escape(s); }
    }

    static class Installer
    {
        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Copyless"); } }
        static string Exe { get { return Path.Combine(Dir, "Copyless.exe"); } }
        static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Copyless.lnk"); }
        }
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Copyless";

        public static bool IsInstalledCopy()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);
        }

        static int Elevate(string args)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = true, Verb = "runas" };
            try { using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; } }
            catch { return 1; }   // elevation declined
        }

        public static int Install(bool quiet)
        {
            if (!Startup.IsAdmin()) return Elevate("--install" + (quiet ? " --quiet" : ""));
            Program.StopRunning();
            Directory.CreateDirectory(Dir);
            if (!IsInstalledCopy()) File.Copy(Application.ExecutablePath, Exe, true);

            Startup.CreateTask(Exe);
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (k != null && k.GetValue("Copyless") != null) k.DeleteValue("Copyless");

            CreateShortcut(StartMenuLink, Exe, "--launch", "Copy selected text automatically");

            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Copyless");
                k.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                k.SetValue("Publisher", "Copyless");
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + Exe + "\" --uninstall --quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024) + 1, RegistryValueKind.DWord);
            }

            if (!Startup.RunTask()) Process.Start(Exe);
            if (!quiet)
                MessageBox.Show("Copyless is installed and running.\n\nSelect any text and it's copied. The icon is by the clock " +
                    "(click it to pause). It starts automatically with Windows.", "Copyless", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        public static int Uninstall(bool quiet)
        {
            if (!Startup.IsAdmin()) return Elevate("--uninstall" + (quiet ? " --quiet" : ""));
            if (!quiet && MessageBox.Show("Remove Copyless from this PC?", "Copyless", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            Program.StopRunning();
            try { Startup.Disable(); } catch { }
            try { File.Delete(StartMenuLink); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            // Remove the install directory after this process exits.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            if (!quiet)
                MessageBox.Show("Copyless was removed.\nYour settings are still in " + Settings.Dir + " (delete that folder if you like).",
                    "Copyless", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        static void CreateShortcut(string path, string target, string args, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { desc });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
    }
}
