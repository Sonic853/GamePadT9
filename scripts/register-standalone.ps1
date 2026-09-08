param([switch]$Unregister, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$admin) {
    if ($Elevated) { throw 'Administrator token required.' }
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'),'-Elevated')
    if ($Unregister) { $arguments += '-Unregister' }
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Standalone registration failed; see artifacts/standalone-install.log.' }
    Get-Content -Encoding UTF8 (Join-Path $projectRoot 'artifacts\standalone-install.log') -Tail 12
    exit 0
}
Start-Transcript -Path (Join-Path $projectRoot 'artifacts\standalone-install.log') -Force | Out-Null
$protectedRoot = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$installRoot = Join-Path $protectedRoot 'GamePadT9\standalone'
$manifest = Join-Path $installRoot 'installation.json'
try {
    if ($Unregister) {
        $planned = @((Get-Content -LiteralPath $manifest -Encoding UTF8 -Raw | ConvertFrom-Json))
    } else {
        $planned = @()
        foreach ($architecture in @('x64','x86')) {
            $source = (Get-Content -LiteralPath (Join-Path $projectRoot "artifacts\standalone-$architecture-path.txt") -Raw).Trim()
            $sha = (Get-FileHash -LiteralPath (Join-Path $source 'GamePadT9.TextService.dll')).Hash
            $destination = Join-Path $installRoot ($architecture + '-' + $sha.Substring(0,16))
            New-Item -ItemType Directory -Force $destination | Out-Null
            foreach ($name in @('GamePadT9.TextService.dll','BridgeControl.exe')) {
                $target = Join-Path $destination $name
                if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target }
                if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source $name)).Hash) { throw 'Staged component checksum mismatch.' }
            }
            $planned += [pscustomobject]@{architecture=$architecture; destination=(Join-Path $destination 'GamePadT9.TextService.dll'); control=(Join-Path $destination 'BridgeControl.exe'); sha256=$sha}
        }
        # Retain cleanup paths even if one architecture's profile registration fails.
        $planned | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest -Encoding UTF8
    }
    $action = if ($Unregister) { 'unregister' } else { 'register' }
    $failures = @()
    foreach ($item in $planned) {
        $controlPath = [IO.Path]::GetFullPath($item.control)
        if (!$controlPath.StartsWith([IO.Path]::GetFullPath($installRoot) + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected registration helper path.' }
        & $controlPath $action
        if ($LASTEXITCODE -ne 0) { $failures += $item.architecture }
    }
    if ($failures.Count) { throw "Registration failed for: $($failures -join ', ')." }
    if ($Unregister) {
        # ITfInputProcessorProfiles::Unregister leaves the current user's enable preference.
        $preference = 'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\CTF\TIP\{595B67E9-48A3-4C82-B7B1-64E4A35C9D92}'
        if (Test-Path -LiteralPath $preference) { Remove-Item -LiteralPath $preference -Recurse }
    } else {
        $planned | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\standalone-installation.json') -Encoding UTF8
    }
    Write-Output "Standalone $action succeeded for x64 and x86. Existing xiaobai registration was not changed."
} catch {
    Write-Output $_
    Stop-Transcript | Out-Null
    exit 1
}
Stop-Transcript | Out-Null
