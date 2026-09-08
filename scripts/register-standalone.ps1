param([switch]$Unregister, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$action = if ($Unregister) { 'UnregisterStandalone' } else { 'RegisterStandalone' }
& (Join-Path $PSScriptRoot 'manage-component.ps1') -Action $action
