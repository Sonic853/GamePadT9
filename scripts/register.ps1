param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
# Compatibility entry point. No new input profile or keyboard layout is registered.
if ($Unregister) {
    & (Join-Path $PSScriptRoot 'install-xiaobai.ps1') -Restore
} else {
    & (Join-Path $PSScriptRoot 'install-xiaobai.ps1')
}
