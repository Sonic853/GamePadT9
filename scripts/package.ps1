param([switch]$SkipNative)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$SkipNative) {
    foreach ($architecture in @('x64','x86')) {
        & (Join-Path $PSScriptRoot 'build-standalone.ps1') -Architecture $architecture
        & (Join-Path $PSScriptRoot 'build-proxy.ps1') -Architecture $architecture
        & (Join-Path $PSScriptRoot 'build-input-method.ps1') -Architecture $architecture
    }
}
# A new staging folder avoids exporting any earlier machine's settings or learned data.
$stage = Join-Path $projectRoot ('artifacts\package-' + [Guid]::NewGuid().ToString('N'))
$folder = Join-Path $stage 'GamePadT9-Portable'
New-Item -ItemType Directory -Force $folder | Out-Null
dotnet publish (Join-Path $projectRoot 'src\GamePadT9\GamePadT9.csproj') -c Release -r win-x86 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $folder --nologo
if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed.' }
$hashes = [ordered]@{}
foreach ($architecture in @('x64','x86')) {
    foreach ($kind in @('standalone','proxy','input-method')) {
        $source = (Get-Content -LiteralPath (Join-Path $projectRoot "artifacts\$kind-$architecture-path.txt") -Raw).Trim()
        $destination = Join-Path $folder "components\$kind\$architecture"
        New-Item -ItemType Directory -Force $destination | Out-Null
        $names = if ($kind -eq 'standalone') { @('GamePadT9.TextService.dll','BridgeControl.exe') }
            elseif ($kind -eq 'proxy') { @('GamePadT9.Xiaobai.dll') }
            else { @('InputMethodControl.exe',"InputMethodHook.$architecture.dll") }
        foreach ($name in $names) {
            $file = Join-Path $destination $name
            Copy-Item -LiteralPath (Join-Path $source $name) -Destination $file
            $hashes["components/$kind/$architecture/$name"] = (Get-FileHash -LiteralPath $file).Hash
        }
    }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'components\sha256.json') -Encoding UTF8
'{"format":1,"runtime":"auto-detect-installed-xiaobai","platform":"Windows x64; x86 host","dotnet":"Microsoft.WindowsDesktop.App 10.0 (x86)","selfContained":false,"singleFileHost":true}' | Set-Content -LiteralPath (Join-Path $folder 'portable.json') -Encoding UTF8
[IO.File]::WriteAllText((Join-Path $folder 'Start.cmd'), "@echo off`r`nstart `"GamePad T9`" `"%~dp0GamePadT9.exe`"`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $folder 'Settings.cmd'), "@echo off`r`nstart `"GamePad T9`" `"%~dp0GamePadT9.exe`" --settings`r`n", [Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $projectRoot 'PORTABLE.md') -Destination (Join-Path $folder 'README.md')
if (Test-Path -LiteralPath (Join-Path $projectRoot 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $folder }
# This is an allowlisted package, never a copy of artifacts/ or the developer workspace.
$dist = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist ('GamePadT9-Portable-Windows-x64-NoRuntime-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
Compress-Archive -LiteralPath $folder -DestinationPath $zip -CompressionLevel Optimal
$sha = (Get-FileHash -LiteralPath $zip).Hash
[IO.File]::WriteAllText($zip + '.sha256', $sha + '  ' + [IO.Path]::GetFileName($zip) + "`r`n")
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\portable-package-path.txt'), $folder)
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\portable-archive-path.txt'), $zip)
Write-Output "Portable folder: $folder"
Write-Output "Portable archive: $zip"
Write-Output "SHA256: $sha"
