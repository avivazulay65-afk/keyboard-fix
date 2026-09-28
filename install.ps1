# Builds from source and runs the installer (asks for admin rights once).
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$p = Start-Process (Join-Path $PSScriptRoot 'dist\KeyboardFix-Setup.exe') -ArgumentList '--quiet' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Setup failed ($($p.ExitCode))" }
Write-Host 'Keyboard Fix installed and running.'
