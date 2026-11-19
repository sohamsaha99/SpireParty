# A join while the run is in progress is refused with SpireParty's reason (M3 task 3.13).
# A party that had no saved run at Continue (the case that used to let a new seat through): 5 players on autopilot
# start a new run; after two rooms a new seat (c5) tries to join and is refused; party.json gains no seat.
#
#   tools/scenarios/midrun.ps1
param([string]$Run = "midrun", [string]$EnemyHp = "0.15")
. (Join-Path $PSScriptRoot "lib.ps1")

Stop-Party
Reset-Party $Run
$before = Save-ProfileHashes $Run "before"
try {
    Start-Party $Run @{ Autopilot = $true; EnemyHp = $EnemyHp; Clients = 4 }
    Wait-Rooms $Run 2
    Start-Party $Run @{ Autopilot = $true; EnemyHp = $EnemyHp; Clients = 5; ClientsOnly = $true; Only = @(5); Continue = $true }
    Wait-Host $Run "control channel: refused .* seat .c5.: run_in_progress" 1 120
    Assert-Log $Run "c5.log" "dev launch: join .* -> The party is in the middle of a run"
    $partyJson = Join-Path $env:APPDATA "SlayTheSpire2\mods\SpireParty\parties\$Run\party.json"
    if ((Get-Content $partyJson -Raw) -match '"c5"') { throw "party.json gained seat c5" }
    Write-Host "   ok: party.json has no seat c5"
    Wait-Rooms $Run 3
    Stop-Party

    Assert-CleanLogs $Run
    Assert-ProfileUnchanged $Run $before
    Write-Host "PASS: midrun" -ForegroundColor Green
}
finally {
    Stop-Party
}
