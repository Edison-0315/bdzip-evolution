$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 .NET Framework compiler not found.' }
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$destination = Join-Path $bin 'bdzip-evolution.exe'
$sources = 'Native.cs', 'Analyzer.cs', 'Engine.cs', 'Tests.cs', 'App.cs' | ForEach-Object { Join-Path $root "src\$_" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$destination" "/win32manifest:$(Join-Path $root 'src\app.manifest')" "/resource:$(Join-Path $root 'assets\capstone.dll.gz'),capstone.gz" "/resource:$(Join-Path $root 'licenses\CAPSTONE-LICENSE.txt'),CapstoneLicense" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output $destination
