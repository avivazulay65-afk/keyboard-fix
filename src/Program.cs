using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyboardFix
{
    static class Program
    {
        public const string ExitEventName = "KeyboardFix_Exit_Event";

        [STAThread]
        static void Main(string[] args)
        {
            if (Array.IndexOf(args, "--exit") >= 0)
            {
                try { using (var ev = EventWaitHandle.OpenExisting(ExitEventName)) ev.Set(); } catch { }
                return;
            }

            // Started manually without admin rights, but installed as an elevated task: start the task instead.
            if (!Autostart.IsElevated && Array.IndexOf(args, "--no-elevate") < 0 && Autostart.TaskExists && Autostart.RunTask())
                return;

            try { Native.SetProcessDPIAware(); } catch { }

            bool created;
            using (var mutex = new Mutex(true, "KeyboardFix_SingleInstance_Mutex", out created))
            {
                if (!created) return; // already running

                bool silent = Array.IndexOf(args, "--autostart") >= 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(silent));
            }
        }
    }

    // ---------------------------------------------------------------- Settings

    class Settings
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardFix");
        public static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public string Hotkey = "Ctrl+CapsLock";
        public bool SwitchLayout = true;
        public bool FloatingButton = true;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(Dir);
                    File.WriteAllText(FilePath,
                        "; Keyboard Fix settings - after editing, choose \"Reload settings\" from the tray menu\r\n" +
                        "; Hotkey examples: Ctrl+CapsLock, Ctrl+Shift+D, Alt+Q, Ctrl+Alt+Space, Pause, F9\r\n" +
                        "Hotkey=Ctrl+CapsLock\r\n" +
                        "; Switch the keyboard layout to the target language after converting (true/false)\r\n" +
                        "SwitchLayout=true\r\n" +
                        "; Show a small convert button next to the mouse after selecting text (true/false)\r\n" +
                        "FloatingButton=true\r\n", Encoding.UTF8);
                    return s;
                }
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "hotkey" && val.Length > 0) s.Hotkey = val;
                    else if (key == "switchlayout") s.SwitchLayout = ParseBool(val);
                    else if (key == "floatingbutton") s.FloatingButton = ParseBool(val);
                }
            }
            catch { }
            return s;
        }

        static bool ParseBool(string val)
        {
            return !val.Equals("false", StringComparison.OrdinalIgnoreCase) && val != "0";
        }

        /// <summary>Sets (or adds) a single key=value line in the settings file.</summary>
        public static void SetValue(string key, string value)
        {
            try
            {
                Load(); // creates the file if missing
                var lines = new List<string>(File.ReadAllLines(FilePath));
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string l = lines[i].TrimStart();
                    int eq = l.IndexOf('=');
                    if (eq > 0 && !l.StartsWith(";") && l.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = key + "=" + value;
                        found = true;
                    }
                }
                if (!found) lines.Add(key + "=" + value);
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Autostart

    static class Autostart
    {
        // Preferred: an elevated logon task (created by install.ps1), so the app can also work in admin windows.
        // Fallback: the per-user Run key (no admin rights).
        const string TaskName = "KeyboardFix";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "KeyboardFix";

        static string Command { get { return "\"" + Application.ExecutablePath + "\" --autostart"; } }

        public static bool IsElevated
        {
            get
            {
                try
                {
                    var p = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
                    return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        static string TaskXml()
        {
            string output;
            return Schtasks("/Query /TN " + TaskName + " /XML", out output) == 0 ? output : null;
        }

        public static bool TaskExists { get { return TaskXml() != null; } }

        public static bool RunTask()
        {
            string output;
            return Schtasks("/Run /TN " + TaskName, out output) == 0;
        }

        public static bool IsEnabled
        {
            get
            {
                string xml = TaskXml();
                if (xml != null)
                    return xml.Replace(" ", "").Replace("\0", "").IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) < 0;
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    var v = k == null ? null : k.GetValue(ValueName) as string;
                    return v != null && v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        public static void Set(bool enable)
        {
            if (TaskExists)
            {
                string output;
                if (Schtasks("/Change /TN " + TaskName + (enable ? " /ENABLE" : " /DISABLE"), out output) != 0)
                    throw new Exception(output.Trim());
                return;
            }
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enable) k.SetValue(ValueName, Command);
                else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
            }
        }

        static int Schtasks(string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }
    }
    // ---------------------------------------------------------------- Tray app

    class TrayContext : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeyWindow;
        readonly ToolStripMenuItem hotkeyItem, pauseItem, autostartItem, floatItem;
        readonly FloatingButton floater;
        Settings settings;

        public TrayContext(bool silent)
        {
            settings = Settings.Load();
            hotkeyWindow = new HotkeyWindow(OnHotkey);
            floater = new FloatingButton(delegate { TextSwapper.Run(Keys.None, settings.SwitchLayout); });
            TextSwapper.Typing = new TypingBuffer();

            var menu = new ContextMenuStrip();
            menu.RightToLeft = RightToLeft.Yes;
            hotkeyItem = new ToolStripMenuItem("") { Enabled = false };
            pauseItem = new ToolStripMenuItem("השהה", null, delegate { TogglePause(); });
            autostartItem = new ToolStripMenuItem("הפעל עם הפעלת המחשב", null, delegate { ToggleAutostart(); });
            floatItem = new ToolStripMenuItem("כפתור צף אחרי סימון טקסט בעכבר", null, delegate { ToggleFloating(); });
            menu.Items.Add(hotkeyItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(floatItem);
            menu.Items.Add(autostartItem);
            menu.Items.Add("ערוך הגדרות (קיצור מקשים)", null, delegate { OpenSettings(); });
            menu.Items.Add("טען הגדרות מחדש", null, delegate { ReloadSettings(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("יציאה", null, delegate { ExitThread(); });
            menu.Opening += delegate { autostartItem.Checked = Autostart.IsEnabled; };

            tray = new NotifyIcon
            {
                Icon = MakeIcon(),
                Text = "Keyboard Fix",
                ContextMenuStrip = menu,
                Visible = true
            };

            ListenForExitRequest();

            bool ok = RegisterHotkey();
            if (!ok)
                Balloon("לא ניתן לרשום את קיצור המקשים " + settings.Hotkey +
                        " (אולי תוכנה אחרת תופסת אותו). ערוך את ההגדרות מתפריט הסמל.", ToolTipIcon.Warning);
            else if (!silent)
                Balloon("Keyboard Fix פועל ברקע.\nסמן טקסט ולחץ " + settings.Hotkey + " כדי להפוך עברית↔אנגלית.", ToolTipIcon.Info);
        }

        EventWaitHandle exitEvent;

        // Lets "KeyboardFix.exe --exit" (even from a non-admin process) close this instance, e.g. for upgrades.
        void ListenForExitRequest()
        {
            try
            {
                var security = new System.Security.AccessControl.EventWaitHandleSecurity();
                security.AddAccessRule(new System.Security.AccessControl.EventWaitHandleAccessRule(
                    System.Security.Principal.WindowsIdentity.GetCurrent().User,
                    System.Security.AccessControl.EventWaitHandleRights.Synchronize | System.Security.AccessControl.EventWaitHandleRights.Modify,
                    System.Security.AccessControl.AccessControlType.Allow));
                bool created;
                exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName, out created, security);
                var ctx = SynchronizationContext.Current;
                ThreadPool.RegisterWaitForSingleObject(exitEvent, delegate
                {
                    if (ctx != null) ctx.Post(delegate { ExitThread(); }, null);
                    else Application.Exit();
                }, null, Timeout.Infinite, true);
            }
            catch (Exception ex) { Log.Write("exit event: " + ex.Message); }
        }

        bool RegisterHotkey()
        {
            uint mods; Keys key;
            bool ok = HotkeyParser.TryParse(settings.Hotkey, out mods, out key) && hotkeyWindow.Register(mods, key);
            hotkeyItem.Text = "Keyboard Fix — " + settings.Hotkey + (ok ? "" : " (לא פעיל!)");
            tray.Text = "Keyboard Fix (" + settings.Hotkey + ")";
            pauseItem.Checked = false;
            floater.Active = settings.FloatingButton;
            floatItem.Checked = settings.FloatingButton;
            return ok;
        }

        void ReloadSettings(bool notify)
        {
            hotkeyWindow.Unregister();
            settings = Settings.Load();
            bool ok = RegisterHotkey();
            if (notify)
                Balloon(ok ? "קיצור המקשים: " + settings.Hotkey
                           : "קיצור המקשים " + settings.Hotkey + " לא תקין או תפוס.",
                        ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        void TogglePause()
        {
            if (pauseItem.Checked) ReloadSettings(false);
            else { hotkeyWindow.Unregister(); floater.Active = false; pauseItem.Checked = true; }
        }

        void ToggleFloating()
        {
            settings.FloatingButton = !settings.FloatingButton;
            Settings.SetValue("FloatingButton", settings.FloatingButton ? "true" : "false");
            floatItem.Checked = settings.FloatingButton;
            if (!pauseItem.Checked) floater.Active = settings.FloatingButton;
        }

        void ToggleAutostart()
        {
            try { Autostart.Set(!Autostart.IsEnabled); }
            catch (Exception ex) { Balloon("שגיאה: " + ex.Message, ToolTipIcon.Error); }
        }

        void OpenSettings()
        {
            Settings.Load(); // makes sure the file exists
            try { Process.Start("notepad.exe", "\"" + Settings.FilePath + "\""); } catch { }
        }

        void Balloon(string text, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(4000, "Keyboard Fix", text, icon);
        }

        void OnHotkey()
        {
            uint mods; Keys key;
            HotkeyParser.TryParse(settings.Hotkey, out mods, out key);
            TextSwapper.Run(key, settings.SwitchLayout);
        }

        protected override void ExitThreadCore()
        {
            hotkeyWindow.Unregister();
            hotkeyWindow.DestroyHandle();
            floater.Dispose();
            TextSwapper.Typing.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var bg = new SolidBrush(Color.FromArgb(37, 99, 235)))
                using (var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.FillEllipse(bg, 0, 0, 31, 31);
                    g.DrawString("אA", font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // ---------------------------------------------------------------- Hotkey

    class HotkeyWindow : NativeWindow
    {
        const int WM_HOTKEY = 0x0312;
        const int WM_APP_HOOKHOTKEY = 0x8000 + 1;
        const int HOTKEY_ID = 0x4B46;
        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        readonly Action callback;
        readonly Native.LowLevelProc keyboardProc; // keep a reference so the GC doesn't collect it
        IntPtr keyboardHook = IntPtr.Zero;
        bool registered;
        uint hookMods;
        Keys hookKey;
        bool swallowingKey;
        bool lctrl, rctrl, lshift, rshift, lalt, ralt, lwin, rwin;

        public HotkeyWindow(Action callback)
        {
            this.callback = callback;
            keyboardProc = KeyboardCallback;
            CreateHandle(new CreateParams());
        }

        public bool Register(uint modifiers, Keys key)
        {
            Unregister();
            if (key == Keys.CapsLock || key == Keys.Scroll || key == Keys.NumLock)
            {
                // Toggle keys: RegisterHotKey would still flip the toggle state, so catch them with a
                // low-level hook and swallow them instead.
                hookMods = modifiers;
                hookKey = key;
                keyboardHook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, keyboardProc, Native.GetModuleHandle(null), 0);
                registered = keyboardHook != IntPtr.Zero;
            }
            else
                registered = Native.RegisterHotKey(Handle, HOTKEY_ID, modifiers | Native.MOD_NOREPEAT, (uint)key);
            return registered;
        }

        public void Unregister()
        {
            if (keyboardHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
            else if (registered) Native.UnregisterHotKey(Handle, HOTKEY_ID);
            registered = false;
            swallowingKey = false;
        }

        uint CurrentModifiers()
        {
            uint m = 0;
            if (lctrl || rctrl) m |= Native.MOD_CONTROL;
            if (lshift || rshift) m |= Native.MOD_SHIFT;
            if (lalt || ralt) m |= Native.MOD_ALT;
            if (lwin || rwin) m |= Native.MOD_WIN;
            return m;
        }

        IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                switch ((Keys)vk)
                {
                    case Keys.LControlKey: case Keys.ControlKey: lctrl = down; break;
                    case Keys.RControlKey: rctrl = down; break;
                    case Keys.LShiftKey: case Keys.ShiftKey: lshift = down; break;
                    case Keys.RShiftKey: rshift = down; break;
                    case Keys.LMenu: case Keys.Menu: lalt = down; break;
                    case Keys.RMenu: ralt = down; break;
                    case Keys.LWin: lwin = down; break;
                    case Keys.RWin: rwin = down; break;
                }
                if (vk == (int)hookKey)
                {
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (swallowingKey) return (IntPtr)1; // auto-repeat
                        if (CurrentModifiers() == hookMods)
                        {
                            swallowingKey = true;
                            Native.PostMessage(Handle, WM_APP_HOOKHOTKEY, IntPtr.Zero, IntPtr.Zero);
                            return (IntPtr)1;
                        }
                    }
                    else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && swallowingKey)
                    {
                        swallowingKey = false;
                        return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) || m.Msg == WM_APP_HOOKHOTKEY)
            {
                callback();
                return;
            }
            base.WndProc(ref m);
        }
    }
    static class HotkeyParser
    {
        public static bool TryParse(string text, out uint modifiers, out Keys key)
        {
            modifiers = 0; key = Keys.None;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string t = raw.Trim();
                string l = t.ToLowerInvariant();
                if (l == "ctrl" || l == "control") modifiers |= Native.MOD_CONTROL;
                else if (l == "shift") modifiers |= Native.MOD_SHIFT;
                else if (l == "alt") modifiers |= Native.MOD_ALT;
                else if (l == "win") modifiers |= Native.MOD_WIN;
                else if (t.Length == 1 && char.IsDigit(t[0])) key = Keys.D0 + (t[0] - '0');
                else if (t.Length == 1 && char.IsLetter(t[0]) && t[0] < 128) key = (Keys)char.ToUpperInvariant(t[0]);
                else
                {
                    try { key = (Keys)Enum.Parse(typeof(Keys), t, true); }
                    catch { return false; }
                }
            }
            return key != Keys.None;
        }
    }

    // ---------------------------------------------------------------- The actual work

    static class TextSwapper
    {
        static bool busy;
        public static TypingBuffer Typing;

        public static void Run(Keys hotkeyKey, bool switchLayout)
        {
            if (busy) return;
            busy = true;
            try { RunCore(hotkeyKey, switchLayout); }
            catch (Exception ex) { Log.Write("error: " + ex); }
            finally { busy = false; }
        }

        static void RunCore(Keys hotkeyKey, bool switchLayout)
        {
            WaitForKeysReleased(hotkeyKey, 1500);

            // In a terminal Ctrl+C would interrupt the running program - retype the typed line instead.
            if (TextFocus.ForegroundIsTerminal())
            {
                var tdir = Typing == null ? LayoutConverter.Direction.None : Typing.Retype();
                Log.Write("terminal retype: " + tdir);
                if (switchLayout && tdir != LayoutConverter.Direction.None)
                    KeyboardLayout.SwitchTo(tdir == LayoutConverter.Direction.ToHebrew ? 0x0D : 0x09);
                return;
            }

            IDataObject backup = BackupClipboard();

            // Nothing selected in a text box -> convert the whole box (Ctrl+A first).
            var sel = TextFocus.OffUiThread<TextFocus.Selection>(TextFocus.GetSelection, TextFocus.Selection.Unknown, 700);
            string selected = null;
            bool triedSelectAll = false;
            if (sel == TextFocus.Selection.None && TextFocus.OffUiThread<bool>(TextFocus.FocusIsEditable, false, 700))
            {
                selected = SelectAllAndCopy();
                triedSelectAll = true;
            }
            else
            {
                uint seq = Native.GetClipboardSequenceNumber();
                Native.SendChord(Keys.C);
                selected = WaitForClipboardText(seq, 800);
                if (!string.IsNullOrEmpty(selected) && IsVsCodeEmptySelectionCopy()) selected = null; // code editor, no selection
                else if (string.IsNullOrEmpty(selected) && TextFocus.OffUiThread<bool>(TextFocus.FocusIsEditable, false, 700))
                {
                    selected = SelectAllAndCopy();
                    triedSelectAll = true;
                }
            }
            Log.Write("hotkey; uia=" + sel + " selectAll=" + triedSelectAll + "; selected: " + (selected ?? "<none>"));
            if (string.IsNullOrEmpty(selected))
            {
                RestoreClipboard(backup);
                return;
            }

            LayoutConverter.Direction dir;
            string converted = LayoutConverter.Convert(selected, out dir);
            if (dir == LayoutConverter.Direction.None || converted == selected)
            {
                RestoreClipboard(backup);
                return;
            }

            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, converted);
            data.SetData(DataFormats.Text, converted);
            ExcludeFromClipboardHistory(data);
            if (!SetClipboard(data))
            {
                RestoreClipboard(backup);
                return;
            }

            Log.Write("pasting: " + converted);
            Native.SendChord(Keys.V);

            if (switchLayout)
                KeyboardLayout.SwitchTo(dir == LayoutConverter.Direction.ToHebrew ? 0x0D : 0x09);

            // Give the target app time to read the clipboard before restoring it.
            Sleep(450);
            RestoreClipboard(backup);
        }

        static string SelectAllAndCopy()
        {
            Native.SendChord(Keys.A);
            Sleep(60);
            uint seq = Native.GetClipboardSequenceNumber();
            Native.SendChord(Keys.C);
            return WaitForClipboardText(seq, 800);
        }

        static void WaitForKeysReleased(Keys hotkeyKey, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!Native.IsDown(hotkeyKey) && !Native.IsDown(Keys.ControlKey) &&
                    !Native.IsDown(Keys.ShiftKey) && !Native.IsDown(Keys.Menu) &&
                    !Native.IsDown(Keys.LWin) && !Native.IsDown(Keys.RWin))
                    return;
                Sleep(10);
            }
            var held = new List<string>();
            foreach (Keys k in new[] { hotkeyKey, Keys.ControlKey, Keys.ShiftKey, Keys.Menu, Keys.LWin, Keys.RWin })
                if (Native.IsDown(k)) held.Add(k.ToString());
            Log.Write("keys still held after timeout: " + string.Join(",", held.ToArray()));
            // Still held: release Shift/Alt logically so they don't turn Ctrl+C into something else.
            Native.ReleaseIfDown(Keys.LShiftKey, Keys.RShiftKey, Keys.LMenu, Keys.RMenu, hotkeyKey);
        }

        static string WaitForClipboardText(uint seqBefore, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Native.GetClipboardSequenceNumber() != seqBefore)
                {
                    Sleep(30);
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
                        }
                        catch (ExternalException) { Sleep(30); }
                    }
                    return null;
                }
                Sleep(15);
            }
            return null;
        }

        // VS Code copies the whole line when nothing is selected - don't treat that as a selection.
        static bool IsVsCodeEmptySelectionCopy()
        {
            try
            {
                if (!Clipboard.ContainsData("vscode-editor-data")) return false;
                object o = Clipboard.GetData("vscode-editor-data");
                string s = o as string;
                var ms = o as MemoryStream;
                if (s == null && ms != null) s = Encoding.UTF8.GetString(ms.ToArray());
                return s != null && s.Replace(" ", "").IndexOf("\"isFromEmptySelection\":true", StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        static IDataObject BackupClipboard()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    IDataObject src = Clipboard.GetDataObject();
                    if (src == null) return null;
                    var copy = new DataObject();
                    foreach (string f in src.GetFormats(false))
                    {
                        try
                        {
                            object d = src.GetData(f, false);
                            if (d != null) copy.SetData(f, false, d);
                        }
                        catch { }
                    }
                    return copy;
                }
                catch (ExternalException) { Sleep(30); }
            }
            return null;
        }

        static void RestoreClipboard(IDataObject backup)
        {
            try
            {
                if (backup == null || backup.GetFormats(false).Length == 0) Clipboard.Clear();
                else
                {
                    ExcludeFromClipboardHistory(backup);
                    Clipboard.SetDataObject(backup, true, 10, 50);
                }
            }
            catch { }
        }

        static bool SetClipboard(IDataObject data)
        {
            try { Clipboard.SetDataObject(data, true, 10, 50); return true; }
            catch { return false; }
        }

        // Keeps our temporary clipboard content out of Win+V clipboard history / cloud sync.
        static void ExcludeFromClipboardHistory(IDataObject data)
        {
            try
            {
                data.SetData("ExcludeClipboardContentFromMonitorProcessing", false, new MemoryStream(new byte[4]));
                data.SetData("CanIncludeInClipboardHistory", false, new MemoryStream(new byte[4]));
                data.SetData("CanUploadToCloudClipboard", false, new MemoryStream(new byte[4]));
            }
            catch { }
        }

        // Waits while still pumping messages: when we own the clipboard, other apps' copy/paste
        // sends us synchronous clipboard messages and would block until we process them.
        static void Sleep(int ms)
        {
            var sw = Stopwatch.StartNew();
            do { Application.DoEvents(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
        }
    }

    // Writes to %APPDATA%\KeyboardFix\debug.log only if that file already exists.
    static class Log
    {
        static readonly string FilePath = Path.Combine(Settings.Dir, "debug.log");
        public static void Write(string msg)
        {
            try { if (File.Exists(FilePath)) File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n", Encoding.UTF8); }
            catch { }
        }
    }

    static class KeyboardLayout
    {
        const int WM_INPUTLANGCHANGEREQUEST = 0x0050;

        /// <param name="primaryLang">0x0D = Hebrew, 0x09 = English</param>
        public static void SwitchTo(int primaryLang)
        {
            try
            {
                int n = Native.GetKeyboardLayoutList(0, null);
                if (n <= 0) return;
                var list = new IntPtr[n];
                Native.GetKeyboardLayoutList(n, list);
                foreach (IntPtr hkl in list)
                {
                    if ((hkl.ToInt64() & 0x3FF) == primaryLang)
                    {
                        IntPtr hwnd = Native.GetForegroundWindow();
                        if (hwnd != IntPtr.Zero)
                            Native.PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
                        return;
                    }
                }
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Win32

    static class Native
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        const uint KEYEVENTF_KEYUP = 0x2;

        static readonly UIntPtr Marker = (UIntPtr)(uint)TypingBuffer.OurInputMarker;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static INPUT KeyInput(ushort vk, char unicode, uint flags)
        {
            var i = new INPUT { type = 1 /* KEYBOARD */ };
            i.u.ki = new KEYBDINPUT { wVk = vk, wScan = unicode, dwFlags = flags, dwExtraInfo = (IntPtr)TypingBuffer.OurInputMarker };
            return i;
        }

        public static void Send(List<INPUT> inputs)
        {
            if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public int x, y; }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInstance, int cursorName);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO pci);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static bool IsDown(Keys k)
        {
            return (GetAsyncKeyState((int)k) & 0x8000) != 0;
        }

        public static void ReleaseIfDown(params Keys[] keys)
        {
            foreach (Keys k in keys)
                if (IsDown(k)) keybd_event((byte)k, 0, KEYEVENTF_KEYUP, Marker);
        }

        /// <summary>Sends Ctrl+key.</summary>
        public static void SendChord(Keys key)
        {
            bool ctrlHeld = IsDown(Keys.ControlKey);
            if (!ctrlHeld) keybd_event((byte)Keys.ControlKey, 0, 0, Marker);
            keybd_event((byte)key, 0, 0, Marker);
            keybd_event((byte)key, 0, KEYEVENTF_KEYUP, Marker);
            if (!ctrlHeld) keybd_event((byte)Keys.ControlKey, 0, KEYEVENTF_KEYUP, Marker);
        }
    }
}
