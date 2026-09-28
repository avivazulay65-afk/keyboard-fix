using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace KeyboardFix
{
    /// <summary>
    /// Asks Windows (UI Automation, plus the classic Win32 caret) about the control that has keyboard focus:
    /// is it an editable text box, and does it have selected text?
    /// </summary>
    static class TextFocus
    {
        public enum Selection { Unknown, None, InStaticText, InTextBox }

        /// <summary>Must not be called on the UI thread (may block on the target app).</summary>
        public static Selection GetSelection()
        {
            try
            {
                AutomationElement el = AutomationElement.FocusedElement;
                TreeWalker walker = TreeWalker.RawViewWalker;
                bool editable = false;
                for (int depth = 0; el != null && depth < 6; depth++)
                {
                    editable = editable || IsEditable(el);
                    object pattern;
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                    {
                        var tp = (TextPattern)pattern;
                        if (tp.SupportedTextSelection == SupportedTextSelection.None) return Selection.Unknown;
                        TextPatternRange selected = null;
                        foreach (TextPatternRange r in tp.GetSelection())
                            if (r.GetText(64).Trim().Length > 0) { selected = r; break; }
                        if (selected == null) return Selection.None;
                        return editable || IsEditable(el) || IsWritable(selected) || HasWin32Caret()
                            ? Selection.InTextBox : Selection.InStaticText;
                    }
                    el = walker.GetParent(el);
                }
            }
            catch (Exception ex) { Log.Write("uia: " + ex.GetType().Name + ": " + ex.Message); }
            return Selection.Unknown;
        }

        /// <summary>Is keyboard focus in an editable text control? Must not be called on the UI thread.</summary>
        public static bool FocusIsEditable()
        {
            if (HasWin32Caret()) return true;
            try
            {
                AutomationElement el = AutomationElement.FocusedElement;
                TreeWalker walker = TreeWalker.RawViewWalker;
                for (int depth = 0; el != null && depth < 6; depth++)
                {
                    if (IsEditable(el)) return true;
                    object pattern;
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                    {
                        // The caret / selection range tells whether this text can be edited (works for Chrome content).
                        foreach (TextPatternRange r in ((TextPattern)pattern).GetSelection())
                            if (IsWritable(r)) return true;
                        return false;
                    }
                    el = walker.GetParent(el);
                }
            }
            catch { }
            return false;
        }

        /// <summary>Runs a check on a worker thread, pumping messages meanwhile; returns fallback on timeout.</summary>
        public static T OffUiThread<T>(Func<T> check, T fallback, int timeoutMs)
        {
            T result = fallback;
            bool done = false;
            var t = new Thread(delegate() { try { result = check(); } catch { } done = true; });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!done && sw.ElapsedMilliseconds < timeoutMs)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(5);
            }
            return done ? result : fallback;
        }

        static bool IsWritable(TextPatternRange r)
        {
            try
            {
                object v = r.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                return v is bool && !(bool)v;
            }
            catch { return false; }
        }

        static bool IsEditable(AutomationElement el)
        {
            object vp;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out vp))
                return !((ValuePattern)vp).Current.IsReadOnly;
            return el.Current.ControlType == ControlType.Edit;
        }

        public static bool HasWin32Caret()
        {
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            uint pid;
            uint tid = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return GetGUIThreadInfo(tid, ref info) && info.hwndCaret != IntPtr.Zero;
        }

        /// <summary>Windows Terminal / classic console: Ctrl+C there interrupts the running program.</summary>
        public static bool ForegroundIsTerminal()
        {
            var sb = new StringBuilder(256);
            GetClassName(GetForegroundWindow(), sb, sb.Capacity);
            string c = sb.ToString();
            return c == "ConsoleWindowClass" || c == "CASCADIA_HOSTING_WINDOW_CLASS" || c == "PseudoConsoleWindow" ||
                   c == "mintty" || c == "VirtualConsoleClass";
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    }
}
