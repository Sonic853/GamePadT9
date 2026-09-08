param([switch]$Prepare, [switch]$SkipXiaobai, [switch]$Standalone, [switch]$SkipInputMethodControl, [switch]$LegacyXiaobaiSource)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Prepare) { & (Join-Path $PSScriptRoot 'prepare.ps1') }
if (!$SkipXiaobai) {
    $script = if ($LegacyXiaobaiSource) { 'build-xiaobai.ps1' } else { 'build-proxy.ps1' }
    & (Join-Path $PSScriptRoot $script) -Architecture x64
    & (Join-Path $PSScriptRoot $script) -Architecture x86
}
if ($Standalone) {
    & (Join-Path $PSScriptRoot 'build-standalone.ps1') -Architecture x64
    & (Join-Path $PSScriptRoot 'build-standalone.ps1') -Architecture x86
}
$out = Join-Path $projectRoot 'artifacts\app'
if (!$SkipInputMethodControl) {
    & (Join-Path $PSScriptRoot 'build-input-method.ps1') -Architecture x64
    & (Join-Path $PSScriptRoot 'build-input-method.ps1') -Architecture x86
}
New-Item -ItemType Directory -Force $out | Out-Null
dotnet build (Join-Path $projectRoot 'src\GamePadT9\GamePadT9.csproj') -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
