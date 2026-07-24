# Packs the staged plugin folder into a ZIP with FORWARD-SLASH entry names.
# PowerShell's Compress-Archive writes backslash separators, which Rider's (Java) plugin loader
# rejects with "Corrupted archive (no file entries)". We add each entry by an explicit '/'-joined
# name so the archive is portable regardless of the PowerShell / .NET version.
param(
    [Parameter(Mandatory = $true)][string]$StageRoot,
    [Parameter(Mandatory = $true)][string]$Zip
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip -Force }

$root = (Resolve-Path -LiteralPath $StageRoot).Path.TrimEnd('\')
$files = Get-ChildItem -LiteralPath $root -Recurse -File

$archive = [System.IO.Compression.ZipFile]::Open($Zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $rel)
        Write-Host "  + $rel"
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Wrote $Zip"
