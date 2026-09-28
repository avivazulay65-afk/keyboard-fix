# Builds bin\KeyboardFix.exe and the single-file installer dist\KeyboardFix-Setup.exe,
# using the C# compiler that ships with Windows (.NET Framework 4.x) - nothing to install.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler (.NET Framework 4) not found.' }

$icon = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-icon.ps1') -Out $icon }

$bin = Join-Path $root 'bin'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $bin, $dist | Out-Null

# The app
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:"$icon" `
    /out:"$bin\KeyboardFix.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /lib:"$(Split-Path $csc)\WPF" /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll `
    (Get-ChildItem "$root\src\*.cs").FullName
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $bin\KeyboardFix.exe"

# The installer (embeds the app)
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:"$icon" `
    /win32manifest:"$root\setup\setup.manifest" `
    /resource:"$bin\KeyboardFix.exe,KeyboardFix.exe" `
    /out:"$dist\KeyboardFix-Setup.exe" `
    /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll /r:System.Core.dll `
    "$root\setup\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw "Setup build failed ($LASTEXITCODE)" }
Write-Host "Built $dist\KeyboardFix-Setup.exe"
