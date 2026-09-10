$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$importer = Join-Path $root 'scripts/import-bundled-runtime.ps1'
$fixture = Join-Path $root ('artifacts/runtime-archive-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $fixture | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Ok, [string]$Message) { if (!$Ok) { throw $Message }; $checks.Add($Message) }
& $importer -Archive (Join-Path $root 'data/bundled-runtime.zip') -Destination (Join-Path $fixture 'real')
Check (Test-Path -LiteralPath (Join-Path $fixture 'real/rime.dll')) 'The reviewed runtime imports without an installed input method.'
$conflict = $false
try { & (Join-Path $root 'scripts/package.ps1') -RuntimeArchive 'unused.zip' } catch { $conflict = $_.Exception.Message -like '*requires -Independent*' }
Check $conflict 'A runtime archive cannot silently change the legacy package type.'
# Small invalid fixtures exercise validation before any native loading or extraction.
foreach ($case in @('missing manifest','path traversal','missing engine','invalid hash','missing payload','wrong architecture')) {
    $destination = Join-Path $fixture $case; $archive = $destination + '.zip'
    $files = [ordered]@{ 'rime.dll' = ('0' * 64); 'data/build/default.yaml' = ('0' * 64); 'data/build/xiaobai_simp.schema.yaml' = ('0' * 64); 'data/build/xiaobai_simp.table.bin' = ('0' * 64); 'data/rime.lua' = ('0' * 64) }
    $payloads = @{}
    foreach ($name in $files.Keys) {
        $bytes = [Text.Encoding]::UTF8.GetBytes('fixture')
        if ($name -eq 'rime.dll') {
            $bytes = New-Object byte[] 128; $bytes[0]=0x4d; $bytes[1]=0x5a; $bytes[60]=64; $bytes[64]=0x50; $bytes[65]=0x45; $bytes[68]=0x4c; $bytes[69]=1
            if ($case -eq 'wrong architecture') { $bytes[68]=0x64; $bytes[69]=0x86 }
        }
        $payloads[$name] = $bytes
    }
    foreach ($name in @($files.Keys)) {
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $files[$name] = ([BitConverter]::ToString($hasher.ComputeHash($payloads[$name]))).Replace('-','') } finally { $hasher.Dispose() }
    }
    switch ($case) {
        'path traversal' { $files['../outside.dll'] = '0' * 64 }
        'missing engine' { $files.Remove('rime.dll') }
        'invalid hash' { $files['data/rime.lua'] = 'bad' }
        'missing payload' { $payloads.Remove('data/rime.lua') }
    }
    $zip = [IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
    try {
        if ($case -ne 'missing manifest') { $payloads['manifest.json'] = [Text.Encoding]::UTF8.GetBytes((@{format=1;engineVersion='1.17.0';schema='xiaobai_simp';files=$files} | ConvertTo-Json -Depth 5)) }
        foreach ($name in $payloads.Keys) { $stream=$zip.CreateEntry($name).Open(); try { $stream.Write($payloads[$name],0,$payloads[$name].Length) } finally { $stream.Dispose() } }
    } finally { $zip.Dispose() }
    $rejected = $false
    try { & $importer -Archive $archive -Destination $destination } catch { $rejected = $true }
    Check ($rejected -and !(Test-Path -LiteralPath $destination)) "Reject $case before writing runtime files."
}
@{passed=$true;checks=$checks} | ConvertTo-Json -Depth 4
