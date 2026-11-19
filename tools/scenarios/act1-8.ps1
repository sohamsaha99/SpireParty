# Eight players to the end of Act 1 (M3 task 3.10, gate step 1). Host plus seven autopilot clients, and a ninth
# client started with them that must be refused as "party is full". Waits until the party reaches Act 2.
# Close other desktop apps first: 8 games need about 6 GB of RAM.
#
#   tools/scenarios/act1-8.ps1 [-Shots]
param([string]$Run = "act1-8", [string]$EnemyHp = "0.15", [switch]$Shots, [int]$TimeoutMinutes = 75)
. (Join-Path $PSScriptRoot "lib.ps1")

Stop-Party
Reset-Party $Run
$before = Save-ProfileHashes $Run "before"
try {
    $o = @{ Autopilot = $true; EnemyHp = $EnemyHp; Clients = 8; Refused = @(8) }
    if ($Shots) { $o.Shots = $true }
    Start-Party $Run $o
    Wait-Host $Run "control channel: refused .* seat .c8.: party_full" 1 180
    Assert-Log $Run "c8.log" "dev launch: join .* -> The party is full \(8 players\)"
    Wait-Host $Run "act 1 coord" 1 ($TimeoutMinutes * 60)
    Stop-Party

    Assert-CleanLogs $Run
    Assert-ProfileUnchanged $Run $before
    Write-Host "PASS: act1-8" -ForegroundColor Green
}
finally {
    Stop-Party
}
