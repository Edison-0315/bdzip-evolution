# Runs only inside the release workflow, using its repository-scoped token.
$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_EVENT_PATH -or -not $env:GITHUB_TOKEN) { throw 'A GitHub release workflow context is required.' }
$eventData = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
if (-not $eventData.release.id) { throw 'The triggering event must contain a release.' }
$uploadBase = ([string]$eventData.release.upload_url).Split('{')[0]
$expectedBase = 'https://uploads.github.com/repos/{0}/releases/{1}/assets' -f $env:GITHUB_REPOSITORY, $eventData.release.id
if ($uploadBase -ne $expectedBase) { throw 'Unexpected release upload destination.' }
$headers = @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$root = Split-Path $PSScriptRoot -Parent
foreach ($name in 'bdzip-evolution.exe', 'bdzip-evolution-windows-x64.zip', 'SHA256SUMS.txt') {
    $path = Join-Path $root "dist\$name"
    $encodedName = [Uri]::EscapeDataString($name)
    $asset = Invoke-RestMethod -Method Post -Uri "${uploadBase}?name=$encodedName" -Headers $headers -InFile $path -ContentType 'application/octet-stream'
    Write-Output ('Uploaded {0} ({1} bytes)' -f $asset.name, $asset.size)
}
