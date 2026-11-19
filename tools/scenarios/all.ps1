# Runs regression scenarios one after another and prints one result line each (PASS, or the first failure).
# Each scenario's full output goes to spikes/logs/scenario-<name>.txt.
#
#   tools/scenarios/all.ps1                         every feature scenario (5 players), not act1-8
#   tools/scenarios/all.ps1 -Only dread,roster      a subset
#   tools/scenarios/all.ps1 -Gate                   also act1-8 (8 players; close other apps first)
param([string[]]$Only = @(), [switch]$Gate)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $here)

$all = [ordered]@{
    standard = { & (Join-Path $here "mode.ps1") -Mode standard }
    daily    = { & (Join-Path $here "mode.ps1") -Mode daily }
    custom   = { & (Join-Path $here "mode.ps1") -Mode custom -Modifiers "DeadlyEvents,Vintage" }
    blitz    = { & (Join-Path $here "mode.ps1") -Mode blitz }
    dread    = { & (Join-Path $here "mode.ps1") -Mode dread }
    roster   = { & (Join-Path $here "roster.ps1") }
    midrun   = { & (Join-Path $here "midrun.ps1") }
    diverge  = { & (Join-Path $here "diverge.ps1") }
}
if ($Gate) { $all["act1-8"] = { & (Join-Path $here "act1-8.ps1") -Shots } }

$failed = 0
foreach ($name in $all.Keys) {
    if ($Only.Count -gt 0 -and -not ($Only -contains $name)) { continue }
    $out = Join-Path $root (Join-Path "spikes" (Join-Path "logs" "scenario-$name.txt"))
    $start = Get-Date
    try {
        & $all[$name] *>&1 | Out-File -Encoding utf8 $out
        $line = "PASS"
    }
    catch {
        "$_" | Out-File -Append -Encoding utf8 $out
        $line = "FAIL: $_"
        $failed++
    }
    "{0,-9} {1,4:N0} min  {2}" -f $name, ((Get-Date) - $start).TotalMinutes, $line
}
if ($failed -gt 0) { exit 1 }
