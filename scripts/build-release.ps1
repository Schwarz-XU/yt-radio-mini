<#
.SYNOPSIS
  Builds the Windows x64 portable package: artifacts\YTRadioMini\ and artifacts\YTRadioMini-<version>-win-x64.zip

.DESCRIPTION
  1. downloads and verifies mpv.exe and yt-dlp.exe (fetch-deps.ps1)
  2. runs the unit tests (skip with -SkipTests)
  3. publishes YTRadioMini as a self-contained, ReadyToRun, single-file win-x64 application
  4. arranges the package folder: the exe, README.txt and LICENSE.txt at the top, mpv.exe and
     yt-dlp.exe in tools\, notices and the changelog in docs\, all licence texts in licenses\
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

# 3. publish: one self-contained single-file exe (the .NET runtime and all libraries are inside it)
$csproj = Join-Path $repo 'src/YTRadioMini/YTRadioMini.csproj'
$version = ([xml](Get-Content -Raw -Path $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$version = "$version".Trim()
if (-not $version) { throw 'No <Version> found in YTRadioMini.csproj.' }

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$OutputRoot = (Resolve-Path $OutputRoot).Path
$package = Join-Path $OutputRoot 'YTRadioMini'
$publishDir = Join-Path $OutputRoot '_publish'
foreach ($dir in $package, $publishDir) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }

Invoke-Checked 'dotnet publish' {
    dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `
        -p:DebugType=none -p:DebugSymbols=false --nologo -o $publishDir
}

# 4. assemble the package folder
#   YTRadioMini/
#     YTRadioMini.exe, README.txt, LICENSE.txt
#     tools/     mpv.exe, yt-dlp.exe
#     docs/      THIRD-PARTY-NOTICES.txt, DEPENDENCIES.txt, CHANGELOG.md
#     licenses/  yt-radio-mini/ (this program), mpv/, yt-dlp/, dotnet/, GPL-3.0.txt
$tools = Join-Path $package 'tools'
$docs = Join-Path $package 'docs'
$licenses = Join-Path $package 'licenses'
foreach ($dir in $package, $tools, $docs, $licenses) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

Copy-Item (Join-Path $publishDir 'YTRadioMini.exe') $package
Remove-Item -Recurse -Force $publishDir

Copy-Item (Join-Path $depsDir 'mpv.exe') $tools
Copy-Item (Join-Path $depsDir 'yt-dlp.exe') $tools

Copy-Item (Join-Path $repo 'docs/README.txt') $package
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $package 'LICENSE.txt')
Copy-Item (Join-Path $repo 'docs/THIRD-PARTY-NOTICES.txt') $docs
Copy-Item (Join-Path $repo 'CHANGELOG.md') $docs

$own = Join-Path $licenses 'yt-radio-mini'
New-Item -ItemType Directory -Force -Path $own | Out-Null
foreach ($name in 'LICENSE', 'COPYING', 'COPYING.LESSER') { Copy-Item (Join-Path $repo $name) $own }
Copy-Item -Recurse -Force (Join-Path $repo 'licenses/*') $licenses

$deps = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'dependencies.json') | ConvertFrom-Json
$mpvHash = (Get-FileHash (Join-Path $tools 'mpv.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$ytHash = (Get-FileHash (Join-Path $tools 'yt-dlp.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
@"
YT Radio Mini $version - bundled programs
=========================================

tools\mpv.exe
  Version   : $($deps.mpv.version)
  Source    : $($deps.mpv.url)
  Project   : $($deps.mpv.project)
  Archive   : SHA-256 $($deps.mpv.sha256)
              ($($deps.mpv.sha256Origin))
  mpv.exe   : SHA-256 $mpvHash

tools\yt-dlp.exe
  Version   : $($deps.ytDlp.version)
  Source    : $($deps.ytDlp.url)
  Project   : $($deps.ytDlp.project)
  SHA-256   : $ytHash
              ($($deps.ytDlp.sha256Origin))

YTRadioMini.exe
  Built with the .NET SDK $((dotnet --version).Trim()); the .NET runtime and all libraries are inside the exe
  (self-contained single file, win-x64).
"@ | Set-Content -Path (Join-Path $docs 'DEPENDENCIES.txt') -Encoding UTF8

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
