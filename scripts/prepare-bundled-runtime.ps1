param([string]$SourceArchive, [string]$SevenZipPath = '7z')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$SourceArchive) { $SourceArchive = Join-Path $projectRoot 'data/bundled-source.zip' }
$stage = Join-Path $projectRoot ('artifacts/bundled-prepare-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $stage 'source'
$payload = Join-Path $stage 'runtime/rime'
$compiled = Join-Path $payload 'data/build'
$tools = Join-Path $stage 'tools'
foreach ($folder in @($source,$compiled,$tools)) { New-Item -ItemType Directory -Force $folder | Out-Null }
# Source-only input: a reviewed public data snapshot, never a user's deployed directory.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory((Get-Item -LiteralPath $SourceArchive).FullName, $source)
$download = Join-Path $projectRoot '.cache/rime-33e7814-Windows-msvc-x86.7z'
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($download)) | Out-Null
if (!(Test-Path -LiteralPath $download)) { Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/rime/librime/releases/download/1.17.0/rime-33e7814-Windows-msvc-x86.7z' -OutFile $download }
if ((Get-FileHash -LiteralPath $download).Hash -ne 'AF235C26C06152CE09CEB8FE9D9AB9FBA7AB43CE30AA952B40174D806F5CC3D9') { throw 'Official Rime archive checksum mismatch.' }
& $SevenZipPath x $download ('-o' + $tools) dist/lib/rime.dll dist/bin/rime_deployer.exe -y -bso0
if ($LASTEXITCODE -ne 0) { throw 'Rime extraction failed.' }
Copy-Item -LiteralPath (Join-Path $tools 'dist/lib/rime.dll') -Destination (Join-Path $tools 'dist/bin/rime.dll')
Copy-Item -LiteralPath (Join-Path $tools 'dist/lib/rime.dll') -Destination (Join-Path $payload 'rime.dll')
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = Join-Path $tools 'dist/bin/rime_deployer.exe'
$info.Arguments = '--build "' + $source + '" "' + $source + '" "' + $compiled + '"'
$info.WorkingDirectory = $stage; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
$compile = [Diagnostics.Process]::Start($info)
try {
    $stdout = $compile.StandardOutput.ReadToEndAsync(); $stderr = $compile.StandardError.ReadToEndAsync()
    $compile.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $stage 'deploy.stdout.txt'),$stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $stage 'deploy.stderr.txt'),$stderr.GetAwaiter().GetResult())
    if ($compile.ExitCode -ne 0) { throw "Rime deployment failed ($($compile.ExitCode)); see $stage/deploy.stderr.txt" }
} finally { $compile.Dispose() }
# Only compiled input data is needed at runtime. No installer, UI skins or user databases.
foreach ($item in Get-ChildItem -LiteralPath $compiled -File) {
    if ($item.Name -notmatch '^(default\.yaml|(xiaobai_simp|stroke)\.(schema\.yaml|prism\.bin|table\.bin|reverse\.bin))$') { throw "Unexpected compiled file: $($item.Name)" }
}
Copy-Item -LiteralPath (Join-Path $source 'rime.lua') -Destination (Join-Path $payload 'data/rime.lua')
Copy-Item -LiteralPath (Join-Path $source 'opencc') -Destination (Join-Path $payload 'data/opencc') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'data/bundled-licenses') -Destination (Join-Path $payload 'licenses') -Recurse
$hashes = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName) {
    $name = $file.FullName.Substring($payload.Length+1).Replace('\','/')
    $hashes[$name] = (Get-FileHash -LiteralPath $file.FullName).Hash
}
$manifest = [ordered]@{ format=1; engineVersion='1.17.0'; schema='xiaobai_simp'; files=$hashes }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'manifest.json') -Encoding UTF8
dotnet build (Join-Path $projectRoot 'src/GamePadT9/GamePadT9.csproj') -c Release -o (Join-Path $tools 'host') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Cache compiler build failed.' }
$info.FileName = Join-Path $tools 'host/GamePadT9.exe'
$info.Arguments = '--build-bundled-cache "' + $stage + '" "' + (Join-Path $stage 'cache/mixed') + '"'
$compile = [Diagnostics.Process]::Start($info)
try {
    $stdout = $compile.StandardOutput.ReadToEndAsync(); $stderr = $compile.StandardError.ReadToEndAsync()
    $compile.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $stage 'mixed.stdout.txt'),$stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $stage 'mixed.stderr.txt'),$stderr.GetAwaiter().GetResult())
    if ($compile.ExitCode -ne 0) { throw "Bundled mixed-cache generation failed; see $stage/mixed.stderr.txt" }
} finally { $compile.Dispose() }
$cacheManifest = @(Get-ChildItem -LiteralPath (Join-Path $stage 'cache/mixed') -Filter '*.json')
if ($cacheManifest.Count -ne 1) { throw 'Expected one matching cache.' }
$manifest.mixedFingerprint = $cacheManifest[0].BaseName
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'manifest.json') -Encoding UTF8
foreach ($pair in @(@($payload,'bundled-runtime.zip'),@((Join-Path $stage 'cache/mixed'),'bundled-mixed-cache.zip'))) {
    $archive = Join-Path $stage $pair[1]
    Push-Location -LiteralPath $pair[0]
    try {
        & $SevenZipPath a -tzip -mm=Deflate -mx=9 -mfb=258 -mpass=15 -mmt=off -mtc=off -mta=off -bd -bso0 $archive '*'
        if ($LASTEXITCODE -ne 0) { throw 'Runtime/cache compression failed.' }
        & $SevenZipPath t -bd -bso0 $archive
        if ($LASTEXITCODE -ne 0) { throw 'Archive verification failed.' }
    } finally { Pop-Location }
    Copy-Item -LiteralPath $archive -Destination (Join-Path $projectRoot ('data/' + $pair[1])) -Force
}
Write-Output "Prepared independent runtime and matching cache: $stage"
