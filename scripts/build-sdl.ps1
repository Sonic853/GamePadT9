param([string]$SourceArchive, [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = '3.4.20'
$sourceHash = '9850235FA6C9E99631DBE6F30DD620E4FBA2E9C879B551A8C479E505E9E3F617'
if (!$SourceArchive) {
    $SourceArchive = Join-Path $projectRoot "artifacts/SDL3-$version-source.zip"
    if (!(Test-Path -LiteralPath $SourceArchive)) {
        New-Item -ItemType Directory -Force (Split-Path $SourceArchive) | Out-Null
        & curl.exe -L --fail --silent --show-error "https://github.com/libsdl-org/SDL/releases/download/release-$version/SDL3-$version.zip" -o $SourceArchive
        if ($LASTEXITCODE -ne 0) { throw 'SDL source download failed.' }
    }
}
if ((Get-FileHash -LiteralPath $SourceArchive -Algorithm SHA256).Hash -ne $sourceHash) { throw 'SDL source archive checksum mismatch.' }
$stage = Join-Path $projectRoot ('artifacts/sdl-custom-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $SourceArchive -DestinationPath $stage
$source = Join-Path $stage "SDL3-$version"
$patch = Join-Path $projectRoot 'third_party/SDL3/steam-controller-2015.patch'
& git -C $source apply --no-index --check $patch
if ($LASTEXITCODE -ne 0) { throw 'SDL patch does not match the pinned source.' }
& git -C $source apply --no-index $patch
if ($LASTEXITCODE -ne 0) { throw 'SDL patch failed.' }
$driver = Join-Path $source 'src/joystick/hidapi/SDL_hidapi_steam.c'
if (!(Select-String -LiteralPath $driver -SimpleMatch 'SDL_PrivateJoystickAddTouchpad(joystick, 1);' -Quiet)) { throw 'SDL touchpad patch was not applied.' }
if ($PrepareOnly) { Write-Output "Verified patched source: $source"; return }
$build = Join-Path $stage 'build'
& cmake -S $source -B $build -A Win32 -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF -DSDL_LIBC=OFF
if ($LASTEXITCODE -ne 0) { throw 'SDL configuration failed. Install Visual C++ x86 tools and CMake.' }
& cmake --build $build --config Release --target SDL3-shared --parallel 8
if ($LASTEXITCODE -ne 0) { throw 'SDL build failed.' }
$destination = Join-Path $projectRoot 'third_party/SDL3/win-x86/SDL3.dll'
Copy-Item -LiteralPath (Join-Path $build 'Release/SDL3.dll') -Destination $destination
$info = [ordered]@{ version=$version; variant='GamePadT9-SteamController2015-1'; architecture='x86'; sourceSha256=$sourceHash; patchSha256=(Get-FileHash -LiteralPath $patch).Hash; dllSha256=(Get-FileHash -LiteralPath $destination).Hash; libc=$false }
$info | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'third_party/SDL3/build-info.json') -Encoding UTF8
Write-Output "Updated private SDL library: $destination"
