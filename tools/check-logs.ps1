# Checks every game log of a test run (spikes/logs/<run>/*.log) for problems:
# errors and exceptions, state divergence, adapter self-check failures, SpireParty warnings,
# and how many synced checksums each process computed (they should match for processes that
# were in the same combats the whole time). Card plays the game cancels (target already dead, no
# energy left) are logged as [ERROR] on every peer alike; they are counted apart ("canceled").
#
#   tools/check-logs.ps1 -Run smoke5            exit code 0 = clean, 1 = something to look at
#   tools/check-logs.ps1 -Run smoke5 -Ignore "Received connection failed"
param(
    [Parameter(Mandatory = $true)][string]$Run,
    [string[]]$Ignore = @(),
    [int]$Show = 5
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$runDir = Join-Path $root (Join-Path "spikes" (Join-Path "logs" $Run))
$logs = Get-ChildItem -Path $runDir -Filter "*.log" -ErrorAction Stop | Sort-Object Name
if (-not $logs) { throw "No logs in $runDir" }

$checks = [ordered]@{
    "error"      = '^\[ERROR\](?!.*was in state Canceled)'
    "exception"  = 'Exception:'
    "divergence" = 'State divergence|StateDivergence|state diverged|SpireParty] state divergence'
    "adapter"    = 'adapter .* not applied|SpireParty features disabled'
    "sp-warn"    = '^\[WARN\].*\[SpireParty\]'
}

$problems = 0
$rows = foreach ($log in $logs) {
    $lines = Get-Content $log.FullName
    $row = [ordered]@{ log = $log.BaseName }
    foreach ($k in $checks.Keys) {
        $hits = @($lines | Where-Object { $_ -match $checks[$k] } | Where-Object {
            $line = $_; -not ($Ignore | Where-Object { $line -match [regex]::Escape($_) })
        })
        $row[$k] = $hits.Count
        if ($hits.Count -gt 0) {
            if ($k -ne "sp-warn") { $problems += $hits.Count }
            Write-Host "--- $($log.BaseName): $k ($($hits.Count))" -ForegroundColor Yellow
            $hits | Select-Object -First $Show | ForEach-Object { Write-Host ("    " + $_.Substring(0, [Math]::Min(200, $_.Length))) }
        }
    }
    $sums = @($lines | Select-String -Pattern 'Generating checksum for context: .* id: (\d+)$')
    $row["checksums"] = $sums.Count
    $row["lastId"] = if ($sums) { [int]$sums[-1].Matches[0].Groups[1].Value } else { 0 }
    $row["canceled"] = @($lines | Where-Object { $_ -match "was in state Canceled" }).Count
    $row["autopilot"] = @($lines | Where-Object { $_ -match '\[SpireParty\] autopilot: ' }).Count
    $row["floors"] = @($lines | Where-Object { $_ -match 'autopilot: map vote' }).Count
    [pscustomobject]$row
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$ids = @($rows | Where-Object { $_.checksums -gt 0 } | ForEach-Object { $_.lastId } | Sort-Object -Unique)
if ($ids.Count -gt 1) { Write-Host "Checksum ids differ between processes: $($ids -join ', ') (fine if a process left or joined mid-run)" -ForegroundColor Yellow }

if ($problems -gt 0) { Write-Host "$problems problem line(s)." -ForegroundColor Red; exit 1 }
Write-Host "Clean." -ForegroundColor Green
exit 0
