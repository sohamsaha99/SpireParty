# M0 spike helper: start a host and one client on this machine, no Steam, over ENet 127.0.0.1:33771.
# Saves go to user://default/1/ (host) and user://default/1000/ (client); Steam profiles are untouched.
# Usage:
#   spikes/two-player.ps1            new run (host_standard + join)
#   spikes/two-player.ps1 -Load      load the host's co-op save (load + join)
#   spikes/two-player.ps1 -HostOnly  only the host (with -Load: load the save without the client)
param([switch]$Load, [switch]$HostOnly)
$ErrorActionPreference = "Stop"

$game = $env:STS2_GAME_DIR
if (-not $game) { $game = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2" }
$exe = Join-Path $game "SlayTheSpire2.exe"
$logDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

$hostMode = if ($Load) { "load" } else { "host_standard" }
Start-Process $exe -WorkingDirectory $game -ArgumentList @(
    "--log-file", (Join-Path $logDir "$stamp-host.log"),
    "--force-steam", "off", "--fastmp", $hostMode) | Out-Null
Write-Host "Host started ($hostMode). Log: $logDir\$stamp-host.log"

if (-not $HostOnly) {
    Start-Sleep -Seconds 20
    Start-Process $exe -WorkingDirectory $game -ArgumentList @(
        "--log-file", (Join-Path $logDir "$stamp-client.log"),
        "--force-steam", "off", "--fastmp", "join", "--clientId=1000") | Out-Null
    Write-Host "Client started (id 1000). Log: $logDir\$stamp-client.log"
}
