# Plays the README demo in a window: types Hebrew on the English layout, fixes it with Ctrl+CapsLock,
# then fixes an English word typed on the Hebrew layout with the floating button.
# Keyboard Fix must be running. Run record.ps1 to capture it as a GIF.
param([string]$RectFile)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class D {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern void keybd_event(byte v, byte s, uint f, UIntPtr e);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[] l);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 public static RECT? FindVisible(uint pid, int maxW) {
   RECT? found = null;
   EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); RECT r;
     if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r) && (r.R - r.L) < maxW && (r.R - r.L) > 0) { found = r; return false; }
     return true; }, IntPtr.Zero);
   return found;
 }
}
"@
[D]::SetProcessDPIAware() | Out-Null
$n = [D]::GetKeyboardLayoutList(0, $null); $list = New-Object IntPtr[] $n; [D]::GetKeyboardLayoutList($n, $list) | Out-Null
$en = $list | ? { ($_.ToInt64() -band 0x3FF) -eq 0x09 } | select -First 1
$he = $list | ? { ($_.ToInt64() -band 0x3FF) -eq 0x0D } | select -First 1

$form = New-Object Windows.Forms.Form -Property @{
  Text = 'Keyboard Fix'; Width = 900; Height = 300; StartPosition = 'Manual'; Left = 300; Top = 250
  TopMost = $true; BackColor = [Drawing.Color]::White; FormBorderStyle = 'FixedSingle'; MaximizeBox = $false }
$caption = New-Object Windows.Forms.Label -Property @{
  Dock = 'Top'; Height = 70; TextAlign = 'MiddleCenter'; Font = New-Object Drawing.Font('Segoe UI Semibold', 18)
  ForeColor = [Drawing.Color]::FromArgb(37, 99, 235); BackColor = [Drawing.Color]::FromArgb(239, 246, 255) }
$tb = New-Object Windows.Forms.TextBox -Property @{
  Multiline = $true; BorderStyle = 'None'; Font = New-Object Drawing.Font('Segoe UI', 28); Left = 30; Top = 100; Width = 820; Height = 140
  RightToLeft = 'No' }
$form.Controls.Add($tb); $form.Controls.Add($caption)

function Wait($ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } }
function Say($text) { $caption.Text = $text; $caption.Refresh() }
function Front { [D]::SetForegroundWindow($form.Handle) | Out-Null; $tb.Focus() | Out-Null }
function Check { if ([D]::GetForegroundWindow() -ne $form.Handle) { throw 'demo window lost focus - aborting' } }
function Key([byte]$vk, [switch]$Shift) { Check
  if ($Shift) { [D]::keybd_event(0x10,0,0,[UIntPtr]::Zero) }
  [D]::keybd_event($vk,0,0,[UIntPtr]::Zero); [D]::keybd_event($vk,0,2,[UIntPtr]::Zero)
  if ($Shift) { [D]::keybd_event(0x10,0,2,[UIntPtr]::Zero) } }
function TypeKeys([string]$s) { foreach ($c in $s.ToCharArray()) {
  $vk = switch ($c) { ' ' { 0x20 } ',' { 0xBC } '.' { 0xBE } default { [byte][char]([string]$c).ToUpper() } }
  Key $vk; Wait 85 } }
function Hotkey { Check; [D]::keybd_event(0x11,0,0,[UIntPtr]::Zero); Wait 120; [D]::keybd_event(0x14,0,0,[UIntPtr]::Zero); [D]::keybd_event(0x14,0,2,[UIntPtr]::Zero); Wait 120; [D]::keybd_event(0x11,0,2,[UIntPtr]::Zero) }
function Layout($hkl) { [D]::PostMessage($form.Handle, 0x50, [IntPtr]::Zero, $hkl) | Out-Null; Wait 300 }

$form.Add_Shown({
  try {
    if ($RectFile) { $r = $form.Bounds; "$($r.X) $($r.Y) $($r.Width) $($r.Height)" | Set-Content $RectFile }
    Front; Layout $en
    Say 'Typing in Hebrew... but the keyboard is on English'; Wait 1200
    TypeKeys 'tbh rumv kkf, kgcusv njr canubv'; Wait 900
    Say 'Select the text'; Wait 500
    $tb.SelectAll(); Wait 1100
    Say 'Press Ctrl + CapsLock'; Wait 700
    Hotkey; Wait 1200
    $tb.RightToLeft = 'Yes'; $tb.SelectionLength = 0
    Say 'Fixed!  ✓'; Wait 2200

    $tb.Clear(); $tb.RightToLeft = 'No'; Front; Layout $he
    Say 'Now English, typed on the Hebrew keyboard'; Wait 1000
    TypeKeys 'hello'; Key 0x20; Wait 85; TypeKeys 'world'; Wait 900
    Say 'Select with the mouse, then click the  אA  button'; Wait 600
    $p0 = $tb.PointToScreen((New-Object Drawing.Point 2, 30))
    $p1 = $tb.PointToScreen((New-Object Drawing.Point 330, 30))
    [D]::SetCursorPos($p1.X, $p1.Y) | Out-Null; Wait 400
    [D]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    for ($i = 1; $i -le 25; $i++) { [D]::SetCursorPos([int]($p1.X + ($p0.X - $p1.X) * $i / 25), $p1.Y) | Out-Null; Wait 30 }
    [D]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Wait 1300
    $kf = (Get-Process KeyboardFix | select -First 1).Id
    $r = [D]::FindVisible($kf, 200)
    if (-not $r) { throw 'floating button did not appear' }
    $c = [Windows.Forms.Cursor]::Position
    $target = New-Object Drawing.Point ([int](($r.L + $r.R) / 2)), ([int](($r.T + $r.B) / 2))
    for ($i = 1; $i -le 12; $i++) { [D]::SetCursorPos([int]($c.X + ($target.X - $c.X) * $i / 12), [int]($c.Y + ($target.Y - $c.Y) * $i / 12)) | Out-Null; Wait 30 }
    Wait 500
    [D]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Wait 60; [D]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Wait 1500
    Say 'Keyboard Fix  -  works in every app'; Wait 2200
  } catch { Say $_.Exception.Message; Wait 1500 }
  $form.Close()
})
[void]$form.ShowDialog()
