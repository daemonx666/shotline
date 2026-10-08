param([switch]$DesktopTests)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & dotnet build Shotline.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    & dotnet run --project tests/Shotline.Tests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core behavior tests failed.' }
    if ($env:OS -eq 'Windows_NT') {
        $testArguments = @('run', '--project', 'tests/Shotline.WindowsTests', '--configuration', 'Release', '--no-build')
        if ($DesktopTests) { $testArguments += @('--', '--desktop') }
        & dotnet @testArguments
        if ($LASTEXITCODE -ne 0) { throw 'Windows behavior tests failed.' }
    } elseif ($DesktopTests) {
        throw 'Desktop tests require an interactive Windows desktop.'
    }
    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace validation failed.' }
} finally { Pop-Location }
