param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$locationFile = Join-Path $projectRoot 'artifacts\app\bridge-path.txt'
$bridgeFolder = if (Test-Path -LiteralPath $locationFile) { (Get-Content -LiteralPath $locationFile -Raw).Trim() } else { Join-Path $projectRoot 'artifacts\app' }
$control = Join-Path $bridgeFolder 'BridgeControl.exe'
if (!(Test-Path -LiteralPath $control)) { throw 'Run scripts/build.ps1 first.' }
$action = if ($Unregister) { 'unregister' } else { 'register' }
# Windows TSF profile/category registration needs an elevated token.
# This displays the normal Windows UAC consent dialog; it never bypasses UAC.
$process = Start-Process -FilePath $control -ArgumentList $action -Verb RunAs -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "TSF $action failed (exit $($process.ExitCode))." }
Write-Output "TSF $action succeeded."
