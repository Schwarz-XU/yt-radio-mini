<#
.SYNOPSIS
  Builds the Windows x64 portable package: artifacts\YTRadioMini\ and artifacts\YTRadioMini-<version>-win-x64.zip

.DESCRIPTION
  1. downloads and verifies mpv.exe and yt-dlp.exe (fetch-deps.ps1)
  2. runs the unit tests (skip with -SkipTests)
  3. publishes YTRadioMini as a self-contained, ReadyToRun win-x64 application
  4. adds mpv.exe, yt-dlp.exe, README.txt, DEPENDENCIES.txt and THIRD-PARTY-NOTICES.txt
  5. zips the folder and prints its SHA-256

  Requires the .NET 10 SDK. Works in Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipZip,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

# 1. dependencies
& (Join-Path $PSScriptRoot 'fetch-deps.ps1')
$depsDir = Join-Path $repo 'build/deps'

# 2. tests
if (-not $SkipTests) {
    Invoke-Checked 'The unit tests' { dotnet test (Join-Path $repo 'tests/YTRadioMini.Tests') -c Release --nologo }
}

# 3. publish
$csproj = Join-Path $repo 'src/YTRadioMini/YTRadioMini.csproj'
$version = ([xml](Get-Content -Raw -Path $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$version = "$version".Trim()
if (-not $version) { throw 'No <Version> found in YTRadioMini.csproj.' }

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$OutputRoot = (Resolve-Path $OutputRoot).Path
$package = Join-Path $OutputRoot 'YTRadioMini'
if (Test-Path $package) { Remove-Item -Recurse -Force $package }

Invoke-Checked 'dotnet publish' {
    dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false `
        --nologo -o $package
}

# 4. bundled programs and documents
Copy-Item (Join-Path $depsDir 'mpv.exe') $package
Copy-Item (Join-Path $depsDir 'yt-dlp.exe') $package
Copy-Item (Join-Path $repo 'docs/README.txt') $package
Copy-Item (Join-Path $repo 'docs/THIRD-PARTY-NOTICES.txt') $package

$deps = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'dependencies.json') | ConvertFrom-Json
$mpvHash = (Get-FileHash (Join-Path $package 'mpv.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$ytHash = (Get-FileHash (Join-Path $package 'yt-dlp.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
@"
YT Radio Mini $version - bundled programs
=========================================

mpv.exe
  Version   : $($deps.mpv.version)
  Source    : $($deps.mpv.url)
  Project   : $($deps.mpv.project)
  Archive   : SHA-256 $($deps.mpv.sha256)
              ($($deps.mpv.sha256Origin))
  mpv.exe   : SHA-256 $mpvHash

yt-dlp.exe
  Version   : $($deps.ytDlp.version)
  Source    : $($deps.ytDlp.url)
  Project   : $($deps.ytDlp.project)
  SHA-256   : $ytHash
              ($($deps.ytDlp.sha256Origin))

YTRadioMini.exe
  Built with the .NET SDK $((dotnet --version).Trim()); the .NET runtime is bundled (self-contained, win-x64).
"@ | Set-Content -Path (Join-Path $package 'DEPENDENCIES.txt') -Encoding UTF8

$files = Get-ChildItem -Recurse -File $package
$size = ($files | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Package folder: {0}  ({1} files, {2:N0} MB)" -f $package, $files.Count, $size)

# 5. zip
if (-not $SkipZip) {
    $zip = Join-Path $OutputRoot "YTRadioMini-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path $package -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("Zip: {0}  ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
    Write-Host "SHA-256: $hash"
}
