<#
.SYNOPSIS
  Downloads mpv and yt-dlp as pinned in dependencies.json, verifies their SHA-256 hashes
  and leaves mpv.exe and yt-dlp.exe in build\deps.

.NOTES
  Works in Windows PowerShell 5.1 and PowerShell 7. Extracting the mpv archive (.7z) needs
  7-Zip (7z in PATH or in "C:\Program Files\7-Zip") or the tar.exe that ships with Windows 10/11.
#>
[CmdletBinding()]
param(
    [string]$CacheDir = (Join-Path $PSScriptRoot '../build/deps'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow with the progress bar on 5.1
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$deps = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'dependencies.json') | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null
$CacheDir = (Resolve-Path $CacheDir).Path

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Downloads $Url to $Path unless a file with the expected hash is already there.
function Get-VerifiedFile([string]$Url, [string]$ExpectedSha256, [string]$Path) {
    $expected = $ExpectedSha256.ToLowerInvariant()
    if ((Test-Path $Path) -and -not $Force -and ((Get-Sha256 $Path) -eq $expected)) {
        Write-Host "  cached  $Path"
        return
    }
    Write-Host "  download $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
    $actual = Get-Sha256 $Path
    if ($actual -ne $expected) {
        Remove-Item -Force $Path
        throw "SHA-256 mismatch for $Url`n  expected $expected`n  actual   $actual`nThe file was deleted. Check dependencies.json."
    }
    Write-Host "  verified $actual"
}

function Expand-FromArchive([string]$Archive, [string]$Member, [string]$Destination) {
    $temp = Join-Path $CacheDir '_extract'
    if (Test-Path $temp) { Remove-Item -Recurse -Force $temp }
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    $sevenZip = $null
    foreach ($candidate in '7z', '7za', '7zz') {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { $sevenZip = $cmd.Source; break }
    }
    if (-not $sevenZip -and $env:ProgramFiles) {
        $default = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
        if (Test-Path $default) { $sevenZip = $default }
    }

    if ($sevenZip) {
        & $sevenZip x $Archive "-o$temp" $Member -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7-Zip failed to extract $Member (exit code $LASTEXITCODE)." }
    }
    else {
        $tar = Get-Command 'tar', 'bsdtar' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $tar) { throw 'Cannot extract the .7z archive: install 7-Zip (https://www.7-zip.org/) or use a Windows version that includes tar.exe.' }
        & $tar.Source -xf $Archive -C $temp $Member
        if ($LASTEXITCODE -ne 0) { throw "tar failed to extract $Member (exit code $LASTEXITCODE). Install 7-Zip and try again." }
    }

    $extracted = Join-Path $temp $Member
    if (-not (Test-Path $extracted)) { throw "$Member was not found in $Archive." }
    Move-Item -Force -Path $extracted -Destination $Destination
    Remove-Item -Recurse -Force $temp
}

Write-Host "yt-dlp $($deps.ytDlp.version)"
$ytDlpPath = Join-Path $CacheDir $deps.ytDlp.file
Get-VerifiedFile $deps.ytDlp.url $deps.ytDlp.sha256 $ytDlpPath

Write-Host "mpv $($deps.mpv.version)"
$archivePath = Join-Path $CacheDir $deps.mpv.archiveFile
$mpvPath = Join-Path $CacheDir $deps.mpv.extract
Get-VerifiedFile $deps.mpv.url $deps.mpv.sha256 $archivePath
if ($Force -or -not (Test-Path $mpvPath)) {
    Write-Host "  extract  $($deps.mpv.extract)"
    Expand-FromArchive $archivePath $deps.mpv.extract $mpvPath
}

Write-Host ''
Write-Host "Ready in ${CacheDir}:"
Get-ChildItem -Path $CacheDir -File -Filter '*.exe' | ForEach-Object { '  {0,-12} {1,10:N1} MB' -f $_.Name, ($_.Length / 1MB) }
