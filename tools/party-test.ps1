# Starts a SpireParty test party on this PC: one host and N clients over 127.0.0.1, all without Steam.
# Processes are started through WMI so they survive the Claude desktop app restarting.
# Logs, tokens and screenshots go to spikes/logs/<run>/ (gitignored).
#
#   tools/party-test.ps1 -Run smoke5 -Clients 4 -Autopilot      host + 4 clients, all play themselves
#   tools/party-test.ps1 -Run smoke5 -Clients 4 -Manual 1       client 1 is played by a person (no autopilot)
#   tools/party-test.ps1 -Run smoke5 -Continue                   same party and seats again (rejoin with saved tokens)
#   tools/party-test.ps1 -Run fast -Clients 4 -Autopilot -EnemyHp 0.1   enemies at 10% HP on every process (short fights)
#   tools/party-test.ps1 -Run long -Clients 3 -Autopilot -KillTurn 4   host ends fights from turn 4 (console "kill all", synced)
#   tools/party-test.ps1 -Stop                                   close every game process
#
# Seats are c1..cN, profiles user://default/<1000+n>/ (test profiles only; never the Steam profile).
param(
    [string]$Run = "run",
    [int]$Clients = 4,
    [string]$Party = "",
    [switch]$Autopilot,
    [int[]]$Manual = @(),
    [switch]$HostManual,
    [switch]$NoLight,
    [switch]$Shots,
    [switch]$Continue,
    [switch]$Stop,
    [int]$KillTurn = 0,
    [string]$EnemyHp = "",
    [int]$HostTimeoutSeconds = 90
)
$ErrorActionPreference = "Stop"

if ($Stop) {
    Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host "All game processes stopped."
    return
}

$root = Split-Path -Parent $PSScriptRoot
$game = $env:STS2_GAME_DIR
if (-not $game) { $game = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2" }
$exe = Join-Path $game "SlayTheSpire2.exe"
if (-not $Party) { $Party = $Run }
$runDir = Join-Path $root (Join-Path "spikes" (Join-Path "logs" $Run))
New-Item -ItemType Directory -Force $runDir | Out-Null

if (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) {
    throw "Slay the Spire 2 is already running. Use -Stop first."
}

# Test profiles start with mods disabled and show the mods prompt once. Seed a missing profile's
# settings from an existing test profile that has mods enabled (test profiles only).
$userDir = Join-Path $env:APPDATA "SlayTheSpire2"
$seed = Join-Path $userDir (Join-Path "default" (Join-Path "1000" "settings.save"))
foreach ($id in @(1) + (1..$Clients | ForEach-Object { 1000 + $_ })) {
    $dir = Join-Path $userDir (Join-Path "default" "$id")
    $settings = Join-Path $dir "settings.save"
    if (-not (Test-Path $settings) -and (Test-Path $seed)) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        Copy-Item $seed $settings
        Write-Host "Seeded test profile default/$id"
    }
}

# A fresh run starts from a fresh log per process; -Continue keeps old logs as <name>-<n>.log.
function Get-LogPath([string]$name) {
    $path = Join-Path $runDir "$name.log"
    if ($Continue -and (Test-Path $path)) {
        $n = 2
        while (Test-Path (Join-Path $runDir "$name-r$n.log")) { $n++ }
        $path = Join-Path $runDir "$name-r$n.log"
    }
    return $path
}

function Start-Game([string]$name, [string[]]$extra, [bool]$auto) {
    $argv = @("--log-file", "`"$(Get-LogPath $name)`"", "--force-steam", "off")
    if (-not $NoLight) { $argv += "--spireparty-light" }
    if ($auto) { $argv += "--spireparty-autopilot" }
    if ($EnemyHp) { $argv += "--spireparty-test-enemyhp=$EnemyHp" }
    if ($Shots) { $argv += "--spireparty-shots=`"$(Join-Path $runDir "shots-$name")`"" }
    $argv += $extra
    $cmd = "`"$exe`" " + ($argv -join " ")
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = $game }
    if ($r.ReturnValue -ne 0) { throw "Could not start $name (WMI $($r.ReturnValue))" }
    Write-Host "$name started (pid $($r.ProcessId))"
    return [int]$r.ProcessId
}

Add-Type -Namespace SpireParty -Name Win -MemberDefinition @"
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hWnd, string text);
"@ -ErrorAction SilentlyContinue

function Set-Title([int]$procId, [string]$title) {
    for ($i = 0; $i -lt 60; $i++) {
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if (-not $p) { return }
        if ($p.MainWindowHandle -ne 0) { [SpireParty.Win]::SetWindowText($p.MainWindowHandle, $title) | Out-Null; return }
        Start-Sleep -Milliseconds 500
    }
}

$players = $Clients + 1
$hostLog = Get-LogPath "host"
$hostAuto = $Autopilot -and -not $HostManual
$hostExtra = @("--spireparty-host=$Party", "--spireparty-expect=$players")
if ($KillTurn -gt 0) { $hostExtra += "--spireparty-killturn=$KillTurn" }
$hostPid = Start-Game "host" $hostExtra $hostAuto
$pids = [ordered]@{ host = $hostPid }

# Wait until the host is listening before starting clients.
$deadline = (Get-Date).AddSeconds($HostTimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $hostLog) -and (Select-String -Path $hostLog -Pattern "hosting party" -Quiet)) { break }
    Start-Sleep -Seconds 1
}
if (-not (Select-String -Path $hostLog -Pattern "hosting party" -Quiet -ErrorAction SilentlyContinue)) {
    throw "Host did not start hosting within $HostTimeoutSeconds s; see $hostLog"
}
Set-Title $hostPid "HOST"

for ($n = 1; $n -le $Clients; $n++) {
    $seat = "c$n"
    $tokenFile = Join-Path $runDir "token-$seat.txt"
    if (-not $Continue -and (Test-Path $tokenFile)) { Remove-Item $tokenFile }
    $auto = $Autopilot -and -not ($Manual -contains $n)
    $clientPid = Start-Game $seat @("--clientId=$(1000 + $n)", "--spireparty-join=127.0.0.1,$seat,,C$n",
        "--spireparty-tokenfile=`"$tokenFile`"") $auto
    $pids[$seat] = $clientPid
    Start-Sleep -Seconds 2
}
foreach ($k in $pids.Keys) { if ($k -ne "host") { Set-Title $pids[$k] ("CLIENT " + $k.ToUpper()) } }

$pids | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $runDir "pids.json")
Write-Host "Party '$Party': host + $Clients clients. Logs in $runDir"
