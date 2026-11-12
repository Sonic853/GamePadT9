# GamePad T9 SDL build

`win-x86/SDL3.dll` is a modified SDL 3.4.20 build, not the upstream binary.
The zlib license in `LICENSE.txt` is unchanged. The patch is confined to
`src/joystick/hidapi/SDL_hidapi_steam.c` and `src/joystick/SDL_gamepad.c`.

The upstream 2015 Steam Controller driver already decodes both pads but does
not expose touchpads through the SDL gamepad API. `steam-controller-2015.patch`
exposes left/right pad contact, unrotated coordinates, and separate clicks.
It preserves USB/wireless packet multiplexing and BLE partial-update handling.
The old right-stick emulation remains available to other SDL callers. GamePad
T9 ignores that emulation and reads the actual pad, including finger release.
The C# host applies the user's angle, rather than the driver's legacy fixed
15-degree mouse-axis rotation and 1000-unit offset.

Both pad indices contain one finger: 0 = left, 1 = right. Clicks map to
`SDL_GAMEPAD_BUTTON_TOUCHPAD` (left) and `SDL_GAMEPAD_BUTTON_MISC2` (right).
The physical stick, triggers, face buttons, grips and other controller drivers
retain their upstream decoding. No Windows driver is installed.

To reproduce with Visual C++ x86 tools, Windows SDK, Git and CMake available:

```powershell
./scripts/build-sdl.ps1
```

The script downloads the official [SDL 3.4.20 source ZIP](https://github.com/libsdl-org/SDL/releases/download/release-3.4.20/SDL3-3.4.20.zip),
verifies SHA256 `9850235FA6C9E99631DBE6F30DD620E4FBA2E9C879B551A8C479E505E9E3F617`,
applies the checked-in patch in a fresh directory under `artifacts`, and builds
Release / Win32 with `SDL_LIBC=OFF`. This avoids a separate MSVC runtime dependency.
Use `-SourceArchive <zip>` for an offline build or `-PrepareOnly` to verify just
the source and patch. `build-info.json` records the source, patch and DLL hashes;
different compiler versions may produce a different DLL hash.

The normal build and GitHub Actions use the checked-in Git LFS DLL; they do
not need to rebuild SDL or download its source. Single-file publishing bundles
the same DLL, and includes this note and the patch under `licenses/`.
