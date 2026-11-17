# Snapshot or compare the game's profile folders, to prove a party run wrote nothing there.
# Read-only: never changes a file.
#
#   tools/hash-profile.ps1 -Out before.txt            snapshot
#   tools/hash-profile.ps1 -Before before.txt -After after.txt
#
# Covers <user data>/steam/** and <user data>/default/** (the profile trees). SpireParty's own
# folder (<user data>/mods/SpireParty) and logs/caches are not part of the profile and are skipped.
param(
    [string]$Out,
    [string]$Before,
    [string]$After,
    [string]$UserDataDir = (Join-Path $env:APPDATA "SlayTheSpire2")
)
$ErrorActionPreference = "Stop"

if ($Before -or $After) {
    if (-not ($Before -and $After)) { throw "Use -Before <file> -After <file>" }
    # (PowerShell names are case-insensitive: don't reuse $Before/$After for the tables.)
    $old = @{}; Get-Content $Before | ForEach-Object { $p = $_ -split "`t", 3; $old[$p[2]] = $p[0] }
    $new = @{}; Get-Content $After | ForEach-Object { $p = $_ -split "`t", 3; $new[$p[2]] = $p[0] }
    $changes = 0
    foreach ($k in (@($old.Keys) + @($new.Keys) | Sort-Object -Unique)) {
        if (-not $new.ContainsKey($k)) { "REMOVED  $k"; $changes++ }
        elseif (-not $old.ContainsKey($k)) { "ADDED    $k"; $changes++ }
        elseif ($old[$k] -ne $new[$k]) { "CHANGED  $k"; $changes++ }
    }
    if ($changes -eq 0) { "No changes in the profile folders." } else { "$changes change(s)." }
    return
}

if (-not $Out) { throw "Use -Out <file> to snapshot, or -Before <file> -After <file>" }
$lines = foreach ($tree in "steam", "default") {
    $root = Join-Path $UserDataDir $tree
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -Recurse -File | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($UserDataDir.Length + 1).Replace('\', '/')
        "{0}`t{1}`t{2}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Length, $rel
    }
}
$lines | Set-Content -Encoding utf8 $Out
"Snapshot of $($lines.Count) files written to $Out"
