param([Parameter(Mandatory=$true)][ValidateSet('RegisterStandalone','UnregisterStandalone','InjectXiaobai','RestoreXiaobai')][string]$Action)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $projectRoot 'artifacts\app\GamePadT9.exe'
if (!(Test-Path -LiteralPath $app)) { throw 'Build GamePadT9 before managing input components.' }
$stdout = Join-Path $projectRoot 'artifacts\component-action.stdout.txt'
$stderr = Join-Path $projectRoot 'artifacts\component-action.stderr.txt'
$process = Start-Process -FilePath $app -ArgumentList @('--component-action',$Action) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Get-Content -LiteralPath $stdout -Encoding UTF8
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $stderr -Encoding UTF8; throw 'Component operation failed. Original registrations are retained for restore/retry.' }
