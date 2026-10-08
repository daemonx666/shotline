param([string]$Version = '1.0.0', [string]$OutputDirectory = '', [switch]$SkipLaunchCheck)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot ('artifacts/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $OutputDirectory) { throw 'Output directory already exists. Choose a new path; existing output is preserved.' }
$publishDirectory = Join-Path $OutputDirectory 'Shotline'
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
Push-Location $projectRoot
try {
    & dotnet publish src/Shotline/Shotline.csproj --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory "-p:Version=$Version" -p:PublishSingleFile=false -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md -Destination $publishDirectory
    $runtimeConfig = Get-Content (Join-Path $publishDirectory 'Shotline.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    $desktopVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
    if ([string]::IsNullOrWhiteSpace($runtimeVersion) -or [string]::IsNullOrWhiteSpace($desktopVersion)) {
        throw 'Cannot resolve bundled runtime versions for license inclusion.'
    }
    $cacheLine = (& dotnet nuget locals global-packages --list)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the NuGet runtime package cache.' }
    $nugetRoot = ($cacheLine -replace '^[^:]+:\s*', '').Trim()
    $licenseDirectory = Join-Path $publishDirectory 'licenses'
    New-Item -ItemType Directory -Path $licenseDirectory | Out-Null
    Copy-Item (Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/LICENSE.TXT") -Destination (Join-Path $licenseDirectory 'dotnet-runtime-license.txt')
    Copy-Item (Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/THIRD-PARTY-NOTICES.TXT") -Destination (Join-Path $licenseDirectory 'dotnet-runtime-third-party-notices.txt')
    Copy-Item (Join-Path $nugetRoot "microsoft.windowsdesktop.app.runtime.win-x64/$desktopVersion/LICENSE") -Destination (Join-Path $licenseDirectory 'windows-desktop-license.txt')
    $docsDirectory = Join-Path $publishDirectory 'docs'
    New-Item -ItemType Directory -Path $docsDirectory | Out-Null
    Copy-Item docs/ACCEPTANCE.md, docs/SOURCE-MAP.md -Destination $docsDirectory
    $commit = (& git rev-parse HEAD).Trim()
    $metadata = [ordered]@{ name = 'Shotline'; version = $Version; architecture = 'x64'; runtime = 'self-contained';
        commit = $commit; builtAtUtc = [DateTime]::UtcNow.ToString('o'); sdk = (& dotnet --version).Trim();
        buildHost = [Environment]::OSVersion.VersionString; desktopAcceptance = 'Not performed by CI' }
    $metadata | ConvertTo-Json | Set-Content (Join-Path $publishDirectory 'build-info.json') -Encoding utf8
    $zip = Join-Path $OutputDirectory "Shotline-$Version-win-x64.zip"
    Compress-Archive -Path $publishDirectory -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content ($zip + '.sha256') -Encoding ascii
    Copy-Item (Join-Path $publishDirectory 'build-info.json') -Destination $OutputDirectory
    $verifyArguments = @{ ZipPath = $zip }
    if (-not $SkipLaunchCheck) { $verifyArguments.LaunchSmoke = $true }
    & (Join-Path $PSScriptRoot 'verify-portable.ps1') @verifyArguments
    Write-Host "Portable ZIP: $zip"
    Write-Host "SHA256: $hash"
} finally { Pop-Location }
