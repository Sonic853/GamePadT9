# Third-party components

## Rime and bundled input data (independent edition only)

The independent edition includes the official unmodified x86 Rime **1.17.0** DLL,
the latest stable release verified on 2026-11-12, and input data compiled from
Xiaobai T9's public distribution. The installed-Xiaobai portable edition does
not bundle these files. See [the exact sources, hashes and component licenses](data/bundled-licenses/SOURCES.md)
and [reproduction instructions](data/BUNDLED-RUNTIME.md). The independent ZIP
contains these notices in `runtime/rime/licenses`; corresponding dictionary
sources are provided separately as `data/bundled-source.zip` and a CI artifact.

## WPF UI 4.3.0

The NuGet packages `WPF-UI` and `WPF-UI.Abstractions` provide the Fluent configuration windows, cards, controls, themes and interface icons. They are bundled into the portable application's executable without the .NET runtime.

Source: https://github.com/lepoco/wpfui/tree/4.3.0

WPF UI is distributed under the MIT license. Its original license and bundled third-party notices are preserved in `third_party/WPF-UI` and copied to `licenses/WPF-UI.md` and `licenses/WPF-UI-ThirdPartyNotices.txt`. Steam controller art continues to use the original SVG resources; the WPF controls render these at their actual display DPI into an in-memory surface, without additional PNG assets.

## Steam controller glyphs

The application embeds 60 unmodified SVG originals selected by `src/GamePadT9/SteamGlyphs.props` from the classified folders in `src/GamePadT9/Assets/Steam`, including both generations of Steam Controller and the 2015 pad/grip glyphs. PNG button glyphs, including the former root-level copies, have been removed; the import script collects SVG files only. The subdirectories contain the complete locally available Xbox, PS3 / PS4 / PS5, Nintendo Switch / Switch 2 / Joy-Con and Steam Controller / Steam Controller 2015 glyph collection, including shared art, all supplied light / dark / knockout themes, SVG originals. These files were copied without modification from the locally installed Steam client's `controller_base/images/api` on 2026-09-08.

The collection is classified using the Steam client's own model-to-glyph mappings, including PS3's reuse of PS / PS4 art and the newer Steam Controller's reuse of Steam Deck (`sd_*`) art. Shared source files are also copied into the applicable model folders. See [the asset catalog](src/GamePadT9/Assets/Steam/README.md) and [manifest.json](src/GamePadT9/Assets/Steam/manifest.json) for category counts, source paths, mapping provenance and verified SHA256 hashes. `scripts/import-steam-glyphs.ps1` reproduces the import against this Steam client version. Only the selected SVG files are embedded; the complete archive is not bundled with the application.

Source and usage documentation: https://partner.steamgames.com/doc/features/steam_controller/getting_started_for_devs#3.3

Valve also provides official glyph renders at https://steamcdn-a.akamaihd.net/steam/partner/controller/SteamControllerGlyphs_v1.zip . This project uses the installed client's newer glyph set, including PS5 Create / Options.

Steam artwork remains the property of its respective rights holders; this project's code license does not relicense that artwork. No Steam SDK, Steam API initialization or running Steam client is required by GamePad T9.

## SVG.NET 3.4.8

The NuGet package `Svg` renders embedded Steam SVG paths into the WinForms graphics surface at the requested display size. Parsed documents are cached; the application does not resize fixed-resolution PNG glyphs.

Source: https://github.com/svg-net/SVG/tree/9d564f47c82780df4b703ee0bf2436c6a2276f5c

SVG.NET is distributed under the Microsoft Public License (MS-PL). The original license is in `third_party/SVG.NET/LICENSE.txt` and is copied into application output as `licenses/SVG.NET.txt`.

## ExCSS 4.3.2

ExCSS is the CSS parser dependency of SVG.NET, explicitly pinned to the reviewed stable version rather than SVG.NET's minimum dependency. Source: https://github.com/TylerBrinks/ExCSS/tree/0a75db4bfffe9dc26555fd8b9ca152f6f9a2760f

ExCSS is distributed under the MIT license. The original license is in `third_party/ExCSS/LICENSE.txt` and is copied into application output as `licenses/ExCSS.txt`.

## SDL 3.4.20

Official release: https://github.com/libsdl-org/SDL/releases/tag/release-3.4.20

`third_party/SDL3/win-x86/SDL3.dll` is a **GamePad T9 modified x86 build of SDL 3.4.20**, built from the official `SDL3-3.4.20.zip` source. The changes expose both Steam Controller 2015 pads' unrotated coordinates, contact and click states through the gamepad API; other controller decoding, including Triton, is retained. The C# host calls only the joystick/gamepad APIs. This is an application library, not an installed Windows driver. SDL is distributed under the zlib license; the full original license is in `third_party/SDL3/LICENSE.txt` and is copied into application output as `licenses/SDL3.txt`. Reproduction instructions, source/DLL hashes and the patch are in `third_party/SDL3/BUILD.md`, `build-info.json` and `steam-controller-2015.patch`, also distributed under `licenses/`.

SHA256 of the source archive: `9850235FA6C9E99631DBE6F30DD620E4FBA2E9C879B551A8C479E505E9E3F617`.

SHA256 of the included x86 DLL: `43A547300AA6D8E3E383FF90207E88D13BCE66FCC662D9AE13682920792080AC`.
