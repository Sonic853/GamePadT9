param([switch]$Notepad, [switch]$Editor, [switch]$Installed, [switch]$Standalone, [switch]$InputMethod, [ValidateSet('x64','x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
if ($InputMethod) { $Editor = $true; $Installed = $true }
if ($Standalone -and !$Editor) { throw '-Standalone requires -Editor.' }
if ($Standalone) { $Installed = $true }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $projectRoot 'artifacts\app\GamePadT9.exe'
$mode = if ($Editor) { '--verify-editor' } elseif ($Notepad) { '--verify-notepad' } else { '--self-test' }
$prefix = if ($Editor) { 'editor' } elseif ($Notepad) { 'notepad' } else { 'self-test' }
if ($Standalone) { $prefix += '-standalone'; $mode += ' --standalone' }
if ($Editor) {
    $editorFolder = 'test-editor' + $(if ($Standalone) { '-standalone' } elseif ($Installed) { '-installed' } else { '' }) + $(if ($Architecture -eq 'x86') { '-x86' } else { '' })
    $editorOut = Join-Path $projectRoot ('artifacts\' + $editorFolder)
    $useIsolation = (!$Installed).ToString().ToLowerInvariant()
    dotnet build (Join-Path $projectRoot 'tests\TestEditor\TestEditor.csproj') -c Release -r "win-$Architecture" "-p:Isolated=$useIsolation" -o $editorOut --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test editor build failed.' }
    if (!$Installed) {
        $release = (Get-Content -LiteralPath (Join-Path $projectRoot "artifacts\xiaobai-$Architecture-path.txt") -Raw).Trim()
        Copy-Item -LiteralPath (Join-Path $release 'weasel-gamepad.dll') -Destination (Join-Path $editorOut 'weasel.dll') -Force
        $mode += ' --isolated'
    }
    if ($Architecture -eq 'x86') { $mode += ' --x86'; $prefix += '-x86' }
}
if ($InputMethod) { $prefix = "input-method-$Architecture"; $mode = '--verify-input-method' + $(if ($Architecture -eq 'x86') { ' --x86' } else { '' }) }
$process = Start-Process -FilePath $app -ArgumentList $mode -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $projectRoot "artifacts\$prefix.stdout.txt") `
    -RedirectStandardError (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
if ($process.ExitCode -ne 0) {
    Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
    throw "$prefix failed (exit $($process.ExitCode))."
}
Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stdout.txt")
