# Removes Keyboard Fix (same as uninstalling it from Settings > Apps). Settings in %APPDATA%\KeyboardFix are kept.
$u = Join-Path $env:LOCALAPPDATA 'Programs\KeyboardFix\Uninstall.exe'
if (-not (Test-Path $u)) { Write-Host 'Keyboard Fix is not installed.'; exit 0 }
Start-Process $u -ArgumentList '--uninstall', '--quiet' -Wait
Write-Host 'Keyboard Fix removed.'
