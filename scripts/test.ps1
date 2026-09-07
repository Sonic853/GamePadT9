param([switch]$Notepad)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $projectRoot 'artifacts\app\GamePadT9.exe'
$mode = if ($Notepad) { '--verify-notepad' } else { '--self-test' }
$prefix = if ($Notepad) { 'notepad' } else { 'self-test' }
$process = Start-Process -FilePath $app -ArgumentList $mode -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $projectRoot "artifacts\$prefix.stdout.txt") `
    -RedirectStandardError (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
if ($process.ExitCode -ne 0) {
    Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
    throw "$prefix failed (exit $($process.ExitCode))."
}
Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stdout.txt")
