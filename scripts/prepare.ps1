param(
    [string]$InstallRoot = 'C:\Program Files\Rime\xiaobait9-2026.08.04',
    [string]$RimeUserRoot = (Join-Path $env:APPDATA 'Rime')
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generatedPath = Join-Path $projectRoot 'data\installed'
$prebuiltPath = Join-Path $generatedPath 'build'
$isolatedUserPath = Join-Path $projectRoot 'artifacts\rime-user'
foreach ($required in @((Join-Path $InstallRoot 'rime.dll'),(Join-Path $RimeUserRoot 'build\xiaobai_simp.schema.yaml'),(Join-Path $RimeUserRoot 'build\xiaobai_simp.table.bin'))) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing installed/deployed xiaobai component: $required" }
}
New-Item -ItemType Directory -Force $prebuiltPath | Out-Null
New-Item -ItemType Directory -Force $isolatedUserPath | Out-Null
foreach ($scriptName in @('rime.lua','lua')) {
    $scriptSource = Join-Path $RimeUserRoot $scriptName
    if (Test-Path -LiteralPath $scriptSource) { Copy-Item -LiteralPath $scriptSource -Destination $isolatedUserPath -Recurse -Force }
}
# Read-only snapshot of deployed tables/schemas; do not share the live user database.
Get-ChildItem -LiteralPath (Join-Path $RimeUserRoot 'build') -File |
    Where-Object { $_.Extension -in '.yaml','.bin' } |
    Copy-Item -Destination $prebuiltPath
$configuration = [ordered]@{
    installRoot = [IO.Path]::GetFullPath($InstallRoot)
    schema = 'xiaobai_simp'
    prebuiltPath = $prebuiltPath
    userPath = $isolatedUserPath
}
$configuration | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $projectRoot 'gamepadt9.json')
$sources = [ordered]@{
    mode = 'Installed xiaobai T9, original x86 rime.dll and deployed schema'
    installRoot = $InstallRoot
    sourceRimeUserRoot = $RimeUserRoot
    hashes = @(Get-FileHash (Join-Path $InstallRoot 'rime.dll'),(Join-Path $prebuiltPath 'xiaobai_simp.schema.yaml'),(Join-Path $prebuiltPath 'xiaobai_simp.table.bin') -Algorithm SHA256 | Select-Object Path,Hash)
}
$sources | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $generatedPath 'sources.json')
Write-Output 'Original xiaobai runtime configured; deployed data snapshot prepared.'
