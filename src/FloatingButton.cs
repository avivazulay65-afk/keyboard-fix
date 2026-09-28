using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;

namespace KeyboardFix
{
    /// <summary>
    /// A small "אA" button that pops up next to the mouse right after text was selected with the mouse
    /// (drag or double-click over text). Clicking it converts the selection without taking focus
    /// away from the app you're typing in.
    /// </summary>
    class FloatingButton : Form
    {
        const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204,
                  WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A;
        const int ShowForMs = 3000, DragThresholdPx = 8, LeaveDistancePx = 180;

        readonly Action onClick;
        readonly Timer hideTimer = new Timer();
        readonly Native.LowLevelProc hookProc; // keep a reference so the GC doesn't collect it
        IntPtr hook = IntPtr.Zero;
        readonly IntPtr ibeam;

        Point downPos;
        bool downOnText;
        int lastDownTime;
        Point lastDownPos;
        bool hovered;
        bool pressedOnMe;
        int checkId; // increments on every mouse-down so stale selection checks are ignored

        public FloatingButton(Action onClick)
        {
            this.onClick = onClick;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Size = new Size((int)(34 * scale), (int)(26 * scale));

            var tip = new ToolTip();
            tip.SetToolTip(this, "הפוך עברית ↔ אנגלית");

            ibeam = Native.LoadCursor(IntPtr.Zero, 32513 /* IDC_IBEAM */);
            hookProc = HookCallback;
            hideTimer.Interval = ShowForMs;
            hideTimer.Tick += delegate { HidePopup(); };

            MouseEnter += delegate { hovered = true; Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };

        }

        public bool Active
        {
            get { return hook != IntPtr.Zero; }
            set
            {
                if (value && hook == IntPtr.Zero)
                    hook = Native.SetWindowsHookEx(14 /* WH_MOUSE_LL */, hookProc, Native.GetModuleHandle(null), 0);
                else if (!value && hook != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                    HidePopup();
                }
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */ | 0x8 /* WS_EX_TOPMOST */;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int rad = Height / 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(r.X, r.Y, rad * 2, r.Height, 90, 180);
                path.AddArc(r.Right - rad * 2, r.Y, rad * 2, r.Height, 270, 180);
                path.CloseFigure();
                using (var bg = new SolidBrush(hovered ? Color.FromArgb(29, 78, 216) : Color.FromArgb(37, 99, 235)))
                    g.FillPath(bg, path);
            }
            using (var font = new Font("Segoe UI", Height * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("אA", font, Brushes.White, new RectangleF(0, 0, Width, Height), sf);
        }

        void ShowAt(Point c)
        {
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int x = Math.Min(Math.Max(c.X + 12, wa.Left), wa.Right - Width);
            int y = c.Y - Height - 10;
            if (y < wa.Top) y = c.Y + 20;
            Native.SetWindowPos(Handle, (IntPtr)(-1) /* HWND_TOPMOST */, x, y, 0, 0,
                0x0001 /* NOSIZE */ | 0x0010 /* NOACTIVATE */ | 0x0040 /* SHOWWINDOW */);
            Visible = true;
            hideTimer.Stop();
            hideTimer.Start();
        }

        void HidePopup()
        {
            hideTimer.Stop();
            if (Visible) Visible = false;
        }

        bool CursorIsIBeam()
        {
            var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            return Native.GetCursorInfo(ref ci) && ci.hCursor == ibeam;
        }

        bool OverMe(Point p)
        {
            return Visible && Bounds.Contains(p);
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                // Clicks on the button are handled here and swallowed, so they never reach (or activate) any window.
                if (msg == WM_LBUTTONDOWN && OverMe(Cursor.Position))
                {
                    pressedOnMe = true;
                    return (IntPtr)1;
                }
                if (msg == WM_LBUTTONUP && pressedOnMe)
                {
                    pressedOnMe = false;
                    if (OverMe(Cursor.Position))
                        BeginInvoke((MethodInvoker)delegate { HidePopup(); onClick(); });
                    return (IntPtr)1;
                }
                try { OnMouse(msg); } catch { }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        void OnMouse(int msg)
        {
            Point p = Cursor.Position; // virtualized the same way as our window coordinates
            if (msg == WM_LBUTTONDOWN)
            {
                if (OverMe(p)) return;
                HidePopup();
                checkId++;
                downPos = p;
                downOnText = CursorIsIBeam();
            }
            else if (msg == WM_LBUTTONUP)
            {
                if (OverMe(p)) return;
                int now = Environment.TickCount;
                bool dragged = Math.Abs(p.X - downPos.X) >= DragThresholdPx || Math.Abs(p.Y - downPos.Y) >= DragThresholdPx;
                bool doubleClick = now - lastDownTime <= SystemInformation.DoubleClickTime &&
                                   Math.Abs(p.X - lastDownPos.X) <= 4 && Math.Abs(p.Y - lastDownPos.Y) <= 4;
                lastDownTime = now;
                lastDownPos = p;
                if ((downOnText || CursorIsIBeam()) && (dragged || doubleClick))
                    ShowIfSelected(p, dragged);
            }
            else if (msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN || msg == WM_MOUSEWHEEL)
            {
                if (!OverMe(p)) HidePopup();
            }
            else if (Visible)
            {
                // Mouse moved far away from the button - the user isn't going for it.
                var b = Bounds;
                int dx = Math.Max(Math.Max(b.Left - p.X, p.X - b.Right), 0);
                int dy = Math.Max(Math.Max(b.Top - p.Y, p.Y - b.Bottom), 0);
                if (dx > LeaveDistancePx || dy > LeaveDistancePx) HidePopup();
            }
        }

        // Asks (off the UI thread) whether the focused control is a text box that really has selected text.
        void ShowIfSelected(Point at, bool dragged)
        {
            int id = checkId;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(80); // let the app update its selection first
                var sel = TextFocus.GetSelection();
                if (sel == TextFocus.Selection.Unknown)
                {
                    // Some apps (e.g. Chrome) switch their accessibility tree on only after the first request.
                    Thread.Sleep(300);
                    sel = TextFocus.GetSelection();
                }
                // Unknown (app doesn't expose its text): trust a drag, but only inside a classic text box with a caret.
                bool show = sel == TextFocus.Selection.InTextBox ||
                            (sel == TextFocus.Selection.Unknown && dragged && TextFocus.HasWin32Caret());
                Log.Write("selection check: " + sel + " dragged=" + dragged + " show=" + show);
                if (!show) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate { if (id == checkId) ShowAt(at); });
                }
                catch { }
            });
        }
        protected override void Dispose(bool disposing)
        {
            Active = false;
            if (disposing) hideTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
