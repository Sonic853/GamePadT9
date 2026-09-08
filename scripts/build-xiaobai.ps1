param([ValidateSet('x64','x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$originalRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\xiaobai-t9'))
$deps = Join-Path $projectRoot 'artifacts\deps'
$boost = Join-Path $deps 'boost_1_84_0'
New-Item -ItemType Directory -Force $deps | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $boost 'boost\version.hpp'))) {
    $archive = Join-Path $deps 'boost_1_84_0.tar.bz2'
    if (!(Test-Path -LiteralPath $archive)) {
        curl.exe -fsSL --retry 2 -o $archive 'https://archives.boost.io/release/1.84.0/source/boost_1_84_0.tar.bz2'
        if ($LASTEXITCODE -ne 0) { throw 'Boost download failed.' }
    }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne 'cc4b893acf645c9d4b698e9a0f08ca8846aa5d6c68275c14c3e7949c24109454') { throw 'Boost SHA256 mismatch.' }
    tar.exe -xf $archive -C $deps
    if ($LASTEXITCODE -ne 0) { throw 'Boost extraction failed.' }
}
$sourceRoot = Join-Path $projectRoot "artifacts\xiaobai-source-$Architecture"
$buildOut = Join-Path $projectRoot "artifacts\xiaobai-native-$Architecture"
New-Item -ItemType Directory -Force $sourceRoot,$buildOut | Out-Null
foreach ($folder in @('WeaselTSF','WeaselIPC','WeaselUI','include','resource')) {
    $destination = Join-Path $sourceRoot $folder
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $originalRoot $folder) | Copy-Item -Destination $destination -Recurse -Force
}
function Edit-Staged([string]$relative, [scriptblock]$edit) {
    $path = Join-Path $sourceRoot $relative
    $value = [IO.File]::ReadAllText($path)
    $changed = & $edit $value
    if ($value -eq $changed) { throw "Source patch did not match: $relative" }
    [IO.File]::WriteAllText($path, $changed, [Text.UTF8Encoding]::new($false))
}
Edit-Staged 'WeaselTSF\WeaselTSF.h' { param($s)
    $s.Replace(' private:', " private:`n  void* _gamePadBridge = nullptr;`n  bool _gamePadServerCompatible = false;`n  void _InitGamePadBridge();`n  void _UninitGamePadBridge();`n  bool _IsGamePadNumericKey(WPARAM key);")
}
Edit-Staged 'WeaselTSF\WeaselTSF.cpp' { param($s)
    $s.Replace('STDAPI WeaselTSF::Deactivate() {', "STDAPI WeaselTSF::Deactivate() {`n  _UninitGamePadBridge();`n  _UninitThreadFocusSink();").Replace("  _EnsureServerConnected();", "  _EnsureServerConnected();`n  _InitGamePadBridge();").Replace('bool ok = m_client.GetResponseData(std::ref(parser));', "bool ok = m_client.GetResponseData(std::ref(parser));`n  _gamePadServerCompatible = ok;")
}
Edit-Staged 'WeaselIPC\WeaselClientImpl.cpp' { param($s)
    $s.Replace('return channel.HandleResponseData(handler);', "try { return channel.HandleResponseData(handler); }`n  catch (...) { OutputDebugStringW(L`"GamePadT9: incompatible xiaobai IPC response.`"); return false; }")
}
Edit-Staged 'WeaselTSF\KeyEventSink.cpp' { param($s)
    # Bypass only our tagged numeric strokes, before the original response parser runs.
    # Otherwise its cached response could be consumed a second time.
    $pattern = '(STDAPI WeaselTSF::On(?:Test)?Key(?:Down|Up)\([\s\S]*?BOOL\* pfEaten\) \{)'
    if ([regex]::Matches($s, $pattern).Count -ne 4) { throw 'Expected four TSF key callbacks.' }
    [regex]::Replace($s, $pattern, '$1' + "`n  if (_IsGamePadNumericKey(wParam)) {`n    _fTestKeyDownPending = _fTestKeyUpPending = FALSE;`n    *pfEaten = FALSE; return S_OK;`n  }")
}
Edit-Staged 'WeaselTSF\WeaselTSF.rc' { param($s)
    "#define VERSION_MAJOR 2026`n#define VERSION_MINOR 8`n#define VERSION_PATCH 4`n#define PRODUCT_VERSION 2026.8.4.1`n#define FILE_VERSION `"2026.8.4.1`"`n" + $s.Replace('afxres.h','winresrc.h')
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsPath) { throw 'Visual C++ tools are required.' }
$vcvars = Join-Path $vsPath ('VC\Auxiliary\Build\' + $(if ($Architecture -eq 'x64') { 'vcvars64.bat' } else { 'vcvars32.bat' }))
function Quoted([string]$path) { '"' + $path.Replace('\','/') + '"' }
$flags = @('/nologo','/std:c++17','/utf-8','/EHsc','/W3','/MT','/O2','/MP4','/c','/Y-','/DUNICODE','/D_UNICODE','/DNDEBUG','/DBOOST_ALL_NO_LIB','/DVERSION_MAJOR=2026','/DVERSION_MINOR=8','/DVERSION_PATCH=4',('/I' + (Quoted (Join-Path $sourceRoot 'include'))),('/I' + (Quoted $boost)),('/I' + (Quoted (Join-Path $sourceRoot 'WeaselTSF'))))
$flagsPath = Join-Path $buildOut 'compiler.rsp'
[IO.File]::WriteAllLines($flagsPath, $flags, [Text.UTF8Encoding]::new($false))
$batchLines = [Collections.Generic.List[string]]::new()
$batchLines.Add('@echo off')
$batchLines.Add(('call "{0}" >nul' -f $vcvars))
$batchLines.Add('if errorlevel 1 exit /b 1')
$objects = [Collections.Generic.List[string]]::new()
$groups = @('WeaselIPC','WeaselUI','WeaselTSF','Serialization','Integration')
foreach ($group in $groups) {
    $objectDir = Join-Path $buildOut $group
    New-Item -ItemType Directory -Force $objectDir | Out-Null
    $files = if ($group -eq 'Serialization') { Get-ChildItem -LiteralPath (Join-Path $boost 'libs\serialization\src') -Filter '*.cpp' }
        elseif ($group -eq 'Integration') { Get-Item -LiteralPath (Join-Path $projectRoot 'native\xiaobai\Integration.cpp') }
        else { Get-ChildItem -LiteralPath (Join-Path $sourceRoot $group) -Filter '*.cpp' | Where-Object Name -ne 'stdafx.cpp' }
    $sourceList = Join-Path $buildOut "$group.rsp"
    [IO.File]::WriteAllLines($sourceList, @($files | ForEach-Object { Quoted $_.FullName }), [Text.UTF8Encoding]::new($false))
    foreach ($file in $files) { $objects.Add((Quoted (Join-Path $objectDir ($file.BaseName + '.obj')))) }
    $objectFlag = '/Fo' + (Quoted ($objectDir + '/'))
    $batchLines.Add(('cl @"{0}" {1} @"{2}"' -f $flagsPath,$objectFlag,$sourceList))
    $batchLines.Add('if errorlevel 1 exit /b 1')
}
$res = Join-Path $buildOut 'weasel.res'
$batchLines.Add(('pushd "{0}"' -f (Join-Path $sourceRoot 'WeaselTSF')))
$batchLines.Add(('rc /nologo /c65001 /i "{0}" /fo "{1}" WeaselTSF.rc' -f (Join-Path $sourceRoot 'include'),$res))
$batchLines.Add('if errorlevel 1 exit /b 1')
$batchLines.Add('popd')
$dll = Join-Path $buildOut 'weasel-gamepad.dll'
$link = @('/nologo','/DLL',('/DEF:' + (Quoted (Join-Path $sourceRoot 'WeaselTSF\WeaselTSF.def'))),('/OUT:' + (Quoted $dll)),('/IMPLIB:' + (Quoted (Join-Path $buildOut 'weasel-gamepad.lib'))),('/MACHINE:' + $(if ($Architecture -eq 'x64') { 'X64' } else { 'X86' })),(Quoted $res)) + $objects.ToArray() + @('user32.lib','gdi32.lib','advapi32.lib','ole32.lib','oleaut32.lib','uuid.lib','shell32.lib','shlwapi.lib','usp10.lib','comdlg32.lib','winspool.lib')
$linkPath = Join-Path $buildOut 'link.rsp'
[IO.File]::WriteAllLines($linkPath, $link, [Text.UTF8Encoding]::new($false))
$batchLines.Add(('link @"{0}"' -f $linkPath))
$batchLines.Add('exit /b %errorlevel%')
$batch = Join-Path $buildOut 'build.cmd'
[IO.File]::WriteAllLines($batch, $batchLines, [Text.Encoding]::Default)
& $env:ComSpec /d /c $batch
if ($LASTEXITCODE -ne 0) { throw "xiaobai $Architecture build failed." }
$hash = (Get-FileHash -LiteralPath $dll).Hash.Substring(0,16)
$release = Join-Path $projectRoot "artifacts\xiaobai\$Architecture-$hash"
New-Item -ItemType Directory -Force $release | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $release 'weasel-gamepad.dll')
[IO.File]::WriteAllText((Join-Path $projectRoot "artifacts\xiaobai-$Architecture-path.txt"), $release)
Write-Output "Built xiaobai $Architecture bridge: $release"
