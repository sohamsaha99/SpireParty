# Deploys a built SpireParty into <Sts2GameDir>/mods/SpireParty/.
# Usage: tools/deploy.ps1 [-Configuration Debug|Release] [-Build]
param(
    [string]$Configuration = "Debug",
    [switch]$Build
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" "SpireParty.csproj"))

if (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) {
    throw "Slay the Spire 2 is running. Close the game before deploying."
}

if ($Build) {
    dotnet build $proj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

# Ask MSBuild so the path logic lives only in Directory.Build.props.
$modsDir = (dotnet msbuild $proj -nologo -getProperty:Sts2ModsDir).Trim()
if (-not $modsDir) { throw "Could not resolve Sts2ModsDir from MSBuild." }

$outDir = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" (Join-Path "bin" (Join-Path $Configuration "net9.0"))))
$dll = Join-Path $outDir "SpireParty.dll"
if (-not (Test-Path $dll)) { throw "Build output not found: $dll (run with -Build)" }

New-Item -ItemType Directory -Force $modsDir | Out-Null

Copy-Item $dll $modsDir -Force
$pdb = Join-Path $outDir "SpireParty.pdb"
if (Test-Path $pdb) { Copy-Item $pdb $modsDir -Force }
Copy-Item (Join-Path $outDir "SpireParty.json") $modsDir -Force

foreach ($dir in @("lib", "web")) {
    $src = Join-Path $root $dir
    if (Test-Path $src) {
        $dst = Join-Path $modsDir $dir
        if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
        Copy-Item -Recurse $src $dst
    }
}
$libOut = Join-Path $outDir "lib"
if (Test-Path $libOut) { Copy-Item -Recurse -Force $libOut $modsDir }

foreach ($file in @("LICENSE", "THIRD_PARTY_NOTICES.md")) {
    $src = Join-Path $root $file
    if (Test-Path $src) { Copy-Item $src $modsDir -Force }
}

Write-Host "Deployed SpireParty ($Configuration) to $modsDir"
