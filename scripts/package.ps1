$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'bdzip-evolution'
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin\bdzip-evolution.exe') -Destination $stage -Force
foreach ($name in 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md') {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage -Force
}
Copy-Item -LiteralPath (Join-Path $root 'licenses\CAPSTONE-LICENSE.txt') -Destination (Join-Path $stage 'licenses') -Force
$archive = Join-Path $dist 'bdzip-evolution-windows-x64.zip'
# Enumerate only the release allowlist, even if a previous staging folder exists.
$releaseFiles = @('bdzip-evolution.exe', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'licenses\CAPSTONE-LICENSE.txt') | ForEach-Object { Join-Path $stage $_ }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($archive, [IO.FileMode]::Create)
try {
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($releaseFile in $releaseFiles) {
            $entryName = $releaseFile.Substring($stage.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $releaseFile, $entryName) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$exe = Join-Path $dist 'bdzip-evolution.exe'
Copy-Item -LiteralPath (Join-Path $stage 'bdzip-evolution.exe') -Destination $exe -Force
$hashes = @($exe, $archive) | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf) }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $hashes, (New-Object Text.UTF8Encoding($false)))
Write-Output $archive
