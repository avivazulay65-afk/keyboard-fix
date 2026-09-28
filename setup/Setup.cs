using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Keyboard Fix Setup")]
[assembly: AssemblyProduct("Keyboard Fix")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace KeyboardFixSetup
{
    /// <summary>
    /// Single-file installer: embeds KeyboardFix.exe, installs it per-user, registers an elevated logon task,
    /// a Start menu shortcut and an "Apps & features" uninstall entry. "--uninstall" removes everything.
    /// </summary>
    static class Setup
    {
        const string Title = "Keyboard Fix";
        const string Version = "1.0.0";
        const string TaskName = "KeyboardFix";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyboardFix";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KeyboardFix");
        static readonly string AppExe = Path.Combine(InstallDir, "KeyboardFix.exe");
        static readonly string UninstallExe = Path.Combine(InstallDir, "Uninstall.exe");
        static readonly string Shortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Keyboard Fix.lnk");

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool quiet = Array.IndexOf(args, "--quiet") >= 0;
            try
            {
                return Array.IndexOf(args, "--uninstall") >= 0 ? Uninstall(quiet) : Install(quiet);
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show("Error / שגיאה:\n\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static bool Ask(string text, bool quiet)
        {
            return quiet || MessageBox.Show(text, Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }

        static void Info(string text, bool quiet)
        {
            if (!quiet) MessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------ install

        static int Install(bool quiet)
        {
            if (!Ask("Install Keyboard Fix " + Version + "?\n" +
                     "Fixes text typed in the wrong keyboard layout (Hebrew ↔ English).\n\n" +
                     "להתקין את Keyboard Fix?\n" +
                     "מתקן טקסט שהוקלד בשפה הלא נכונה (עברית ↔ אנגלית).", quiet))
                return 2;

            StopRunningApp();
            Directory.CreateDirectory(InstallDir);

            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyboardFix.exe"))
            using (FileStream fs = File.Create(AppExe))
                res.CopyTo(fs);

            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, UninstallExe, true);

            MigrateSettings();
            DeleteRunValue();
            RegisterTask();
            CreateShortcut();
            RegisterUninstallEntry();

            string output;
            Run("schtasks.exe", "/Run /TN " + TaskName, out output);

            Info("Keyboard Fix is installed and running (tray icon).\n" +
                 "Select text and press Ctrl+CapsLock.\n\n" +
                 "Keyboard Fix הותקן ופועל (סמל במגש ליד השעון).\n" +
                 "מסמנים טקסט ולוחצים Ctrl+CapsLock.", quiet);
            return 0;
        }

        static void MigrateSettings()
        {
            try
            {
                string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardFix", "settings.ini");
                if (!File.Exists(ini)) return;
                string[] lines = File.ReadAllLines(ini);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Trim() == "Hotkey=Ctrl+D") lines[i] = "Hotkey=Ctrl+CapsLock";
                File.WriteAllLines(ini, lines, Encoding.UTF8);
            }
            catch { }
        }

        static void RegisterTask()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Keyboard Fix - Hebrew/English wrong-layout text converter</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>4</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(AppExe) + "</Command>" +
                "<Arguments>--autostart</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string tmp = Path.Combine(Path.GetTempPath(), "KeyboardFix-task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                string output;
                if (Run("schtasks.exe", "/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F", out output) != 0)
                    throw new Exception("Could not create the startup task:\n" + output.Trim());
            }
            finally { File.Delete(tmp); }
        }

        static void CreateShortcut()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic lnk = shell.CreateShortcut(Shortcut);
                lnk.TargetPath = AppExe;
                lnk.WorkingDirectory = InstallDir;
                lnk.Description = "Hebrew/English wrong-layout text fixer";
                lnk.Save();
            }
            catch { }
        }

        static void RegisterUninstallEntry()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Keyboard Fix");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Keyboard Fix");
                k.SetValue("DisplayIcon", AppExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + UninstallExe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + UninstallExe + "\" --uninstall --quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = (new FileInfo(AppExe).Length + new FileInfo(UninstallExe).Length) / 1024;
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
            }
        }

        // ------------------------------------------------------------ uninstall

        static int Uninstall(bool quiet)
        {
            if (!Ask("Remove Keyboard Fix?\n\nלהסיר את Keyboard Fix?", quiet)) return 2;

            StopRunningApp();
            string output;
            Run("schtasks.exe", "/Delete /TN " + TaskName + " /F", out output);
            DeleteRunValue();
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { File.Delete(Shortcut); } catch { }
            try { File.Delete(AppExe); } catch { }

            // This exe may be running from the install folder - remove the folder after we exit.
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"")
            { CreateNoWindow = true, UseShellExecute = false });

            Info("Keyboard Fix was removed.\n(Your settings in %APPDATA%\\KeyboardFix were kept.)\n\n" +
                 "Keyboard Fix הוסר.", quiet);
            return 0;
        }

        // ------------------------------------------------------------ helpers

        static void StopRunningApp()
        {
            string output;
            if (File.Exists(AppExe)) Run(AppExe, "--exit", out output);
            for (int i = 0; i < 20; i++)
            {
                Process[] running = Process.GetProcessesByName("KeyboardFix");
                if (running.Length == 0) return;
                if (i == 10) foreach (Process p in running) { try { p.Kill(); } catch { } }
                Thread.Sleep(150);
            }
        }

        static void DeleteRunValue()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null && k.GetValue("KeyboardFix") != null) k.DeleteValue("KeyboardFix");
            }
            catch { }
        }

        static int Run(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.HasExited ? p.ExitCode : -1;
            }
        }
    }
}
