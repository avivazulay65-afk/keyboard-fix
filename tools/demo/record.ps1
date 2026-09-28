# Records demo.ps1 to assets\demo.gif (needs ffmpeg on PATH and Keyboard Fix running).
param([string]$Out = (Join-Path $PSScriptRoot '..\..\assets\demo.gif'), [int]$Fps = 15)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Drawing; using System.Runtime.InteropServices;
public static class Cap {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
 [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT pt; }
 [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
 [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
 [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
 [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO info);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
 public static Bitmap Grab(Rectangle r) {
   var bmp = new Bitmap(r.Width, r.Height);
   using (var g = Graphics.FromImage(bmp)) {
     g.CopyFromScreen(r.Location, Point.Empty, r.Size);
     var ci = new CURSORINFO(); ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
     if (GetCursorInfo(ref ci) && ci.flags == 1) {
       ICONINFO ii; int hx = 0, hy = 0;
       if (GetIconInfo(ci.hCursor, out ii)) { hx = ii.xHotspot; hy = ii.yHotspot; DeleteObject(ii.hbmMask); DeleteObject(ii.hbmColor); }
       IntPtr hdc = g.GetHdc();
       DrawIconEx(hdc, ci.pt.X - r.X - hx, ci.pt.Y - r.Y - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3);
       g.ReleaseHdc(hdc);
     }
   }
   return bmp;
 }
}
"@ -ReferencedAssemblies System.Drawing
[Cap]::SetProcessDPIAware() | Out-Null

$work = Join-Path ([IO.Path]::GetTempPath()) ('kf-demo-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$rectFile = Join-Path $work 'rect.txt'
$demo = Start-Process powershell -PassThru -ArgumentList '-STA', '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', "`"$(Join-Path $PSScriptRoot 'demo.ps1')`"", '-RectFile', "`"$rectFile`""
for ($i = 0; $i -lt 100 -and -not (Test-Path $rectFile); $i++) { Start-Sleep -Milliseconds 100 }
if (-not (Test-Path $rectFile)) { throw 'demo window did not start' }
$x, $y, $w, $h = (Get-Content $rectFile) -split ' ' | % { [int]$_ }
$rect = New-Object Drawing.Rectangle $x, $y, $w, $h

$frame = 0; $interval = [int](1000 / $Fps); $sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $demo.HasExited) {
    $due = $frame * $interval
    $wait = $due - $sw.ElapsedMilliseconds
    if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    $bmp = [Cap]::Grab($rect)
    $bmp.Save((Join-Path $work ('f{0:D5}.png' -f $frame)), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $frame++
}
Write-Host "Captured $frame frames"

$pal = Join-Path $work 'palette.png'
$filters = "scale=760:-1:flags=lanczos"
ffmpeg -loglevel error -y -framerate $Fps -i (Join-Path $work 'f%05d.png') -vf "$filters,palettegen=stats_mode=diff" $pal
ffmpeg -loglevel error -y -framerate $Fps -i (Join-Path $work 'f%05d.png') -i $pal -lavfi "$filters [x]; [x][1:v] paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" -loop 0 $Out
Remove-Item $work -Recurse -Force
Write-Host "Wrote $Out ($([int]((Get-Item $Out).Length / 1KB)) KB)"
