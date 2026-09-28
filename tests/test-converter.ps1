# Unit tests for LayoutConverter (runs without installing anything).
$ErrorActionPreference = 'Stop'
$src = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot '..\src\LayoutConverter.cs')
Add-Type -TypeDefinition $src -Language CSharp

$cases = @(
    @('tbh rumv ahvhv rauo cgcrh', 'אני רוצה שיהיה רשום בעברי'),
    @('אני רוצה שיהיה רשום בעברי', 'tbh rumv ahvhv rauo cgcrh'),
    @('akuo', 'שלום'),
    @('שלום', 'akuo'),
    @('Akuo', 'שלום'),
    @('יקךךם', 'hello'),
    @('hello', 'יקךךם'),
    @('dhfh,', 'גיכית'),
    @('ן', 'i'),
    @('123 !?', '123 !?'),
    @('ין 2026', 'hi 2026'),
    @('akuo עולם', 'שלום guko'),
    @('שלום hello', 'akuo יקךךם'),
    @('tbh rumv to go to עברית', 'אני רוצה אם עם אם gcrh,'),
    @('akuo, guko.', 'שלוםת עולםץ'),
    @('akuo ,', 'שלום ת'),
    @('ab שמ', 'שנ an')
)

$fail = 0
foreach ($c in $cases) {
    $got = [KeyboardFix.LayoutConverter]::Convert($c[0])
    if ($got -ceq $c[1]) { Write-Host "PASS  '$($c[0])' -> '$got'" }
    else { Write-Host "FAIL  '$($c[0])' -> '$got' (expected '$($c[1])')" -ForegroundColor Red; $fail++ }
}
if ($fail) { exit 1 } else { Write-Host "All $($cases.Count) tests passed." }
