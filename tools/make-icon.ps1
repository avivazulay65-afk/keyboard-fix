# Generates assets\app.ico (16-256 px, PNG-compressed entries): blue circle with "אA".
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\app.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(37, 99, 235))), 0, 0, $s - 1, $s - 1)
    $font = New-Object Drawing.Font 'Segoe UI', ([float]($s * 0.40)), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString([string]([char]0x05D0) + 'A', $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, ($s * 0.03), $s, $s), $sf)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [IO.File]::Create($Out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Wrote $Out"
