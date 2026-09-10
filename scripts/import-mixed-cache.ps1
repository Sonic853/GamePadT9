param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = (Get-Item -LiteralPath $Archive -ErrorAction Stop).FullName
$destinationPath = [IO.Path]::GetFullPath($Destination)
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        if ($entries.ContainsKey($entry.FullName)) { throw "Duplicate cache archive entry: $($entry.FullName)" }
        $entries[$entry.FullName] = $entry
    }
    $manifests = @($zip.Entries | Where-Object { $_.FullName -cmatch '^[a-f0-9]{64}\.json$' })
    if (!$manifests.Count) { throw 'The archive contains no mixed-cache manifests.' }
    $selected = @{}
    foreach ($manifestEntry in $manifests) {
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        if ($manifest.Id -cnotmatch '^gamepad_t9_mixed_[a-f0-9]{16}$' -or
            $manifest.SchemaHash -notmatch '^[a-f0-9]{64}$' -or $manifest.PrismHash -notmatch '^[a-f0-9]{64}$') {
            throw "Invalid mixed-cache manifest: $($manifestEntry.FullName)"
        }
        $prefix = $manifest.Id.Substring('gamepad_t9_mixed_'.Length) + '/build/'
        $files = @{
            ($prefix + $manifest.Id + '.schema.yaml') = $manifest.SchemaHash
            ($prefix + $manifest.Id + '.prism.bin') = $manifest.PrismHash
        }
        foreach ($name in $files.Keys) {
            $entry = $entries[$name]
            if (!$entry -or $entry.Length -eq 0) { throw "Missing compiled cache file: $name" }
            $stream = $entry.Open(); $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '') }
            finally { $hasher.Dispose(); $stream.Dispose() }
            if ($hash -ne $files[$name]) { throw "Mixed-cache checksum mismatch: $name" }
            $selected[$name] = $entry
        }
        $ready = $entries[$prefix + 'ready']
        if (!$ready) { throw "Missing mixed-cache completion marker: $($manifest.Id)" }
        $reader = [IO.StreamReader]::new($ready.Open())
        try { if ($reader.ReadToEnd().Trim() -cne $manifest.Id) { throw 'Invalid mixed-cache completion marker.' } }
        finally { $reader.Dispose() }
        $selected[$ready.FullName] = $ready
        $selected[$manifestEntry.FullName] = $manifestEntry
    }
    # Copy only validated cache files, never source schemas, learned data or
    # arbitrary archive paths. Validate the entire archive before writing.
    foreach ($name in $selected.Keys) {
        $file = Join-Path $destinationPath $name
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($file)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($selected[$name], $file, $false)
    }
    Write-Output "Imported $($manifests.Count) verified mixed-input cache(s)."
} finally { $zip.Dispose() }
