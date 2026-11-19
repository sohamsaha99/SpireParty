# The M2 roster flows, automated (M3 task 3.10). Standard mode, 5 players on autopilot:
#   1. new party; play; Save and Quit
#   2. bench c4 and reset c3's token; Continue: c3 claims its seat again with a new token, c4 is refused as benched
#   3. restore c4 and add c5; Continue with everyone
#   4. restore the newest backup (undoes "add c5"); Continue: c5 is refused, the rest play on
# Then the log checker and the profile hash compare.
#
#   tools/scenarios/roster.ps1
param([string]$Run = "roster", [string]$EnemyHp = "0.15")
. (Join-Path $PSScriptRoot "lib.ps1")
$common = @{ Autopilot = $true; EnemyHp = $EnemyHp }

Stop-Party
Reset-Party $Run
$before = Save-ProfileHashes $Run "before"
try {
    Start-Party $Run ($common + @{ Clients = 4; SaveAfter = 2 })
    Wait-SaveAndQuit $Run
    Stop-Party

    Start-Party $Run ($common + @{ Clients = 4; Continue = $true; Refused = @(4); SaveAfter = 2; Roster = "bench,c4;reset,c3" })
    Assert-Log $Run "host-r2.log" "roster bench c4 .* -> ok"
    Assert-Log $Run "host-r2.log" "roster reset c3 .* -> ok"
    Wait-Host $Run "open seat .c3. .* claimed with a new token"
    Assert-Log $Run "c4-r2.log" "dev launch: join .* -> .*benched"
    Wait-SaveAndQuit $Run
    Stop-Party

    Start-Party $Run ($common + @{ Clients = 5; Continue = $true; SaveAfter = 2; Roster = "restore,c4;add,c5,silent" })
    Assert-Log $Run "host-r3.log" "roster restore c4 .* -> ok"
    Assert-Log $Run "host-r3.log" "roster add c5 .* -> ok"
    Wait-SaveAndQuit $Run
    Stop-Party

    Start-Party $Run ($common + @{ Clients = 5; Continue = $true; Refused = @(5); Roster = "backup,latest" })
    Assert-Log $Run "host-r4.log" "roster backup latest .* -> ok"
    Wait-Rooms $Run 2
    Assert-Log $Run "c5-r2.log" "dev launch: join .* -> .*(not taking new players|not part of this party)"
    Stop-Party

    Assert-CleanLogs $Run
    Assert-ProfileUnchanged $Run $before
    Write-Host "PASS: roster" -ForegroundColor Green
}
finally {
    Stop-Party
}
