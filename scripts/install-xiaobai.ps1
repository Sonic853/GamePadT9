param([switch]$Restore, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$admin) {
    if ($Elevated) { throw 'Administrator token required.' }
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'),'-Elevated')
    if ($Restore) { $arguments += '-Restore' }
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installation failed (exit $($process.ExitCode)); see artifacts/xiaobai-install.log." }
    Get-Content -Encoding UTF8 (Join-Path $projectRoot 'artifacts\xiaobai-install.log') -Tail 12
    exit 0
}
Start-Transcript -Path (Join-Path $projectRoot 'artifacts\xiaobai-install.log') -Force | Out-Null
$installRoot = Join-Path $env:ProgramFiles 'GamePadT9\components'
$backupPath = Join-Path $installRoot 'original-registration.json'
$classPath = 'SOFTWARE\Classes\CLSID\{A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A}\InprocServer32'
function Open-Class([string]$architecture, [bool]$write) {
    $view = if ($architecture -eq 'x64') { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
    try { $baseKey.OpenSubKey($classPath,$write) } finally { $baseKey.Dispose() }
}
function Is-InstalledPath([string]$path) {
    [IO.Path]::GetFullPath($path).StartsWith([IO.Path]::GetFullPath($installRoot) + '\',[StringComparison]::OrdinalIgnoreCase)
}
$changes = [Collections.Generic.List[object]]::new()
try {
    if ($Restore) {
        if (!(Test-Path -LiteralPath $backupPath)) { throw 'No original registration backup exists.' }
        $backup = Get-Content -LiteralPath $backupPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($entry in $backup.entries) {
            $key = Open-Class $entry.architecture $true
            try {
                if (!$key) { throw 'Existing xiaobai COM entry is missing.' }
                $current = [string]$key.GetValue('')
                if ($current -eq $entry.originalPath) { continue }
                if (!(Is-InstalledPath $current)) { throw 'Another installer changed the input component; refusing to overwrite its registration.' }
                if ((Get-FileHash -LiteralPath $entry.originalPath).Hash -ne $entry.sha256) { throw 'Original component changed since backup; restore requires review.' }
                $key.SetValue('',[string]$entry.originalPath,[Microsoft.Win32.RegistryValueKind]$entry.valueKind)
                Write-Output "Restored $($entry.architecture): $($entry.originalPath)"
            } finally { if ($key) { $key.Dispose() } }
        }
    } else {
        $entries = @(); $planned = @()
        foreach ($architecture in @('x64','x86')) {
            $view = if ($architecture -eq 'x64') { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
            $userBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
            $userKey = $userBase.OpenSubKey($classPath)
            try { if ($userKey -and $userKey.GetValue('')) { throw 'A per-user xiaobai COM override exists; refusing an ambiguous installation.' } }
            finally { if ($userKey) { $userKey.Dispose() }; $userBase.Dispose() }
            $key = Open-Class $architecture $false
            try {
                if (!$key) { throw "xiaobai $architecture is not installed." }
                $current = [string]$key.GetValue('')
                $expected = Join-Path $env:SystemRoot ($(if ($architecture -eq 'x64') { 'System32' } else { 'SysWOW64' }) + '\weasel.dll')
                if ($current -ne $expected -and !(Is-InstalledPath $current)) { throw "Unexpected xiaobai component: $current" }
                $entries += [pscustomobject]@{ architecture=$architecture; originalPath=$current; valueKind=$key.GetValueKind('').ToString(); sha256=(Get-FileHash -LiteralPath $current).Hash }
            } finally { if ($key) { $key.Dispose() } }
            $release = (Get-Content -LiteralPath (Join-Path $projectRoot "artifacts\xiaobai-$architecture-path.txt") -Raw).Trim()
            $source = Join-Path $release 'weasel-gamepad.dll'
            $sha = (Get-FileHash -LiteralPath $source).Hash
            $testName = if ($architecture -eq 'x86') { 'editor-x86-isolated-verification.json' } else { 'editor-isolated-verification.json' }
            $verified = Get-Content -LiteralPath (Join-Path $projectRoot ('artifacts\' + $testName)) -Raw -Encoding UTF8 | ConvertFrom-Json
            if (!$verified.passed -or !$verified.completedAllChecks -or !$verified.isolatedComponent -or $verified.componentSha256 -ne $sha) {
                throw "Run the isolated editor tests against this exact $architecture component before installing."
            }
            $destination = Join-Path $installRoot ($architecture + '-' + $sha.Substring(0,16))
            $planned += [pscustomobject]@{ architecture=$architecture; source=$source; destination=(Join-Path $destination 'weasel-gamepad.dll'); sha256=$sha; previous=$current }
        }
        New-Item -ItemType Directory -Force $installRoot | Out-Null
        if (!(Test-Path -LiteralPath $backupPath)) {
            if ($entries | Where-Object { Is-InstalledPath $_.originalPath }) { throw 'Cannot recover the original registration from an already modified component.' }
            @{ time=[DateTimeOffset]::Now.ToString('o'); entries=$entries } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $backupPath -Encoding UTF8
        }
        # Stage and verify BOTH files before changing either COM server path.
        foreach ($item in $planned) {
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($item.destination)) | Out-Null
            if (!(Test-Path -LiteralPath $item.destination)) { Copy-Item -LiteralPath $item.source -Destination $item.destination }
            if ((Get-FileHash -LiteralPath $item.destination).Hash -ne $item.sha256) { throw 'Staged component checksum mismatch.' }
        }
        foreach ($item in $planned) {
            $key = Open-Class $item.architecture $true
            try {
                if ($key.GetValue('') -ne $item.previous) { throw 'Input component registration changed during installation.' }
                $key.SetValue('',$item.destination,[Microsoft.Win32.RegistryValueKind]::String)
                $changes.Add($item)
                Write-Output "Installed $($item.architecture): $($item.destination)"
            } finally { $key.Dispose() }
        }
        $planned | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\xiaobai-installation.json') -Encoding UTF8
        Write-Output 'Existing xiaobai profiles and keyboard layouts were retained. No profile was registered.'
    }
} catch {
    foreach ($item in $changes) {
        $key = Open-Class $item.architecture $true
        try { if ($key.GetValue('') -eq $item.destination) { $key.SetValue('',$item.previous) } } finally { $key.Dispose() }
    }
    Write-Output $_
    Stop-Transcript | Out-Null
    exit 1
}
Stop-Transcript | Out-Null
