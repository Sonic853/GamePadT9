$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $projectRoot 'native\Backdrop.cpp'
$definition = Join-Path $projectRoot 'native\Backdrop.def'
$out = Join-Path $projectRoot 'artifacts\backdrop-native-x86'
$dll = Join-Path $out 'GamePadT9.Backdrop.dll'
if ((Test-Path -LiteralPath $dll) -and (Get-Item -LiteralPath $dll).LastWriteTimeUtc -gt (Get-Item -LiteralPath $source).LastWriteTimeUtc -and (Get-Item -LiteralPath $dll).LastWriteTimeUtc -gt (Get-Item -LiteralPath $definition).LastWriteTimeUtc -and (Get-Item -LiteralPath $dll).LastWriteTimeUtc -gt (Get-Item -LiteralPath $PSCommandPath).LastWriteTimeUtc) { return }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ tools are required for the backdrop component.' }
New-Item -ItemType Directory -Force $out | Out-Null
$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars32.bat'
$batch = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /utf-8 /EHsc /W4 /MT /O2 /LD "$source" /Fo"$out\Backdrop.obj" /link /DEF:"$definition" /OUT:"$dll" /IMPLIB:"$out\Backdrop.lib" windowsapp.lib CoreMessaging.lib dwmapi.lib user32.lib ole32.lib d2d1.lib dxguid.lib
exit /b %errorlevel%
"@
$batchPath = Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($batchPath, $batch, [Text.Encoding]::Default)
& $env:ComSpec /d /c $batchPath
if ($LASTEXITCODE -ne 0) { throw 'Backdrop build failed.' }
