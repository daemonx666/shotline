param([Parameter(Mandatory = $true)][string]$ZipPath, [switch]$LaunchSmoke)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$extractDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ShotlinePortable-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -Path $ZipPath -DestinationPath $extractDirectory
$applicationDirectory = Join-Path $extractDirectory 'Shotline'
$required = @('Shotline.exe', 'Shotline.dll', 'Shotline.Core.dll', 'Shotline.runtimeconfig.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'PresentationFramework.dll', 'PresentationCore.dll',
    'WindowsBase.dll', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'build-info.json', 'docs/ACCEPTANCE.md',
    'licenses/dotnet-runtime-license.txt', 'licenses/dotnet-runtime-third-party-notices.txt', 'licenses/windows-desktop-license.txt')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $applicationDirectory $file) -PathType Leaf)) { throw "Missing portable file: $file" }
}
$runtime = Get-Content (Join-Path $applicationDirectory 'Shotline.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($null -ne $runtime.runtimeOptions.PSObject.Properties['framework'] -or $null -ne $runtime.runtimeOptions.PSObject.Properties['frameworks']) {
    throw 'Published package still requires an installed framework.'
}
$exe = Join-Path $applicationDirectory 'Shotline.exe'
$stream = [IO.File]::OpenRead($exe)
try {
    $reader = [IO.BinaryReader]::new($stream)
    if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Invalid executable header.' }
    $stream.Position = 0x3C; $peOffset = $reader.ReadInt32(); $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Portable executable is not x64.' }
} finally { $stream.Dispose() }
if ($LaunchSmoke) {
    if ($env:OS -ne 'Windows_NT') { throw 'Launch check requires Windows. Use publish.ps1 -SkipLaunchCheck for cross-builds.' }
    $process = Start-Process -FilePath $exe -ArgumentList '--ci-smoke' -WorkingDirectory $applicationDirectory -PassThru
    if (-not $process.WaitForExit(20000)) { $process.Kill(); throw 'Isolated application startup check timed out.' }
    if ($process.ExitCode -ne 0) { throw "Isolated application startup failed: $($process.ExitCode)" }
    Write-Host 'PASS Windows isolated application startup and graceful exit (desktop interactions not accepted).'
}
Write-Host "PASS x64 self-contained portable package ($($required.Count) required files)."
