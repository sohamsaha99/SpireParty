# Builds the release zip: dist/SpireParty-<version>.zip, holding one SpireParty/ folder that
# players extract into the game's mods/ folder. Also writes a .sha256 file next to it.
# Usage: tools/package.ps1 [-SkipTests]
param(
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" "SpireParty.csproj"))
$manifestPath = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" "SpireParty.json"))

# The manifest version is what the game compares between players; the assembly version is what
# the SpireParty handshake compares. They must agree.
$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$version = $manifest.version
$projVersion = (dotnet msbuild $proj -nologo -getProperty:Version).Trim()
if ($version -ne $projVersion) {
    throw "Version mismatch: SpireParty.json says $version, SpireParty.csproj says $projVersion."
}
if ($version -notmatch '^0\.\d+\.\d+$') { throw "Version $version is not in 0.x.y form." }

if (-not $SkipTests) {
    dotnet test (Join-Path $root "SpireParty.sln") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$outDir = Join-Path $root (Join-Path "src" (Join-Path "SpireParty" (Join-Path "bin" (Join-Path "Release" "net9.0"))))
$files = [ordered]@{
    "SpireParty/SpireParty.dll"          = (Join-Path $outDir "SpireParty.dll")
    "SpireParty/SpireParty.json"         = (Join-Path $outDir "SpireParty.json")
    "SpireParty/LICENSE"                 = (Join-Path $root "LICENSE")
    "SpireParty/THIRD_PARTY_NOTICES.md"  = (Join-Path $root "THIRD_PARTY_NOTICES.md")
}
foreach ($src in $files.Values) {
    if (-not (Test-Path $src)) { throw "Missing file for the release: $src" }
}
# Third-party .NET libraries, if any are ever added, ship in lib/.
$libOut = Join-Path $outDir "lib"
if (Test-Path $libOut) {
    Get-ChildItem -Recurse -File $libOut | ForEach-Object {
        $rel = $_.FullName.Substring($libOut.Length).TrimStart('\', '/') -replace '\\', '/'
        $files["SpireParty/lib/$rel"] = $_.FullName
    }
}

# Never ship game assemblies.
$forbidden = @("sts2.dll", "GodotSharp.dll", "0Harmony.dll")
foreach ($entry in $files.Keys) {
    if ($forbidden -contains (Split-Path -Leaf $entry)) { throw "Refusing to package a game assembly: $entry" }
}

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zipPath = Join-Path $dist "SpireParty-$version.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# Write entries by hand: Compress-Archive in Windows PowerShell 5.1 stores backslashes in entry
# names, which breaks extraction on macOS and Linux.
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $files.Keys) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $files[$entry], $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

$hash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToLowerInvariant()
$hashLine = "$hash  SpireParty-$version.zip"
[System.IO.File]::WriteAllText("$zipPath.sha256", "$hashLine`n")

Write-Host "Packaged SpireParty $version"
Write-Host "  $zipPath"
foreach ($entry in $files.Keys) { Write-Host "    $entry" }
Write-Host "  SHA-256 $hash"
