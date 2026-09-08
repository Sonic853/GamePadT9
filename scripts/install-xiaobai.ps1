param([switch]$Restore, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$action = if ($Restore) { 'RestoreXiaobai' } else { 'InjectXiaobai' }
& (Join-Path $PSScriptRoot 'manage-component.ps1') -Action $action
