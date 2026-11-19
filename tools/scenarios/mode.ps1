# A party run in one game mode with a roster edit (M3 tasks 3.9 and 3.10). 5 players on autopilot:
#   1. new party in <Mode>; play <Rooms> rooms; the host does Save and Quit
#   2. host benches c2 and adds c5 (Edit roster flow, through the dev switch); Continue without c2; play; Save and Quit
#   3. host restores c2; Continue with everyone; play a few rooms
# Then the log checker and the profile hash compare.
#
#   tools/scenarios/mode.ps1 -Mode blitz
#   tools/scenarios/mode.ps1 -Mode custom -Modifiers DeadlyEvents,Vintage
param(
    [Parameter(Mandatory = $true)][ValidateSet("standard", "daily", "custom", "blitz", "dread")][string]$Mode,
    [string]$Modifiers = "",
    [string]$Run = "",
    [int]$Rooms = 4,
    [string]$AddCharacter = "ironclad",
    [string]$EnemyHp = "0.15"
)
. (Join-Path $PSScriptRoot "lib.ps1")
if (-not $Run) { $Run = "mode-$Mode" }
$common = @{ Autopilot = $true; EnemyHp = $EnemyHp }

Stop-Party
Reset-Party $Run
$before = Save-ProfileHashes $Run "before"
try {
    $o = $common + @{ Clients = 4; Mode = $Mode; SaveAfter = $Rooms }
    if ($Modifiers) { $o.Modifiers = $Modifiers }
    Start-Party $Run $o
    Assert-Log $Run "host.log" "hosting party .*$Mode mode"
    Wait-SaveAndQuit $Run
    Stop-Party

    # Some runs cannot take a new player (a modifier that replaces starting decks, e.g. a Daily with Draft): the add is
    # refused with the reason, the bench before it stays done, and the party continues without c5.
    $added = $true
    try {
        Start-Party $Run ($common + @{ Clients = 5; Continue = $true; Skip = @(2); SaveAfter = 2; Roster = "bench,c2;add,c5,$AddCharacter" })
    }
    catch {
        $refusal = Select-String -Path (Get-HostLog $Run) -Pattern "roster edit refused: add c5 .*modifier" | Select-Object -First 1
        if (-not $refusal) { throw }
        Write-Host "   ok: add refused for a modifier: $($refusal.Line)"
        $added = $false
        $script:ExtraIgnore += "roster edit refused: add c5"
        Stop-Party
        Start-Party $Run ($common + @{ Clients = 4; Continue = $true; Skip = @(2); SaveAfter = 2 })
    }
    Assert-Log $Run "host-r2.log" "roster bench c2 .* -> ok"
    if ($added) { Assert-Log $Run "host-r2.log" "roster add c5 .* -> ok" }
    Assert-Log $Run (Split-Path -Leaf (Get-HostLog $Run)) "hosting party .*$Mode mode"
    Wait-SaveAndQuit $Run
    Stop-Party

    Start-Party $Run ($common + @{ Clients = $(if ($added) { 5 } else { 4 }); Continue = $true; Roster = "restore,c2" })
    Assert-Log $Run (Split-Path -Leaf (Get-HostLog $Run)) "roster restore c2 .* -> ok"
    Assert-Log $Run (Split-Path -Leaf (Get-HostLog $Run)) "hosting party .*$Mode mode"
    Wait-Rooms $Run 2
    Stop-Party

    Assert-CleanLogs $Run
    Assert-ProfileUnchanged $Run $before
    Write-Host "PASS: $Mode" -ForegroundColor Green
}
finally {
    Stop-Party
}
