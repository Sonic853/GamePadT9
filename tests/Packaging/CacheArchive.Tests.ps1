$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$importer = Join-Path $projectRoot 'scripts/import-mixed-cache.ps1'
$testRoot = Join-Path $projectRoot ('artifacts/cache-archive-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $checks.Add($Message)
}
function HashText([string]$Text) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '') }
    finally { $hasher.Dispose() }
}
function Fixture([string]$Path, [hashtable]$Files) {
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Files.Keys) {
            $stream = $zip.CreateEntry($name).Open()
            try { $bytes = [Text.Encoding]::UTF8.GetBytes($Files[$name]); $stream.Write($bytes, 0, $bytes.Length) }
            finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
}

# Verify the actual distributable cache, independently of an installed Rime.
$real = Join-Path $testRoot 'real'
& $importer -Archive (Join-Path $projectRoot 'data/mixed-cache.zip') -Destination $real
Check (@(Get-ChildItem -LiteralPath $real -Filter '*.json').Count -gt 0) 'The checked-in cache imports without an installed input method.'

$id = 'gamepad_t9_mixed_' + ('a' * 16)
$manifestName = ('b' * 64) + '.json'
$prefix = ('a' * 16) + '/build/'
$manifest = @{ Id = $id; SchemaHash = (HashText 'schema'); PrismHash = (HashText 'prism') } | ConvertTo-Json -Compress
$valid = @{ $manifestName = $manifest; ($prefix + $id + '.schema.yaml') = 'schema'; ($prefix + $id + '.prism.bin') = 'prism'; ($prefix + 'ready') = $id }
$extra = $valid.Clone(); $extra['source/private.yaml'] = 'private'; $extra['userdb/LOG'] = 'learned'; $extra['../escape.txt'] = 'outside'
$archive = Join-Path $testRoot 'valid.zip'; Fixture $archive $extra
$destination = Join-Path $testRoot 'valid'
& $importer -Archive $archive -Destination $destination
Check (@(Get-ChildItem -LiteralPath $destination -Recurse -File).Count -eq 4 -and !(Test-Path -LiteralPath (Join-Path $testRoot 'escape.txt'))) 'Only the manifest, schema, prism and marker are copied; unrelated archive paths and learned data are excluded.'

foreach ($case in @('schema hash', 'prism hash', 'missing prism', 'missing marker', 'wrong marker', 'invalid ID', 'invalid hash', 'missing manifest')) {
    $files = $valid.Clone()
    switch ($case) {
        'schema hash' { $files[$prefix + $id + '.schema.yaml'] = 'modified' }
        'prism hash' { $files[$prefix + $id + '.prism.bin'] = 'modified' }
        'missing prism' { $files.Remove($prefix + $id + '.prism.bin') }
        'missing marker' { $files.Remove($prefix + 'ready') }
        'wrong marker' { $files[$prefix + 'ready'] = 'wrong' }
        'invalid ID' { $files[$manifestName] = $manifest.Replace($id, '../outside') }
        'invalid hash' { $files[$manifestName] = $manifest.Replace((HashText 'schema'), 'not-a-hash') }
        'missing manifest' { $files.Remove($manifestName) }
    }
    $archive = Join-Path $testRoot ($case + '.zip'); Fixture $archive $files
    $destination = Join-Path $testRoot $case; $rejected = $false
    try { & $importer -Archive $archive -Destination $destination }
    catch { $rejected = $true }
    Check ($rejected -and !(Test-Path -LiteralPath $destination)) "Reject $case before copying any cache files."
}
$rejected = $false
try { & (Join-Path $projectRoot 'scripts/package.ps1') -SkipMixedCache -MixedCacheArchive 'unused.zip' }
catch { $rejected = $_.Exception.Message -like '*cannot be used together*' }
Check $rejected 'Conflicting cache options fail before building.'
@{ passed = $true; checks = $checks } | ConvertTo-Json -Depth 3
