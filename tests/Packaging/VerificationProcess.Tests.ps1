$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VerificationProcess.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path $projectRoot ('artifacts/verification-process-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$hostPath = (Get-Process -Id $PID).Path
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $checks.Add($Message)
}
function Run-Fixture([string]$Code, [string]$Name, [int]$Timeout = 30) {
    $setup = '[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); '
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($setup + $Code))
    Invoke-VerificationProcess -FilePath $hostPath -Arguments "-NoLogo -NoProfile -NonInteractive -EncodedCommand $encoded" `
        -WorkingDirectory $fixture -LogPrefix (Join-Path $fixture $Name) -Stage $Name -TimeoutSeconds $Timeout -HeartbeatSeconds 1
}

$savedNoGui = $env:DOTNET_DISABLE_GUI_ERRORS
$result = Run-Fixture '[Console]::WriteLine((@{ passed = $true; text = ([string][char]0x4F60 + [char]0x597D); noGui = $env:DOTNET_DISABLE_GUI_ERRORS } | ConvertTo-Json -Compress)); [Console]::Error.WriteLine("diagnostic"); exit 0' 'utf8'
$json = $result.StandardOutput | ConvertFrom-Json
Check ($result.ExitCode -eq 0 -and $json.passed -and $json.text -ceq ([string][char]0x4F60 + [char]0x597D)) 'Redirected Unicode JSON survives Windows PowerShell and pwsh encodings.'
Check ($result.StandardError.Contains('diagnostic') -and $json.noGui -eq '1') 'Stderr is separate and .NET GUI error prompts are disabled for the child only.'
Check ($env:DOTNET_DISABLE_GUI_ERRORS -ceq $savedNoGui) 'The runner does not change the parent environment.'
$result = Run-Fixture '[Console]::Write("o" * 200000); [Console]::Error.Write("e" * 200000); exit 23' 'large-output'
Check ($result.ExitCode -eq 23 -and $result.StandardOutput.Length -eq 200000 -and $result.StandardError.Length -eq 200000) 'Both redirected pipes drain concurrently and preserve a nonzero exit code.'
$clock = [Diagnostics.Stopwatch]::StartNew()
$rejected = $false
try { Run-Fixture '[Console]::WriteLine("before-stall"); [Console]::Error.WriteLine("native-stage"); Start-Sleep -Seconds 60' 'timeout' 5 | Out-Null }
catch { $rejected = $_.Exception.Message -like '*timed out after 5s*' }
Check ($rejected -and $clock.Elapsed.TotalSeconds -lt 20) 'A stuck child fails within its deadline instead of hanging the job.'
Check ((Get-Content -LiteralPath (Join-Path $fixture 'timeout.stdout.txt') -Raw).Contains('before-stall') -and
    (Get-Content -LiteralPath (Join-Path $fixture 'timeout.stderr.txt') -Raw).Contains('native-stage')) 'Partial stdout and stderr survive timeout cleanup.'
$metadata = Get-Content -LiteralPath (Join-Path $fixture 'timeout.process.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$remaining = Get-Process -Id $metadata.pid -ErrorAction SilentlyContinue
Check ($metadata.state -eq 'timed_out' -and !$remaining) 'Timeout diagnostics identify the stage and the owned child is no longer running.'
$rejected = $false
try {
    Invoke-VerificationProcess -FilePath (Join-Path $fixture 'missing.exe') -Arguments '--test' -WorkingDirectory $fixture `
        -LogPrefix (Join-Path $fixture 'missing') -Stage 'missing executable' | Out-Null
} catch { $rejected = $true }
$metadata = Get-Content -LiteralPath (Join-Path $fixture 'missing.process.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($rejected -and $metadata.state -eq 'failed') 'Startup failures also leave a diagnostic record.'
@{ passed = $true; checks = $checks } | ConvertTo-Json -Depth 3
