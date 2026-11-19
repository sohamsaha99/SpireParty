# Runs the checklist for a new Slay the Spire 2 build (docs/testing.md -> After a game update).
# Prints one line per step (PASS, REVIEW, FAIL or SKIP) and a summary, then what to record in
# docs/game-version.md and docs/findings.md. Full report: spikes/logs/check-update-<build>.txt (gitignored).
#
#   tools/check-update.ps1                 steps 1-4 (about 5 minutes the first time on a build: decompile)
#   tools/check-update.ps1 -Full           also every feature scenario (tools/scenarios/all.ps1, about 45 minutes)
#   tools/check-update.ps1 -Full -Gate     also act1-8 (8 players; close other apps first)
#   -Previous <dir>      compare with this decompiled folder (default: the newest other build in decompiled/)
#   -Decompiled <dir>    where decompiled builds live (default: decompiled/ of the main checkout, gitignored)
#   -Redecompile         decompile again even if decompiled/<build>/ exists
#   -SkipLaunch          steps 1-3 only (no deploy, no game start)
#
# Steps:
#   1  read release_info.json, sts2.runtimeconfig.json, the sts2.dll hash and the Steam build; compare with docs/game-version.md
#   2  ilspycmd (pinned 9.1.0.7988) into decompiled/<version>-<commit>/; diff only the game types the adapters in
#      src/SpireParty/GameAdapters/ patch or read, member by member, against the previous decompiled build
#   3  dotnet build -c Debug (compile errors = API breaks) and dotnet test (includes the adapter IL tests)
#   4  tools/deploy.ps1, one light host (party-test.ps1 -Run upd -Clients 0), the startup self-check lines, stop
#   5  (-Full) tools/scenarios/all.ps1 [-Gate]
# Never commits anything; decompiled code stays under the gitignored decompiled/ (CLAUDE.md hard rule 2).
param(
    [switch]$Full,
    [switch]$Gate,
    [switch]$SkipLaunch,
    [switch]$Redecompile,
    [string]$Previous = "",
    [string]$Decompiled = "",
    [string]$Run = "upd",
    [int]$LaunchTimeoutSeconds = 120
)
$ErrorActionPreference = "Stop"
$IlSpyVersion = "9.1.0.7988"

$root = Split-Path -Parent $PSScriptRoot
$tools = $PSScriptRoot
$proj = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" "SpireParty.csproj"))
$adapterDir = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" "GameAdapters"))
$logDir = Join-Path $root (Join-Path "spikes" "logs")
New-Item -ItemType Directory -Force $logDir | Out-Null

$results = New-Object System.Collections.Generic.List[object]
$report = New-Object System.Collections.Generic.List[string]
function Add-Result([string]$step, [string]$status, [string]$text) {
    $results.Add([pscustomobject]@{ Step = $step; Status = $status; Text = $text })
    $color = @{ PASS = "Green"; FAIL = "Red"; REVIEW = "Yellow"; SKIP = "DarkGray" }[$status]
    Write-Host ("{0,-6} {1,-10} {2}" -f $status, $step, $text) -ForegroundColor $color
    $report.Add(("{0,-6} {1,-10} {2}" -f $status, $step, $text))
}
function Write-Detail([string]$line) { Write-Host "         $line"; $report.Add("         $line") }

if (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) {
    throw "Slay the Spire 2 is running. Close the game (or tools/party-test.ps1 -Stop) first."
}

# Paths come from MSBuild so the per-machine override (Directory.Build.props.user, STS2_GAME_DIR) applies.
$gameDir = (dotnet msbuild $proj -nologo -getProperty:Sts2GameDir).Trim()
$dataDir = (dotnet msbuild $proj -nologo -getProperty:Sts2DataDir).Trim()
if (-not $gameDir -or -not (Test-Path $dataDir)) { throw "Game data folder not found: '$dataDir'. Set STS2_GAME_DIR." }

# ---------------------------------------------------------------- 1. build identity
$step = "1 build"
$releaseFile = Join-Path $gameDir "release_info.json"
$runtimeFile = Join-Path $dataDir "sts2.runtimeconfig.json"
$dll = Join-Path $dataDir "sts2.dll"
$release = Get-Content $releaseFile -Raw | ConvertFrom-Json
$runtime = Get-Content $runtimeFile -Raw | ConvertFrom-Json
$fw = @($runtime.runtimeOptions.framework) + @($runtime.runtimeOptions.frameworks) | Where-Object { $_ } | Select-Object -First 1
$cur = [ordered]@{
    version  = "$($release.version)"
    commit   = "$($release.commit)"
    date     = "$($release.date)"
    branch   = "$($release.branch)"
    asmhash  = "$($release.main_assembly_hash)"
    runtime  = "$($fw.name) $($fw.version), tfm $($runtime.runtimeOptions.tfm)"
    size     = (Get-Item $dll).Length
    sha      = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    steam    = ""
    betakey  = ""
}
# Steam's app manifest (two folders up from the install) has the build id and beta branch.
$manifest = Join-Path (Split-Path -Parent (Split-Path -Parent $gameDir)) "appmanifest_2868840.acf"
if (Test-Path $manifest) {
    $acf = Get-Content $manifest -Raw
    if ($acf -match '"buildid"\s+"(\d+)"') { $cur.steam = $Matches[1] }
    if ($acf -match '"BetaKey"\s+"([^"]*)"') { $cur.betakey = $Matches[1] }
}
$build = "$($cur.version)-$($cur.commit)" -replace '[^A-Za-z0-9._-]', '_'

# What docs/game-version.md records.
$doc = Get-Content (Join-Path $root (Join-Path "docs" "game-version.md")) -Raw
function Get-DocField([string]$label) {
    if ($doc -match ("\|\s*" + [regex]::Escape($label) + "\s*\|\s*([^|\r\n]+?)\s*\|")) { return $Matches[1].Trim('` ') }
    return ""
}
$was = [ordered]@{
    version = Get-DocField '`release_info.json` version'
    commit  = Get-DocField '`release_info.json` commit'
    asmhash = Get-DocField '`release_info.json` main_assembly_hash'
    steam   = Get-DocField 'Steam build ID'
    runtime = Get-DocField '.NET runtime (`sts2.runtimeconfig.json`)'
    size    = (Get-DocField '`sts2.dll` size (Windows)') -replace '[^0-9]', ''
    sha     = if ($doc -match 'SHA-256 starts ([0-9a-f]+)') { $Matches[1] } else { "" }
}
$diffs = @()
foreach ($k in @("version", "commit", "asmhash", "steam")) {
    if ($was[$k] -and $cur[$k] -and $was[$k] -ne $cur[$k]) { $diffs += "$k $($was[$k]) -> $($cur[$k])" }
}
if ($was.runtime -and -not ($was.runtime -like "*$($fw.version)*")) { $diffs += "runtime '$($was.runtime)' -> '$($cur.runtime)'" }
# The size and hash in the doc are the Windows dll's; skip them on another OS's data folder.
if ($dataDir -like "*windows*") {
    if ($was.size -and "$($cur.size)" -ne $was.size) { $diffs += "sts2.dll size $($was.size) -> $($cur.size)" }
    if ($was.sha -and -not $cur.sha.StartsWith($was.sha)) { $diffs += "sts2.dll sha256 $($was.sha)... -> $($cur.sha.Substring(0, 16))..." }
}
$buildChanged = $diffs.Count -gt 0
if ($buildChanged) {
    Add-Result $step "REVIEW" "build CHANGED since docs/game-version.md: $($diffs -join '; ')"
} else {
    Add-Result $step "PASS" "build unchanged: $($cur.version) ($($cur.commit)), Steam $($cur.steam) $($cur.betakey), sha256 $($cur.sha.Substring(0, 16))"
}

# ---------------------------------------------------------------- 2. decompile and diff adapter types
$step = "2 diff"
if (-not $Decompiled) {
    # decompiled/ is gitignored and lives in the main checkout (worktrees have none): find it through git.
    $common = (git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null)
    $mainRoot = if ($LASTEXITCODE -eq 0 -and $common) { Split-Path -Parent $common.Trim() } else { $root }
    $Decompiled = Join-Path $mainRoot "decompiled"
}
New-Item -ItemType Directory -Force $Decompiled | Out-Null
$newDir = Join-Path $Decompiled $build
$marker = "spireparty-build.txt"   # written next to each decompile; says which build it is

$decompileOk = $true
if ($Redecompile -and (Test-Path $newDir)) { Remove-Item -Recurse -Force $newDir }
if (-not (Test-Path (Join-Path $newDir $marker))) {
    $ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
    $ver = if ($ilspy) { (& ilspycmd --version 2>$null | Out-String) } else { "" }
    if (-not $ilspy -or $ver -notmatch [regex]::Escape($IlSpyVersion)) {
        Write-Host "Installing ilspycmd $IlSpyVersion ..."
        if ($ilspy) { dotnet tool uninstall -g ilspycmd | Out-Null }
        dotnet tool install -g ilspycmd --version $IlSpyVersion | Out-Null
    }
    if (Test-Path $newDir) { Remove-Item -Recurse -Force $newDir }
    Write-Host "Decompiling sts2.dll into $newDir (a few minutes) ..."
    $ilLog = Join-Path $logDir "check-update-ilspy.txt"
    & ilspycmd -p -o $newDir $dll *> $ilLog
    if ($LASTEXITCODE -ne 0) { $decompileOk = $false }
    else {
        Set-Content -Encoding utf8 (Join-Path $newDir $marker) "$($cur.version) $($cur.commit) sha256 $($cur.sha) ilspycmd $IlSpyVersion $(Get-Date -Format s)"
    }
}

# The previous build: -Previous, else the newest other folder with a marker, else the original decompiled/sts2/.
if (-not $Previous) {
    $prev = Get-ChildItem $Decompiled -Directory | Where-Object {
        $_.FullName -ne $newDir -and (Test-Path (Join-Path $_.FullName $marker))
    } | Sort-Object { (Get-Item (Join-Path $_.FullName $marker)).LastWriteTime } | Select-Object -Last 1
    if ($prev) { $Previous = $prev.FullName }
    elseif (Test-Path (Join-Path $Decompiled "sts2")) { $Previous = Join-Path $Decompiled "sts2" }
}

# Splits a decompiled file into members at the type's first indentation level. Key = the signature line (so an
# overload or a changed parameter list shows as removed + added), value = the member's text without doc comments.
function Get-Members([string]$path) {
    $members = [ordered]@{}
    if (-not (Test-Path $path)) { return $null }
    $key = $null; $pending = @(); $body = New-Object System.Text.StringBuilder
    $inType = $false
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if (-not $inType) {
            if ($line -match '^\S.*\b(class|struct|interface|enum|record)\b') { $inType = $true }
            continue
        }
        if ($line -match '^\t///' -or $line -match '^\t\t+///') { continue }
        if ($line -match '^\t(\S.*)$' -and $line -notmatch '^\t[{}]') {
            $text = $Matches[1]
            if ($text.StartsWith("[")) { $pending += $text; continue }
            if ($null -ne $key) { $members[$key] = $body.ToString() }
            $key = $text.Trim(); $n = 2
            while ($members.Contains($key)) { $key = "$($text.Trim()) #$n"; $n++ }
            $body = New-Object System.Text.StringBuilder
            foreach ($a in $pending) { [void]$body.AppendLine($a) }
            $pending = @()
            [void]$body.AppendLine($line)
            continue
        }
        if ($null -ne $key) { [void]$body.AppendLine($line) }
    }
    if ($null -ne $key) { $members[$key] = $body.ToString() }
    return $members
}
function Get-MemberName([string]$sig) {
    $s = $sig -replace '\s*#\d+$', ''
    if ($s -match '([\w<>\[\],.?]+|this\[)\s*\(' ) { return ($Matches[1] -replace '<.*$', '') }   # method, ctor
    if ($s -match '(\w+)\s*(\{|=>|=|;)') { return $Matches[1] }                                  # property, field, event
    if ($s -match '\b(class|struct|interface|enum|record)\s+(\w+)') { return $Matches[2] }       # nested type
    return $s
}
# Godot's source generator rewrites these whenever any method, property or signal is added; not a real change.
$godotGlue = @("MethodName", "PropertyName", "SignalName", "GetGodotMethodList", "InvokeGodotClassMethod",
    "HasGodotClassMethod", "SetGodotClassPropertyValue", "GetGodotClassPropertyValue", "GetGodotPropertyList",
    "SaveGodotObjectData", "RestoreGodotObjectData", "GetGodotSignalList", "RaiseGodotClassSignalCallbacks", "HasGodotClassSignal")

$affected = [ordered]@{}   # adapter -> list of lines
$likely = @()              # adapters where a member they name changed or a type is gone
if (-not $decompileOk) {
    Add-Result $step "FAIL" "ilspycmd failed; see $(Join-Path $logDir 'check-update-ilspy.txt')"
} elseif (-not $Previous -or -not (Test-Path $Previous)) {
    Add-Result $step "SKIP" "decompiled into $newDir; no previous decompiled build to compare with"
} elseif ((Resolve-Path $Previous).Path -eq (Resolve-Path $newDir).Path) {
    Add-Result $step "SKIP" "previous and new decompiled folders are the same ($newDir)"
} else {
    # Index of game types (file name = type name) in both trees, by namespace folder.
    function Get-TypeIndex([string]$dir) {
        $idx = @{}
        Get-ChildItem $dir -Recurse -Filter "*.cs" | ForEach-Object {
            $idx["$($_.Directory.Name)/$($_.BaseName)"] = $_.FullName
        }
        return $idx
    }
    $newIdx = Get-TypeIndex $newDir
    $oldIdx = Get-TypeIndex $Previous
    $typesSeen = @{}
    foreach ($file in Get-ChildItem $adapterDir -Filter "*Adapter.cs" | Sort-Object Name) {
        $src = Get-Content $file.FullName -Raw
        $adapter = if ($src -match 'Name\s*=>\s*"([^"]+)"') { $Matches[1] } else { continue }   # GameAdapter.cs itself has none
        # Game namespaces the adapter imports (logging is used everywhere and patched nowhere), plus fully named types.
        $namespaces = [regex]::Matches($src, '(?m)^using\s+(MegaCrit\.[\w.]+);') | ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -ne "MegaCrit.Sts2.Core.Logging" }
        $idents = [regex]::Matches(($src -replace '(?m)^using .*$', ''), '\b[A-Z_]\w*\b') | ForEach-Object { $_.Value } | Sort-Object -Unique
        $types = @()
        foreach ($ns in $namespaces) {
            foreach ($id in $idents) {
                $k = "$ns/$id"
                if ($newIdx.ContainsKey($k) -or $oldIdx.ContainsKey($k)) { $types += $k }
            }
        }
        foreach ($m in [regex]::Matches($src, '\b(MegaCrit\.Sts2[\w.]*)\.(\w+)\b')) {
            $k = "$($m.Groups[1].Value)/$($m.Groups[2].Value)"
            if ($newIdx.ContainsKey($k) -or $oldIdx.ContainsKey($k)) { $types += $k }
        }
        $lines = @()
        $isLikely = $false
        foreach ($k in ($types | Sort-Object -Unique)) {
            $typesSeen[$k] = $true
            $typeName = $k.Split("/")[1]
            if (-not $newIdx.ContainsKey($k)) { $lines += "$typeName GONE from $($k.Split('/')[0])"; $isLikely = $true; continue }
            if (-not $oldIdx.ContainsKey($k)) { $lines += "$typeName new in this build (not in the previous one)"; continue }
            $a = [IO.File]::ReadAllText($oldIdx[$k]); $b = [IO.File]::ReadAllText($newIdx[$k])
            if ($a -eq $b) { continue }
            $old = Get-Members $oldIdx[$k]; $new = Get-Members $newIdx[$k]
            $changes = @(); $glue = 0
            foreach ($sig in $old.Keys) {
                $name = Get-MemberName $sig
                $kind = if (-not $new.Contains($sig)) { "removed" } elseif ($new[$sig] -ne $old[$sig]) { "changed" } else { $null }
                if (-not $kind) { continue }
                if ($godotGlue -contains $name) { $glue++; continue }
                $named = $src -match ('\b' + [regex]::Escape($name) + '\b')
                if ($named) { $isLikely = $true }
                $changes += "{0} {1}{2}" -f $kind, $(if ($named) { "* " } else { "" }), ($sig.Substring(0, [Math]::Min(110, $sig.Length)))
            }
            foreach ($sig in $new.Keys) {
                if ($old.Contains($sig)) { continue }
                $name = Get-MemberName $sig
                if ($godotGlue -contains $name) { $glue++; continue }
                $changes += "added   $($sig.Substring(0, [Math]::Min(110, $sig.Length)))"
            }
            if ($changes.Count -eq 0 -and $glue -gt 0) { $changes += "only Godot generated glue changed" }
            if ($changes.Count -eq 0) { $changes += "text changed outside members (usings, attributes, type header)" }
            $lines += "${typeName}:"
            $lines += $changes | ForEach-Object { "  $_" }
        }
        if ($lines.Count -gt 0) {
            $affected[$adapter] = $lines
            if ($isLikely) { $likely += $adapter }
        }
    }
    $prevName = Split-Path -Leaf $Previous
    if ($affected.Count -eq 0) {
        Add-Result $step "PASS" "$($typesSeen.Count) game types used by adapters: none changed ($prevName -> $build)"
    } else {
        Add-Result $step "REVIEW" "$($affected.Count) adapter(s) use changed types ($prevName -> $build); * = member the adapter names"
        foreach ($a in $affected.Keys) {
            Write-Detail "$a$(if ($likely -contains $a) { '  (LIKELY affected)' })"
            foreach ($l in $affected[$a]) { Write-Detail "  $l" }
        }
    }
}

# ---------------------------------------------------------------- 3. build and unit tests
$buildLog = Join-Path $logDir "check-update-build.txt"
dotnet build (Join-Path $root "SpireParty.sln") -c Debug *> $buildLog
if ($LASTEXITCODE -eq 0) { Add-Result "3 build" "PASS" "dotnet build -c Debug" }
else {
    Add-Result "3 build" "FAIL" "dotnet build failed (API break?); see $buildLog"
    Select-String -Path $buildLog -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { Write-Detail $_.Line.Trim() }
}
$testLog = Join-Path $logDir "check-update-test.txt"
$buildOk = $LASTEXITCODE -eq 0
if ($buildOk) {
    dotnet test (Join-Path $root "SpireParty.sln") -c Debug --no-build *> $testLog
    $summary = Select-String -Path $testLog -Pattern "(Passed|Failed)!.*" | Select-Object -Last 1
    if ($LASTEXITCODE -eq 0) { Add-Result "3 test" "PASS" "dotnet test: $(if ($summary) { $summary.Matches[0].Value } else { 'ok' })" }
    else {
        Add-Result "3 test" "FAIL" "dotnet test failed; see $testLog"
        Select-String -Path $testLog -Pattern "^\s*Failed " | Select-Object -First 10 | ForEach-Object { Write-Detail $_.Line.Trim() }
    }
} else { Add-Result "3 test" "SKIP" "build failed" }

# ---------------------------------------------------------------- 4. deploy, one light host, self-check lines
$launchOk = $false
if ($SkipLaunch) { Add-Result "4 launch" "SKIP" "-SkipLaunch" }
elseif (-not $buildOk) { Add-Result "4 launch" "SKIP" "build failed" }
else {
    try {
        & (Join-Path $tools "deploy.ps1") | Out-Null
        $runDir = Join-Path $logDir $Run
        if (Test-Path $runDir) { Remove-Item -Recurse -Force $runDir }
        $hostLog = Join-Path $runDir "host.log"
        try {
            & (Join-Path $tools "party-test.ps1") -Run $Run -Clients 0 -HostTimeoutSeconds $LaunchTimeoutSeconds | Out-Null
        } catch {
            # A test profile the game only just created has mods off; party-test.ps1 turns them on next start.
            if (-not (Select-String -Path $hostLog -Pattern "has not yet seen the mods warning" -Quiet -ErrorAction SilentlyContinue)) { throw }
            Write-Detail "new test profile had mods off; enabled, starting the host again"
            & (Join-Path $tools "party-test.ps1") -Stop | Out-Null
            while (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
            Remove-Item -Recurse -Force $runDir
            & (Join-Path $tools "party-test.ps1") -Run $Run -Clients 0 -HostTimeoutSeconds $LaunchTimeoutSeconds | Out-Null
        }
        Start-Sleep -Seconds 3
        $loaded = Select-String -Path $hostLog -Pattern "\[SpireParty\] SpireParty .* loaded \|" | Select-Object -First 1
        $applied = Select-String -Path $hostLog -Pattern "\[SpireParty\] adapters applied: \[(.*)\]" | Select-Object -First 1
        $notApplied = @(Select-String -Path $hostLog -Pattern "\[SpireParty\] adapter .* not applied: .*" | ForEach-Object { $_.Matches[0].Value })
        $cap = Select-String -Path $hostLog -Pattern "\[SpireParty\] player cap \d+" | Select-Object -First 1
        $expected = Get-ChildItem $adapterDir -Filter "*Adapter.cs" | ForEach-Object {
            if ((Get-Content $_.FullName -Raw) -match 'Name\s*=>\s*"([^"]+)"') { $Matches[1] } }
        $got = if ($applied) { $applied.Matches[0].Groups[1].Value -split ",\s*" } else { @() }
        $missing = @($expected | Where-Object { $got -notcontains $_ })
        if ($loaded) { Write-Detail ($loaded.Line -replace '^.*\[SpireParty\]', '[SpireParty]' -replace '\| user data dir .*$', '') }
        if ($cap) { Write-Detail $cap.Matches[0].Value }
        foreach ($l in $notApplied) { Write-Detail $l }
        if (-not $loaded) { Add-Result "4 launch" "FAIL" "no SpireParty startup line in $hostLog" }
        elseif ($missing.Count -gt 0 -or $notApplied.Count -gt 0) {
            Add-Result "4 launch" "FAIL" "self-check: not applied: $(($missing) -join ', ')"
        } else {
            Add-Result "4 launch" "PASS" "host started; all $($expected.Count) adapters applied; $(if ($cap) { $cap.Matches[0].Value -replace '\[SpireParty\] ', '' })"
            $launchOk = $true
        }
    } catch {
        Add-Result "4 launch" "FAIL" "$_"
    } finally {
        & (Join-Path $tools "party-test.ps1") -Stop | Out-Null
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
    }
}

# ---------------------------------------------------------------- 5. scenarios (-Full)
if (-not $Full) { Add-Result "5 scenario" "SKIP" "add -Full (about 45 min; -Gate adds act1-8)" }
elseif (-not $launchOk) { Add-Result "5 scenario" "SKIP" "step 4 did not pass" }
else {
    $allArgs = @{}
    if ($Gate) { $allArgs.Gate = $true }
    $out = & (Join-Path $tools (Join-Path "scenarios" "all.ps1")) @allArgs
    $ok = $LASTEXITCODE -eq 0
    Add-Result "5 scenario" $(if ($ok) { "PASS" } else { "FAIL" }) "tools/scenarios/all.ps1$(if ($Gate) { ' -Gate' }) (outputs in spikes/logs/scenario-*.txt)"
    foreach ($l in $out) { Write-Detail "$l" }
}

# ---------------------------------------------------------------- summary and what to record
$fails = @($results | Where-Object Status -eq "FAIL").Count
$reviews = @($results | Where-Object Status -eq "REVIEW").Count
$passes = @($results | Where-Object Status -eq "PASS").Count
Write-Host ""
$verdict = if ($fails -gt 0) { "FAIL" } elseif ($reviews -gt 0) { "REVIEW" } else { "PASS" }
$sum = "SUMMARY: $verdict  ($passes pass, $reviews review, $fails fail) on $($cur.version) ($($cur.commit)), Steam $($cur.steam)"
Write-Host $sum -ForegroundColor $(@{ PASS = "Green"; REVIEW = "Yellow"; FAIL = "Red" }[$verdict])
$report.Add(""); $report.Add($sum)

$todo = New-Object System.Collections.Generic.List[string]
if ($affected.Count -gt 0) {
    $todo.Add("Adapters that may be affected: $(($affected.Keys) -join ', ')")
    if ($likely.Count -gt 0) { $todo.Add("  likely (a member they name changed or a type is gone): $($likely -join ', ')") }
    $todo.Add("  read each changed member in $newDir against the adapter; the scenarios (-Full) exercise them in play")
}
if ($buildChanged) {
    $todo.Add("docs/game-version.md (once every step passes): set these rows")
    $todo.Add("  | Checked on | $(Get-Date -Format yyyy-MM-dd) (sts2.dll SHA-256 starts $($cur.sha.Substring(0, 16))) |")
    $todo.Add("  | Steam branch (beta key) | ``$($cur.betakey)`` |")
    $todo.Add("  | Steam build ID | $($cur.steam) |")
    $todo.Add("  | ``release_info.json`` version | $($cur.version) |")
    $todo.Add("  | ``release_info.json`` commit | $($cur.commit) |")
    $todo.Add("  | ``release_info.json`` date | $($cur.date) |")
    $todo.Add("  | ``release_info.json`` branch | $($cur.branch) |")
    $todo.Add("  | ``release_info.json`` main_assembly_hash | $($cur.asmhash) |")
    $todo.Add("  | .NET runtime (``sts2.runtimeconfig.json``) | $($cur.runtime) |")
    $todo.Add("  | ``sts2.dll`` size (Windows) | $('{0:N0}' -f $cur.size) bytes |")
    $todo.Add("  then the Machines table, and the save versions and ModelIdSerializationCache hash from the host log")
} else {
    $todo.Add("docs/game-version.md: build unchanged; add today's date to 'Checked on' if this was a release check")
}
if ($affected.Count -gt 0 -or $fails -gt 0) {
    $todo.Add("docs/findings.md: a section '## Game update $($cur.version) ($($cur.commit))' with, per affected adapter,")
    $todo.Add("  what changed in the game, whether SpireParty needed a change, and the fallback taken if any")
} else {
    $todo.Add("docs/findings.md: nothing new (no adapter type changed)")
}
Write-Host ""
Write-Host "To record:"
$report.Add(""); $report.Add("To record:")
foreach ($t in $todo) { Write-Host "  $t"; $report.Add("  $t") }

$reportFile = Join-Path $logDir "check-update-$build.txt"
$report | Set-Content -Encoding utf8 $reportFile
Write-Host ""
Write-Host "Report: $reportFile"
if ($fails -gt 0) { exit 1 }
exit 0
