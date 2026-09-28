using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyboardFix
{
    /// <summary>
    /// Remembers the keys typed on the current line (memory only; cleared on Enter, clicks, window switches, etc.)
    /// so the line can be erased and retyped in the other language - used in terminals, where selecting and
    /// copying with Ctrl+C isn't possible (Ctrl+C would interrupt the running program).
    /// </summary>
    class TypingBuffer : IDisposable
    {
        public const int OurInputMarker = 0x4B465846; // dwExtraInfo on input we inject ourselves
        const int MaxKeys = 500;

        struct Key
        {
            public int Vk;
            public bool Shift;
            public bool Hebrew; // the layout that was active when it was typed
        }

        readonly List<Key> keys = new List<Key>();
        IntPtr window = IntPtr.Zero;
        readonly Native.LowLevelProc keyboardProc, mouseProc;
        IntPtr keyboardHook, mouseHook;

        public TypingBuffer()
        {
            keyboardProc = KeyboardCallback;
            mouseProc = MouseCallback;
            IntPtr mod = Native.GetModuleHandle(null);
            keyboardHook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, keyboardProc, mod, 0);
            mouseHook = Native.SetWindowsHookEx(14 /* WH_MOUSE_LL */, mouseProc, mod, 0);
        }

        public void Dispose()
        {
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            keyboardHook = mouseHook = IntPtr.Zero;
            keys.Clear();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    if (msg == 0x100 || msg == 0x104) // WM_KEYDOWN / WM_SYSKEYDOWN
                    {
                        var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                        if (k.dwExtraInfo.ToInt64() != OurInputMarker) OnKeyDown(k.vkCode);
                    }
                }
                catch { }
            }
            return Native.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            int msg = wParam.ToInt32();
            if (nCode >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207)) keys.Clear(); // any click moves the caret
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        void OnKeyDown(int vk)
        {
            var key = (Keys)vk;
            switch (key)
            {
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.LWin: case Keys.RWin: case Keys.CapsLock:
                    return; // modifiers alone don't change the text
                case Keys.Back:
                    if (keys.Count > 0) keys.RemoveAt(keys.Count - 1);
                    return;
            }

            IntPtr fg = Native.GetForegroundWindow();
            if (fg != window) { keys.Clear(); window = fg; }

            bool ctrlOrAlt = Native.IsDown(Keys.ControlKey) || Native.IsDown(Keys.Menu) ||
                             Native.IsDown(Keys.LWin) || Native.IsDown(Keys.RWin);
            bool shift = Native.IsDown(Keys.ShiftKey);
            if (ctrlOrAlt || UsChar(vk, shift) == '\0')
            {
                keys.Clear(); // Enter, arrows, Home, Tab, shortcuts... - the line or caret changed
                return;
            }
            if (keys.Count >= MaxKeys) keys.RemoveAt(0);
            keys.Add(new Key { Vk = vk, Shift = shift, Hebrew = ForegroundLayoutIsHebrew() });
        }

        static bool ForegroundLayoutIsHebrew()
        {
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            return (Native.GetKeyboardLayout(tid).ToInt64() & 0x3FF) == 0x0D;
        }

        /// <summary>
        /// Erases the remembered text and retypes it in the other layout.
        /// Returns the direction of the last key (the language to switch to), or None if nothing was remembered.
        /// </summary>
        public LayoutConverter.Direction Retype()
        {
            if (keys.Count == 0 || Native.GetForegroundWindow() != window) return LayoutConverter.Direction.None;

            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                Key k = keys[i];
                char us = UsChar(k.Vk, k.Shift);
                // Typed on Hebrew -> what the key gives on English, and vice versa.
                sb.Append(k.Hebrew ? us : (k.Shift && !char.IsLetter(us) ? us : LayoutConverter.ToHebrew(us)));
                k.Hebrew = !k.Hebrew; // pressing the hotkey again flips it back
                keys[i] = k;
            }

            var inputs = new List<Native.INPUT>();
            for (int i = 0; i < keys.Count; i++)
            {
                inputs.Add(Native.KeyInput(0x08, '\0', 0));
                inputs.Add(Native.KeyInput(0x08, '\0', 2 /* KEYUP */));
            }
            foreach (char c in sb.ToString())
            {
                inputs.Add(Native.KeyInput(0, c, 4 /* UNICODE */));
                inputs.Add(Native.KeyInput(0, c, 4 | 2));
            }
            Native.Send(inputs);
            Log.Write("retyped in terminal: " + sb);

            return keys[keys.Count - 1].Hebrew ? LayoutConverter.Direction.ToHebrew : LayoutConverter.Direction.ToEnglish;
        }

        /// <summary>The character a key produces on the US layout, or '\0' if it isn't a text key.</summary>
        static char UsChar(int vk, bool shift)
        {
            if (vk >= 'A' && vk <= 'Z') return shift ? (char)vk : char.ToLowerInvariant((char)vk);
            if (vk >= '0' && vk <= '9') return shift ? ")!@#$%^&*("[vk - '0'] : (char)vk;
            if (vk >= 0x60 && vk <= 0x69) return (char)('0' + vk - 0x60); // numpad digits
            if (vk == 0x20) return ' ';
            const string oemVks = "º»¼½¾¿ÀÛÜÝÞ";
            int i = oemVks.IndexOf((char)vk);
            if (i >= 0) return shift ? ":+<_>?~{|}\""[i] : ";=,-./`[\\]'"[i];
            return '\0';
        }
    }
}
