param([switch]$Notepad, [switch]$Editor, [switch]$Installed, [switch]$Standalone, [switch]$InputMethod, [switch]$Backspace, [switch]$Xiaobai, [switch]$Settings, [switch]$Controllers, [switch]$Focus, [switch]$Proxy, [switch]$Components, [ValidateSet('x64','x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
if ($InputMethod -or $Settings -or $Focus) { $Editor = $true; $Installed = $true }
if ($Standalone -and !$Editor) { throw '-Standalone requires -Editor.' }
if ($Standalone) { $Installed = $true }
if ($Proxy) {
    if ($Standalone -or $Installed -or $InputMethod -or $Settings -or $Focus) { throw '-Proxy requires isolated editor tests.' }
    $Editor = $true
}
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $projectRoot 'artifacts\app\GamePadT9.exe'
$mode = if ($Editor) { '--verify-editor' } elseif ($Notepad) { '--verify-notepad' } else { '--self-test' }
$prefix = if ($Editor) { 'editor' } elseif ($Notepad) { 'notepad' } else { 'self-test' }
if ($Standalone) { $prefix += '-standalone'; $mode += ' --standalone' }
if ($Proxy) { $prefix += '-proxy'; $mode += ' --proxy' }
if ($Editor) {
    $editorFolder = 'test-editor' + $(if ($Proxy) { '-proxy' } elseif ($Standalone) { '-standalone' } elseif ($Installed) { '-installed' } else { '' }) + $(if ($Architecture -eq 'x86') { '-x86' } else { '' })
    $editorOut = Join-Path $projectRoot ('artifacts\' + $editorFolder)
    $useIsolation = (!$Installed).ToString().ToLowerInvariant()
    # The embedded COM manifest changes between isolated and installed tests. Force a
    # rebuild so MSBuild cannot reuse the other mode's apphost/managed assembly.
    dotnet build (Join-Path $projectRoot 'tests\TestEditor\TestEditor.csproj') -t:Rebuild -c Release -r "win-$Architecture" "-p:Isolated=$useIsolation" -o $editorOut --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test editor build failed.' }
    if (!$Installed) {
        $kind = if ($Proxy) { 'proxy' } else { 'xiaobai' }
        $release = (Get-Content -LiteralPath (Join-Path $projectRoot "artifacts\$kind-$Architecture-path.txt") -Raw).Trim()
        $name = if ($Proxy) { 'GamePadT9.Xiaobai.dll' } else { 'weasel-gamepad.dll' }
        Copy-Item -LiteralPath (Join-Path $release $name) -Destination (Join-Path $editorOut 'weasel.dll') -Force
        if ($Proxy) {
            $view = if ($Architecture -eq 'x64') { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
            $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
            $key = $registry.OpenSubKey('SOFTWARE\Classes\CLSID\{A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A}\InprocServer32')
            try { if (!$key) { throw 'Installed xiaobai is required for proxy integration tests.' }; $original = [string]$key.GetValue('') }
            finally { if ($key) { $key.Dispose() }; $registry.Dispose() }
            $protectedRoot = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
            $componentInstallRoot = Join-Path $protectedRoot 'GamePadT9'
            if ([IO.Path]::GetFullPath($original).StartsWith($componentInstallRoot + '\',[StringComparison]::OrdinalIgnoreCase)) {
                $backup = Get-Content -LiteralPath (Join-Path $componentInstallRoot 'components\original-registration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $original = ($backup.entries | Where-Object architecture -eq $Architecture).originalPath
            }
            if (!$original -or $original.Contains("`n") -or $original.Contains("`r")) { throw 'Invalid original DLL path.' }
            [IO.File]::WriteAllText((Join-Path $editorOut 'original.ini'), "[Original]`r`nPath=$original`r`n", [Text.Encoding]::Unicode)
        }
        $mode += ' --isolated'
    }
    if ($Architecture -eq 'x86') { $mode += ' --x86'; $prefix += '-x86' }
}
if ($InputMethod) { $prefix = "input-method-$Architecture"; $mode = '--verify-input-method' + $(if ($Architecture -eq 'x86') { ' --x86' } else { '' }) }
if ($Backspace) {
    dotnet build (Join-Path $projectRoot 'tests\BackspaceEditor\BackspaceEditor.csproj') -c Release -r "win-$Architecture" -o (Join-Path $projectRoot "artifacts\backspace-editor-$Architecture") --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backspace test editor build failed.' }
    $prefix = "backspace-$Architecture"
    $mode = '--verify-backspace' + $(if ($Architecture -eq 'x86') { ' --x86' } else { '' }) + $(if ($Xiaobai) { ' --xiaobai' } else { '' })
}
if ($Settings) { $prefix = 'settings'; $mode = '--verify-settings' }
if ($Controllers) { $prefix = 'controllers'; $mode = '--verify-controllers' }
if ($Components) { $prefix = 'components'; $mode = '--verify-components' }
if ($Focus) { $prefix = "focus-$Architecture"; $mode = '--verify-focus' + $(if ($Architecture -eq 'x86') { ' --x86' } else { '' }) }
$process = Start-Process -FilePath $app -ArgumentList $mode -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $projectRoot "artifacts\$prefix.stdout.txt") `
    -RedirectStandardError (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
if ($process.ExitCode -ne 0) {
    Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
    throw "$prefix failed (exit $($process.ExitCode))."
}
Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stdout.txt")
