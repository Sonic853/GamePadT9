param([ValidateSet('x64','x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ tools are required.' }
$out = Join-Path $projectRoot "artifacts\input-method-native-$Architecture"
New-Item -ItemType Directory -Force $out | Out-Null
$vcvars = Join-Path $vsPath ('VC\Auxiliary\Build\' + $(if ($Architecture -eq 'x64') { 'vcvars64.bat' } else { 'vcvars32.bat' }))
$source = Join-Path $projectRoot 'native\InputMethodControl.cpp'
$batch = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /utf-8 /EHsc /W4 /MT /O2 /LD "$source" /Fo"$out\Hook.obj" /link /OUT:"$out\InputMethodHook.$Architecture.dll" /IMPLIB:"$out\Hook.lib" user32.lib ole32.lib uuid.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /utf-8 /EHsc /W4 /MT /O2 /DINPUTMETHOD_HOST "$source" /Fo"$out\Control.obj" /link /OUT:"$out\InputMethodControl.exe" user32.lib ole32.lib uuid.lib
exit /b %errorlevel%
"@
$batchPath = Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($batchPath, $batch, [Text.Encoding]::Default)
& $env:ComSpec /d /c $batchPath
if ($LASTEXITCODE -ne 0) { throw 'Input method helper build failed.' }
$checksums = (Get-FileHash -LiteralPath (Join-Path $out "InputMethodHook.$Architecture.dll")).Hash + (Get-FileHash -LiteralPath (Join-Path $out 'InputMethodControl.exe')).Hash
$hasher = [Security.Cryptography.SHA256]::Create()
try { $hash = ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($checksums)))).Replace('-','').Substring(0,16) }
finally { $hasher.Dispose() }
$release = Join-Path $projectRoot "artifacts\input-method\$Architecture-$hash"
New-Item -ItemType Directory -Force $release | Out-Null
foreach ($name in @("InputMethodHook.$Architecture.dll",'InputMethodControl.exe')) {
    $destination = Join-Path $release $name
    if (!(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath (Join-Path $out $name) -Destination $destination }
    if ((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath (Join-Path $out $name)).Hash) { throw 'Input method component checksum mismatch.' }
}
[IO.File]::WriteAllText((Join-Path $projectRoot "artifacts\input-method-$Architecture-path.txt"), $release)
