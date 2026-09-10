param([Parameter(Mandatory)][string]$Archive, [string]$EditorPath, [switch]$RegenerateCache)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path $projectRoot ('artifacts/iv-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Compression.ZipFile]::ExtractToDirectory((Get-Item -LiteralPath $Archive).FullName, $fixture)
$folder = Join-Path $fixture 'GamePadT9-Portable-Independent'
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Value, [string]$Message) { if (!$Value) { throw $Message }; $checks.Add($Message) }
$marker = Get-Content -LiteralPath (Join-Path $folder 'portable.json') -Raw | ConvertFrom-Json
Check ($marker.runtime -eq 'bundled-rime' -and !$marker.selfContained) 'Independent package uses bundled Rime and excludes the .NET runtime.'
Check (!(Test-Path -LiteralPath (Join-Path $folder 'data/builtin-user'))) 'The published archive contains no learning database.'
Check (!(Test-Path -LiteralPath (Join-Path $folder 'user-settings.json')) -and !(Test-Path -LiteralPath (Join-Path $folder 'program-profiles.json'))) 'The archive contains no machine-specific preferences.'
$runtime = Join-Path $folder 'runtime/rime'
$manifestPath = Join-Path $runtime 'manifest.json'
$manifestText = [IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json
Check ($manifest.engineVersion -eq '1.17.0' -and (Get-FileHash -LiteralPath (Join-Path $runtime 'rime.dll')).Hash -eq 'D123622CEC3BC209E0FDCE5192ADBE43634223518C78575B25C6FD49B0B896A6') 'The package contains the verified official Rime 1.17.0 DLL.'
foreach ($property in $manifest.files.PSObject.Properties) { Check ((Get-FileHash -LiteralPath (Join-Path $runtime $property.Name)).Hash -eq $property.Value) ('Verified runtime asset: ' + $property.Name) }
$script:runNumber = 0
function Run([string]$Arguments, [bool]$Success = $true) {
    $script:runNumber++
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $folder 'GamePadT9.exe'; $info.Arguments = $Arguments
    $info.WorkingDirectory = $fixture; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult(); $errorOutput = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $fixture ($script:runNumber.ToString() + '.stdout.txt')),$output)
        [IO.File]::WriteAllText((Join-Path $fixture ($script:runNumber.ToString() + '.stderr.txt')),$errorOutput)
        if ($Success) {
            if ($process.ExitCode -ne 0) { throw "$Arguments failed ($($process.ExitCode)): $errorOutput" }
            return ($output | ConvertFrom-Json)
        }
        Check ($process.ExitCode -ne 0 -and $errorOutput.Contains('内置 Rime')) 'Damaged bundled assets fail explicitly instead of falling back to installed Xiaobai.'
    } finally { $process.Dispose() }
}
# A deliberately unusable legacy override must never affect the bundled mode.
[IO.File]::WriteAllText((Join-Path $folder 'runtime-location.json'),'{"install":"Z:\\missing-xiaobai","user":"Z:\\missing-user-data"}')
$first = Run '--self-test'
Check ($first.checks.Count -gt 100) 'A relocated package starts and composes using its own engine despite an invalid Xiaobai override.'
$localManifest = Get-ChildItem -LiteralPath (Join-Path $folder 'data/builtin-user/gamepad-mixed') -Filter '*.json' | Select-Object -First 1
Check ($localManifest.BaseName -eq $manifest.mixedFingerprint) 'First startup adopts the matching bundled mixed cache.'
$localPrism = @(Get-ChildItem -LiteralPath (Join-Path $folder 'data/builtin-user/gamepad-mixed') -Recurse -Filter '*.prism.bin')[0]
$cacheWrite = $localPrism.LastWriteTimeUtc
$second = Run '--self-test'
Check ((Get-Item -LiteralPath $localPrism.FullName).LastWriteTimeUtc -eq $cacheWrite) 'Subsequent startup reuses the existing local index without recompilation.'
$luaPath = Join-Path $runtime 'data/rime.lua'; $luaBytes = [IO.File]::ReadAllBytes($luaPath)
try {
    [IO.File]::WriteAllText($luaPath,'corrupt')
    Run '--self-test' $false
} finally { [IO.File]::WriteAllBytes($luaPath,$luaBytes) }
try {
    $manifest.files | Add-Member -NotePropertyName '../outside.dll' -NotePropertyValue ('0' * 64)
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Run '--self-test' $false
} finally { [IO.File]::WriteAllText($manifestPath,$manifestText) }
if ($RegenerateCache) {
    $regenerated = Run '--verify-cache'
    Check ($regenerated.passed -and $regenerated.checks.Count -ge 14) 'A missing or damaged mixed cache regenerates offline using bundled Rime 1.17.0 and the bundled dictionary.'
}
if ($EditorPath) {
    $ui = Run ('--verify-independent "' + (Get-Item -LiteralPath $EditorPath).FullName + '"')
    Check ($ui.passed -and $ui.checks.Count -ge 32) 'External target completion falls back to the clipboard with a notice when components are absent; explicit preferences and failed deliveries retain their expected behavior.'
}
$report = @{ passed=$true; checks=$checks; fixture=$fixture; selfTestChecks=$first.checks.Count }
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/independent-package-verification.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5
