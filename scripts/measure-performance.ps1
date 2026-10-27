<#
.SYNOPSIS
  Measures start-up time, memory and CPU of YT Radio Mini on this computer.
  Memory and CPU are summed over the whole process tree (GUI + mpv + any yt-dlp that is running).

.PARAMETER ExePath
  The program to measure. Default: the package built by build-release.ps1.

.PARAMETER Url
  Optional YouTube link. When given, the script pastes it into the running program (it uses the
  clipboard and SendKeys, so do not touch the keyboard or the clipboard while it runs), waits for
  mpv to appear and then measures playback as well. Without it only the idle state is measured.

.PARAMETER IdleSeconds / PlaySeconds / SampleSeconds
  Length of the idle and the playback measurement, and the time between two samples.

.PARAMETER ReportPath
  Optional file to which the result (Markdown) is also written.

.NOTES
  "Memory" is the private working set, the number Task Manager shows in its Memory column.
  Working set (which includes pages shared with other processes) is reported as well.
  CPU is the share of the whole machine (all logical processors) used by the process tree.
  The numbers describe this computer at this moment; they are not a guarantee.
#>
[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '../artifacts/YTRadioMini/YTRadioMini.exe'),
    [string]$Url = '',
    [int]$IdleSeconds = 20,
    [int]$PlaySeconds = 60,
    [int]$SampleSeconds = 2,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath. Run scripts/build-release.ps1 first, or pass -ExePath." }
$ExePath = (Resolve-Path $ExePath).Path
$cores = [Environment]::ProcessorCount

# ---- helpers -------------------------------------------------------------------------------

function Get-DescendantIds([int]$RootId) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
    $found = New-Object System.Collections.Generic.List[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootId)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        $found.Add($current)
        foreach ($child in ($all | Where-Object { $_.ParentProcessId -eq $current })) { $queue.Enqueue([int]$child.ProcessId) }
    }
    return $found
}

# One sample of the whole tree: per-process private working set, working set and CPU seconds.
function Get-TreeSample([int]$RootId) {
    $rows = @()
    foreach ($id in (Get-DescendantIds $RootId)) {
        $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        $perf = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$id" -ErrorAction SilentlyContinue
        $private = if ($perf) { [double]$perf.WorkingSetPrivate } else { 0 }
        $rows += [pscustomobject]@{
            Id = $id; Name = $proc.ProcessName
            PrivateMB = $private / 1MB
            WorkingSetMB = $proc.WorkingSet64 / 1MB
            CpuSeconds = $proc.TotalProcessorTime.TotalSeconds
        }
    }
    return $rows
}

# Samples for $Seconds and returns a summary (average and peak of the tree totals, plus the average per program).
function Measure-Phase([int]$RootId, [int]$Seconds) {
    $totals = @()
    $perName = @{}
    $previous = $null
    $previousAt = $null
    $end = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $end) {
        $rows = @(Get-TreeSample $RootId)
        $now = Get-Date
        $cpuPercent = $null
        if ($previous) {
            $delta = 0.0
            foreach ($r in $rows) {
                $before = $previous | Where-Object { $_.Id -eq $r.Id } | Select-Object -First 1
                if ($before) { $delta += [Math]::Max(0.0, $r.CpuSeconds - $before.CpuSeconds) }
            }
            $cpuPercent = 100.0 * $delta / (($now - $previousAt).TotalSeconds * $cores)
        }
        $totals += [pscustomobject]@{
            PrivateMB = ($rows | Measure-Object PrivateMB -Sum).Sum
            WorkingSetMB = ($rows | Measure-Object WorkingSetMB -Sum).Sum
            Cpu = $cpuPercent
        }
        foreach ($g in ($rows | Group-Object Name)) {
            if (-not $perName.ContainsKey($g.Name)) { $perName[$g.Name] = New-Object System.Collections.Generic.List[double] }
            $perName[$g.Name].Add(($g.Group | Measure-Object PrivateMB -Sum).Sum)
        }
        $previous = $rows
        $previousAt = $now
        Start-Sleep -Seconds $SampleSeconds
    }
    $cpuValues = @($totals | Where-Object { $null -ne $_.Cpu } | ForEach-Object { $_.Cpu })
    return [pscustomobject]@{
        Samples = $totals.Count
        PrivateAvg = ($totals | Measure-Object PrivateMB -Average).Average
        PrivateMax = ($totals | Measure-Object PrivateMB -Maximum).Maximum
        WorkingSetAvg = ($totals | Measure-Object WorkingSetMB -Average).Average
        WorkingSetMax = ($totals | Measure-Object WorkingSetMB -Maximum).Maximum
        CpuAvg = if ($cpuValues.Count) { ($cpuValues | Measure-Object -Average).Average } else { 0 }
        CpuMax = if ($cpuValues.Count) { ($cpuValues | Measure-Object -Maximum).Maximum } else { 0 }
        PerProgram = $perName.GetEnumerator() | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ Name = $_.Key; AvgPrivateMB = ($_.Value | Measure-Object -Average).Average }
        }
    }
}

function Format-Phase([string]$Title, $m) {
    $lines = @()
    $lines += "### $Title"
    $lines += ''
    $lines += '| Metric | Average | Peak |'
    $lines += '|---|---:|---:|'
    $lines += ('| Memory, private working set, whole process tree (MB) | {0:N1} | {1:N1} |' -f $m.PrivateAvg, $m.PrivateMax)
    $lines += ('| Working set, whole process tree (MB) | {0:N1} | {1:N1} |' -f $m.WorkingSetAvg, $m.WorkingSetMax)
    $lines += ('| CPU, share of the whole machine (%) | {0:N2} | {1:N2} |' -f $m.CpuAvg, $m.CpuMax)
    $lines += ''
    $lines += 'Average private working set per program:'
    $lines += ''
    foreach ($p in $m.PerProgram) { $lines += ('- {0}: {1:N1} MB' -f $p.Name, $p.AvgPrivateMB) }
    $lines += ''
    return $lines
}

# ---- run -------------------------------------------------------------------------------------

$report = @()
$report += '# YT Radio Mini performance measurement'
$report += ''
$report += ('- Date: {0:yyyy-MM-dd HH:mm}' -f (Get-Date))
$report += "- Computer: $([Environment]::OSVersion.VersionString), $cores logical processors"
$report += "- Program: $ExePath"
$report += ''

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $ExePath -PassThru
$startupMs = $null
while ($stopwatch.Elapsed.TotalSeconds -lt 20) {
    $process.Refresh()
    if ($process.HasExited) { throw 'The program exited during start-up (is another copy already running?).' }
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $startupMs = $stopwatch.ElapsedMilliseconds; break }
    Start-Sleep -Milliseconds 10
}
if ($null -eq $startupMs) { throw 'No window appeared within 20 seconds.' }
$report += "- Start-up (process start to visible window): $startupMs ms"
$report += ''

try {
    Start-Sleep -Seconds 3   # let start-up work settle
    Write-Host "Measuring idle for $IdleSeconds s ..."
    $report += Format-Phase "Idle (nothing playing)" (Measure-Phase $process.Id $IdleSeconds)

    if ($Url) {
        Write-Host 'Pasting the link and starting playback ...'
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class Win { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }' -ErrorAction SilentlyContinue
        Set-Clipboard -Value $Url
        [void][Win]::SetForegroundWindow($process.MainWindowHandle)
        Start-Sleep -Milliseconds 500
        [System.Windows.Forms.SendKeys]::SendWait('^v')
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

        $deadline = (Get-Date).AddSeconds(60)
        $mpvSeen = $false
        while ((Get-Date) -lt $deadline) {
            if (@(Get-DescendantIds $process.Id | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_.ProcessName -eq 'mpv' }).Count -gt 0) { $mpvSeen = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $mpvSeen) {
            $report += '**mpv did not start within 60 s; playback was not measured.**'
            $report += ''
        }
        else {
            Start-Sleep -Seconds 10   # let the stream start and yt-dlp finish
            Write-Host "Measuring playback for $PlaySeconds s ..."
            $report += Format-Phase "Playing ($Url)" (Measure-Phase $process.Id $PlaySeconds)
        }
    }
}
finally {
    # Close the window like a user would, then check that nothing is left behind.
    $childIds = @(Get-DescendantIds $process.Id | Where-Object { $_ -ne $process.Id })
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(10000)) { $process.Kill() }
    Start-Sleep -Seconds 2
    $left = @($childIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_ })
    if ($left.Count -eq 0) { $report += '- After closing the window no mpv or yt-dlp process was left running.' }
    else {
        $report += ('- **Left running after closing: {0}**' -f (($left | ForEach-Object { "$($_.ProcessName) ($($_.Id))" }) -join ', '))
        $left | Stop-Process -Force
    }
}

$report | ForEach-Object { Write-Host $_ }
if ($ReportPath) { $report | Set-Content -Path $ReportPath -Encoding UTF8; Write-Host "Report written to $ReportPath" }
