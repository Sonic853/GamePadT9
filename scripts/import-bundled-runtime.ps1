param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = (Get-Item -LiteralPath $Archive).FullName
$destinationPath = [IO.Path]::GetFullPath($Destination)
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        if ($entries.ContainsKey($entry.FullName)) { throw 'Duplicate bundled-runtime archive entry.' }
        $entries[$entry.FullName] = $entry
    }
    $entry = $entries['manifest.json']
    if (!$entry) { throw 'Bundled-runtime manifest missing; download the complete LFS archive.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.format -ne 1 -or $manifest.schema -cne 'xiaobai_simp' -or !$manifest.engineVersion -or !$manifest.files) { throw 'Invalid bundled-runtime manifest.' }
    $selected = @{ 'manifest.json' = $entry }
    foreach ($file in $manifest.files.PSObject.Properties) {
        $name = $file.Name
        if ($name -cnotmatch '^(rime\.dll|data/(rime\.lua|build/[a-z0-9_.]+|opencc/[A-Za-z0-9_.]+)|licenses/[A-Za-z0-9_.-]+)$' -or $name.Contains('..') -or $file.Value -notmatch '^[a-f0-9]{64}$') { throw "Invalid bundled-runtime path or hash: $name" }
        $entry = $entries[$name]
        if (!$entry -or !$entry.Length) { throw "Missing bundled-runtime file: $name" }
        $stream = $entry.Open(); $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '') }
        finally { $hasher.Dispose(); $stream.Dispose() }
        if ($hash -ne $file.Value) { throw "Bundled-runtime checksum mismatch: $name" }
        $selected[$name] = $entry
    }
    foreach ($required in @('rime.dll','data/build/default.yaml','data/build/xiaobai_simp.schema.yaml','data/build/xiaobai_simp.table.bin','data/rime.lua')) {
        if (!$selected.ContainsKey($required)) { throw "Bundled-runtime manifest missing required file: $required" }
    }
    # Check the PE architecture before writing/loading native code.
    $reader = [IO.BinaryReader]::new($entries['rime.dll'].Open())
    try {
        $bytes = $reader.ReadBytes([int]$entries['rime.dll'].Length)
        if ($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes,0) -ne 0x5a4d) { throw 'Invalid Rime PE file.' }
        $offset = [BitConverter]::ToInt32($bytes,60)
        if ($offset -lt 64 -or $offset -gt $bytes.Length-6 -or [BitConverter]::ToUInt32($bytes,$offset) -ne 0x4550 -or [BitConverter]::ToUInt16($bytes,$offset+4) -ne 0x14c) { throw 'Bundled Rime must be x86.' }
    } finally { $reader.Dispose() }
    # Entire payload verified before writing; unrelated entries are never copied.
    foreach ($name in $selected.Keys) {
        $target = Join-Path $destinationPath $name
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($selected[$name], $target, $false)
    }
    Write-Output "Imported Rime $($manifest.engineVersion), $($manifest.schema) and $($selected.Count - 1) verified files."
} finally { $zip.Dispose() }
