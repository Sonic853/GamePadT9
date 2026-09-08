param([ValidateSet('x64','x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ tools are required.' }
$out = Join-Path $projectRoot "artifacts\proxy-native-$Architecture"
New-Item -ItemType Directory -Force $out | Out-Null
$vcvars = Join-Path $vsPath ('VC\Auxiliary\Build\' + $(if ($Architecture -eq 'x64') { 'vcvars64.bat' } else { 'vcvars32.bat' }))
$batch = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /utf-8 /EHsc /W4 /MT /O2 /LD "$projectRoot\native\XiaobaiProxy.cpp" /Fo"$out\Proxy.obj" /link /DEF:"$projectRoot\native\XiaobaiProxy.def" /OUT:"$out\GamePadT9.Xiaobai.dll" /IMPLIB:"$out\Proxy.lib" user32.lib ole32.lib oleaut32.lib uuid.lib advapi32.lib
exit /b %errorlevel%
"@
$batchPath = Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($batchPath, $batch, [Text.Encoding]::Default)
& $env:ComSpec /d /c $batchPath
if ($LASTEXITCODE -ne 0) { throw "Proxy $Architecture build failed." }
$sha = (Get-FileHash -LiteralPath (Join-Path $out 'GamePadT9.Xiaobai.dll')).Hash
$release = Join-Path $projectRoot ("artifacts\proxy\$Architecture-" + $sha.Substring(0,16))
New-Item -ItemType Directory -Force $release | Out-Null
Copy-Item -LiteralPath (Join-Path $out 'GamePadT9.Xiaobai.dll') -Destination $release -Force
[IO.File]::WriteAllText((Join-Path $projectRoot "artifacts\proxy-$Architecture-path.txt"), $release)
