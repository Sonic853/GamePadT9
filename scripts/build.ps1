param([switch]$Prepare)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Prepare) { & (Join-Path $PSScriptRoot 'prepare.ps1') }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ x64 tools are required.' }
$nativeOut = Join-Path $projectRoot 'artifacts\native'
New-Item -ItemType Directory -Force $nativeOut | Out-Null
$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
# A fixed batch file imports the MSVC environment. No destructive file operations.
$batch = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /utf-8 /EHsc /W4 /MT /O2 /LD "$projectRoot\native\Bridge.cpp" /Fo"$nativeOut\Bridge.obj" /link /DEF:"$projectRoot\native\Bridge.def" /IMPLIB:"$nativeOut\Bridge.lib" /OUT:"$nativeOut\GamePadT9.TextService.dll" user32.lib advapi32.lib ole32.lib uuid.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /utf-8 /EHsc /W4 /MT /O2 "$projectRoot\native\Control.cpp" /Fo"$nativeOut\Control.obj" /link /OUT:"$nativeOut\BridgeControl.exe"
exit /b %errorlevel%
"@
$batchPath = Join-Path $nativeOut 'build-native.cmd'
[IO.File]::WriteAllText($batchPath, $batch, [Text.Encoding]::Default)
& $env:ComSpec /d /c $batchPath
if ($LASTEXITCODE -ne 0) { throw 'Native bridge build failed.' }
dotnet build (Join-Path $projectRoot 'src\GamePadT9\GamePadT9.csproj') -c Release -o (Join-Path $projectRoot 'artifacts\app') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Copy-Item -LiteralPath (Join-Path $nativeOut 'GamePadT9.TextService.dll') -Destination (Join-Path $projectRoot 'artifacts\app')
Copy-Item -LiteralPath (Join-Path $nativeOut 'BridgeControl.exe') -Destination (Join-Path $projectRoot 'artifacts\app')
