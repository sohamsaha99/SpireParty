# Divergence diagnostics (M3 task 3.12). 3 players on autopilot; client c1 flips one of its checksums
# (--spireparty-test-diverge, test only), so the host finds a state divergence. Checks: both the host and c1 save a
# bundle under mods/SpireParty/diagnostics with the game log, versions, the checksum context, and (host) party.json
# without token hashes and the run save; the game's divergence popup gets the report line.
#
#   tools/scenarios/diverge.ps1
param([string]$Run = "diverge", [int]$At = 30, [string]$EnemyHp = "0.15")
. (Join-Path $PSScriptRoot "lib.ps1")
$diag = Join-Path $env:APPDATA "SlayTheSpire2\mods\SpireParty\diagnostics"

Stop-Party
Reset-Party $Run
$started = Get-Date
try {
    Start-Party $Run @{ Autopilot = $true; EnemyHp = $EnemyHp; Clients = 2; DivergeClient = 1; DivergeAt = $At }
    Assert-Log $Run "c1.log" "TEST: this process will flip its checksum"
    Wait-Host $Run "divergence diagnostics saved to" 1 900
    Wait-Host $Run "divergence popup: added the report line" 1 120
    Start-Sleep -Seconds 10
    Assert-Log $Run "c1.log" "divergence diagnostics saved to"
    Assert-Log $Run "c1.log" "divergence popup: added the report line"
    Stop-Party

    $bundles = Get-ChildItem $diag -Directory | Where-Object { $_.CreationTime -ge $started }
    Write-Host "   bundles: $($bundles.Name -join ', ')"
    if ($bundles.Count -lt 2) { throw "expected a bundle from the host and one from c1" }
    $hostBundle = $bundles | Where-Object { Test-Path (Join-Path $_.FullName "party.json") } | Select-Object -First 1
    if (-not $hostBundle) { throw "no bundle with party.json (host)" }
    foreach ($f in "divergence.txt", "info.txt", "game.log", "party.json", "run.save") {
        $p = Join-Path $hostBundle.FullName $f
        if (-not (Test-Path $p)) { throw "host bundle lacks $f" }
    }
    $pj = Get-Content (Join-Path $hostBundle.FullName "party.json") -Raw | ConvertFrom-Json
    $leaked = @($pj.seats | Where-Object { $_.tokenHash -and $_.tokenHash -ne "(removed)" })
    if ($leaked.Count -gt 0) { throw "party.json in the bundle still has token hashes" }
    if (-not (Select-String -Path (Join-Path $hostBundle.FullName "game.log") -Pattern "hosting party .$Run." -Quiet)) { throw "game.log in the host bundle is not this host's log" }
    Write-Host "   ok: host bundle complete, token hashes removed"
    Get-Content (Join-Path $hostBundle.FullName "divergence.txt") -TotalCount 4 | Out-Host
    Write-Host "PASS: diverge" -ForegroundColor Green
}
finally {
    Stop-Party
}
