param([switch]$Notepad, [switch]$Editor)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $projectRoot 'artifacts\app\GamePadT9.exe'
$mode = if ($Editor) { '--verify-editor' } elseif ($Notepad) { '--verify-notepad' } else { '--self-test' }
$prefix = if ($Editor) { 'editor' } elseif ($Notepad) { 'notepad' } else { 'self-test' }
if ($Editor) {
    dotnet build (Join-Path $projectRoot 'tests\TestEditor\TestEditor.csproj') -c Release -o (Join-Path $projectRoot 'artifacts\test-editor') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test editor build failed.' }
}
$process = Start-Process -FilePath $app -ArgumentList $mode -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $projectRoot "artifacts\$prefix.stdout.txt") `
    -RedirectStandardError (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
if ($process.ExitCode -ne 0) {
    Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stderr.txt")
    throw "$prefix failed (exit $($process.ExitCode))."
}
Get-Content -Encoding UTF8 (Join-Path $projectRoot "artifacts\$prefix.stdout.txt")
