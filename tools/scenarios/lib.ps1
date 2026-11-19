# Shared helpers for the regression scenarios in tools/scenarios/ (M3 task 3.10). Dot-source it:
#   . (Join-Path $PSScriptRoot "lib.ps1")
# A scenario runs phases of tools/party-test.ps1, waits on the host log, and ends with
# tools/check-logs.ps1 and the profile hash compare. Every helper throws on failure, so a scenario
# stops at the first problem with the reason; Stop-Party is always called on the way out.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Tools = Join-Path $Root "tools"
# Lines some scenarios expect: a host doing Save and Quit leaves clients sending for a moment.
# Game log lines that change nothing (findings → M3, scenario runs): see docs/testing.md → Known pitfalls.
$script:ExtraIgnore = @("is not connected!", "Tried to add hand for player", "] Reward set with id")

function Get-RunDir([string]$Run) { Join-Path $Root (Join-Path "spikes" (Join-Path "logs" $Run)) }

# The host log of the current phase: host.log, then host-r2.log, host-r3.log, ... for -Continue phases.
function Get-HostLog([string]$Run) {
    Get-ChildItem (Get-RunDir $Run) -Filter "host*.log" | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
}

function Start-Party([string]$Run, [hashtable]$Options) {
    Write-Host "== party-test $Run $(($Options.GetEnumerator() | ForEach-Object { "-$($_.Key) $($_.Value)" }) -join ' ')"
    & (Join-Path $Tools "party-test.ps1") -Run $Run @Options | Out-Host
}

function Stop-Party {
    & (Join-Path $Tools "party-test.ps1") -Stop | Out-Null
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
    Start-Sleep -Seconds 2
}

# Waits until $Pattern appears $Count times in the current host log. Fails at once on a state divergence in any
# log of the run, or after $TimeoutSeconds.
function Wait-Host([string]$Run, [string]$Pattern, [int]$Count = 1, [int]$TimeoutSeconds = 900) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $log = Get-HostLog $Run
    Write-Host "   waiting for $Count x '$Pattern' in $(Split-Path -Leaf $log)"
    while ((Get-Date) -lt $deadline) {
        $n = @(Select-String -Path $log -Pattern $Pattern -ErrorAction SilentlyContinue).Count
        if ($n -ge $Count) { return }
        $div = Select-String -Path (Join-Path (Get-RunDir $Run) "*.log") -Pattern "State divergence" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($div) { throw "State divergence: $($div.Filename): $($div.Line)" }
        Start-Sleep -Seconds 5
    }
    throw "Timed out after $TimeoutSeconds s waiting for $Count x '$Pattern' in $log"
}

# Rooms entered since the current host log started (one "Moving to coordinate" per room).
function Wait-Rooms([string]$Run, [int]$Rooms, [int]$TimeoutSeconds = 900) {
    Wait-Host $Run "Moving to coordinate" $Rooms $TimeoutSeconds
}

function Wait-SaveAndQuit([string]$Run, [int]$TimeoutSeconds = 900) {
    Wait-Host $Run "Save and Quit after" 1 $TimeoutSeconds
    Wait-Host $Run "host session for .* ended" 1 120
}

# A line must (or must not) appear in a given log of the run.
function Assert-Log([string]$Run, [string]$LogName, [string]$Pattern, [switch]$Absent) {
    $path = Join-Path (Get-RunDir $Run) $LogName
    $hit = Select-String -Path $path -Pattern $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($Absent -and $hit) { throw "Unexpected in ${LogName}: $($hit.Line)" }
    if (-not $Absent -and -not $hit) { throw "Missing in ${LogName}: '$Pattern'" }
    Write-Host "   ok: $LogName $(if ($Absent) { 'has no' } else { 'has' }) '$Pattern'"
}

function Assert-CleanLogs([string]$Run) {
    & (Join-Path $Tools "check-logs.ps1") -Run $Run -Ignore (@("Invalid Task ID", "card holder passed is null") + $script:ExtraIgnore) | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "check-logs found problems in $Run" }
}

function Save-ProfileHashes([string]$Run, [string]$Name) {
    New-Item -ItemType Directory -Force (Get-RunDir $Run) | Out-Null
    $out = Join-Path (Get-RunDir $Run) "profile-$Name.txt"
    & (Join-Path $Tools "hash-profile.ps1") -Out $out | Out-Null
    return $out
}

function Assert-ProfileUnchanged([string]$Run, [string]$Before) {
    $after = Save-ProfileHashes $Run "after"
    $result = & (Join-Path $Tools "hash-profile.ps1") -Before $Before -After $after
    $result | Out-Host
    if (-not ($result -contains "No changes in the profile folders.")) { throw "Profile files changed during $Run" }
}

# Removes a test party left from an earlier run of the same scenario (test parties only: the folder is the run name).
function Reset-Party([string]$Run) {
    $dir = Join-Path $env:APPDATA (Join-Path "SlayTheSpire2" (Join-Path "mods" (Join-Path "SpireParty" (Join-Path "parties" $Run))))
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    $runDir = Get-RunDir $Run
    if (Test-Path $runDir) { Remove-Item -Recurse -Force $runDir }
}
