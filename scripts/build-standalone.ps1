param([ValidateSet('x64','x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ tools are required.' }
$out = Join-Path $projectRoot "artifacts\standalone-native-$Architecture"
New-Item -ItemType Directory -Force $out | Out-Null
$vcvars = Join-Path $vsPath ('VC\Auxiliary\Build\' + $(if ($Architecture -eq 'x64') { 'vcvars64.bat' } else { 'vcvars32.bat' }))
$batch = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /utf-8 /EHsc /W4 /MT /O2 /LD "$projectRoot\native\Bridge.cpp" /Fo"$out\Bridge.obj" /link /DEF:"$projectRoot\native\Bridge.def" /OUT:"$out\GamePadT9.TextService.dll" /IMPLIB:"$out\Bridge.lib" user32.lib ole32.lib uuid.lib advapi32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /utf-8 /EHsc /W4 /MT /O2 "$projectRoot\native\Control.cpp" /Fo"$out\Control.obj" /link /OUT:"$out\BridgeControl.exe"
exit /b %errorlevel%
"@
$batchPath = Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($batchPath, $batch, [Text.Encoding]::Default)
& $env:ComSpec /d /c $batchPath
if ($LASTEXITCODE -ne 0) { throw "Standalone $Architecture build failed." }
$dll = Join-Path $out 'GamePadT9.TextService.dll'
$sha = (Get-FileHash -LiteralPath $dll).Hash
$release = Join-Path $projectRoot ("artifacts\standalone\$Architecture-" + $sha.Substring(0,16))
New-Item -ItemType Directory -Force $release | Out-Null
foreach ($name in @('GamePadT9.TextService.dll','BridgeControl.exe')) {
    $destination = Join-Path $release $name
    if (!(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath (Join-Path $out $name) -Destination $destination }
    if ((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath (Join-Path $out $name)).Hash) { throw 'Release checksum mismatch.' }
}
[IO.File]::WriteAllText((Join-Path $projectRoot "artifacts\standalone-$Architecture-path.txt"), $release)
Write-Output "Built standalone ${Architecture}: $release"
