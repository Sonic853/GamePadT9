# Run only an owned verification child, with bounded waits and durable UTF-8 logs.
function Stop-VerificationProcess([Diagnostics.Process]$Process) {
    if ($Process.HasExited) { return }
    if ($Process.GetType().GetMethod('Kill', [type[]]@([bool]))) {
        $Process.Kill($true)
    } else {
        # Windows PowerShell's .NET Framework has no Kill(entireProcessTree).
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = Join-Path $env:SystemRoot 'System32/taskkill.exe'
        $info.Arguments = "/PID $($Process.Id) /T /F"
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $killer = [Diagnostics.Process]::Start($info)
        try { if (!$killer.WaitForExit(5000)) { $killer.Kill() } }
        finally { $killer.Dispose() }
        if (!$Process.HasExited) { $Process.Kill() }
    }
    if (!$Process.WaitForExit(5000)) { throw "Could not stop verification PID $($Process.Id)." }
}

function Invoke-VerificationProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogPrefix,
        [Parameter(Mandatory)][string]$Stage,
        [ValidateRange(1, 1800)][int]$TimeoutSeconds = 120,
        [ValidateRange(1, 60)][int]$HeartbeatSeconds = 15
    )
    $ErrorActionPreference = 'Stop'
    if (![IO.Path]::IsPathRooted($LogPrefix)) { $LogPrefix = Join-Path $PWD $LogPrefix }
    New-Item -ItemType Directory -Path (Split-Path $LogPrefix -Parent) -Force | Out-Null
    $stdoutPath = $LogPrefix + '.stdout.txt'; $stderrPath = $LogPrefix + '.stderr.txt'
    $metadata = [ordered]@{ stage = $Stage; arguments = $Arguments; timeoutSeconds = $TimeoutSeconds; startedUtc = [DateTime]::UtcNow.ToString('o'); pid = $null; state = 'starting' }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath; $info.Arguments = $Arguments; $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.ErrorDialog = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $info.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
    $info.EnvironmentVariables['DOTNET_HOST_TRACE'] = '1'
    $info.EnvironmentVariables['DOTNET_HOST_TRACEFILE'] = $LogPrefix + '.host.txt'
    $info.EnvironmentVariables['GAMEPADT9_VERIFICATION_PROGRESS'] = '1'
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    $started = $false; $stdoutFile = $null; $stderrFile = $null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        # No PowerShell event callbacks: pipe reads run on .NET tasks, not on a
        # thread that lacks a PowerShell runspace. Buffer size 1 keeps logs live.
        $stdoutFile = [IO.FileStream]::new($stdoutPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite, 1, $true)
        $stderrFile = [IO.FileStream]::new($stderrPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite, 1, $true)
        $started = $process.Start()
        if (!$started) { throw "Could not start verification: $Stage" }
        $metadata.pid = $process.Id; $metadata.state = 'running'
        [IO.File]::WriteAllText($LogPrefix + '.process.json', ($metadata | ConvertTo-Json))
        Write-Host "[independent] START $Stage; PID $($process.Id); timeout ${TimeoutSeconds}s"
        $stdoutCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $stderrCopy = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        $nextHeartbeat = $HeartbeatSeconds; $lastErrorLength = 0L
        $exited = $false
        while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            $remaining = [Math]::Max(1, [Math]::Min(1000, [Math]::Ceiling($TimeoutSeconds * 1000 - $clock.Elapsed.TotalMilliseconds)))
            if ($process.WaitForExit([int]$remaining)) { $exited = $true; break }
            if ($clock.Elapsed.TotalSeconds -ge $nextHeartbeat) {
                Write-Host "[independent] WAIT $Stage; PID $($process.Id); elapsed $([int]$clock.Elapsed.TotalSeconds)s / ${TimeoutSeconds}s"
                $length = (Get-Item -LiteralPath $stderrPath).Length
                if ($length -ne $lastErrorLength) {
                    Get-Content -LiteralPath $stderrPath -Encoding UTF8 -Tail 4 | ForEach-Object { Write-Host "[independent] $($_.Substring(0, [Math]::Min(500, $_.Length)))" }
                    $lastErrorLength = $length
                }
                $nextHeartbeat += $HeartbeatSeconds
            }
        }
        if (!$exited) { $metadata.state = 'timed_out'; Stop-VerificationProcess $process }
        # A descendant can keep a pipe open even after its parent exits. Never
        # replace WaitForExit() with another unbounded wait on redirected output.
        $copies = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdoutCopy, $stderrCopy))
        if (!$copies.Wait(5000)) { throw "Verification output pipes did not close within 5s: $Stage. Logs: $LogPrefix" }
        $stdoutFile.Flush(); $stderrFile.Flush()
        $stdoutFile.Dispose(); $stdoutFile = $null
        $stderrFile.Dispose(); $stderrFile = $null
        if (!$exited) { throw [TimeoutException]::new("Verification timed out after ${TimeoutSeconds}s: $Stage ($Arguments). Logs: $LogPrefix") }
        $metadata.state = 'exited'; $metadata.exitCode = $process.ExitCode
        Write-Host "[independent] END $Stage; exit $($process.ExitCode); elapsed $([Math]::Round($clock.Elapsed.TotalSeconds, 1))s"
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = [IO.File]::ReadAllText($stdoutPath, [Text.Encoding]::UTF8)
            StandardError = [IO.File]::ReadAllText($stderrPath, [Text.Encoding]::UTF8)
        }
    } catch {
        if ($metadata.state -ne 'timed_out') { $metadata.state = 'failed' }
        $metadata.error = $_.Exception.Message
        foreach ($path in @($stdoutPath, $stderrPath)) {
            if (Test-Path -LiteralPath $path) {
                Write-Host "[independent] Last output from ${path}:"
                Get-Content -LiteralPath $path -Encoding UTF8 -Tail 12 | ForEach-Object { Write-Host $_ }
            }
        }
        throw
    } finally {
        try { if ($started -and !$process.HasExited) { Stop-VerificationProcess $process } }
        finally {
            $process.Dispose()
            if ($stdoutFile) { $stdoutFile.Dispose() }
            if ($stderrFile) { $stderrFile.Dispose() }
            $clock.Stop(); $metadata.elapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 2)
            [IO.File]::WriteAllText($LogPrefix + '.process.json', ($metadata | ConvertTo-Json))
        }
    }
}
