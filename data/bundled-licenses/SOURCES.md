# Bundled Rime runtime and input data

GamePad T9 uses the unmodified x86 library from the official Rime 1.17.0 release,
verified as the latest stable release on 2026-09-10:
https://github.com/rime/librime/releases/tag/1.17.0

Archive: rime-33e7814-Windows-msvc-x86.7z
SHA256: AF235C26C06152CE09CEB8FE9D9AB9FBA7AB43CE30AA952B40174D806F5CC3D9
rime.dll SHA256: D123622CEC3BC209E0FDCE5192ADBE43634223518C78575B25C6FD49B0B896A6
The DLL contains the Lua, octagram and predict modules listed in Rime-version-info.txt.
The module sources are https://github.com/hchunhui/librime-lua/tree/ec52e48,
https://github.com/lotem/librime-octagram/tree/dfcc151 and
https://github.com/rime/librime-predict/tree/920bd41.
Core and dependency notices are included in this directory; the DLL is not
represented as a BSD-only aggregate. The octagram component is GPLv3 and KenLM
is LGPLv2.1. Rime's release build configuration and dependency acquisition are
available in the upstream source at commit 33e7814.

The compiled xiaobai_simp and stroke input data are built in a fresh directory
from the public data shipped with Xiaobai T9 2026.08.04. No files from AppData,
user databases, private customizations, skins or input-method installers are used.
The only .custom.yaml files are the vendor's original distribution defaults.
The date/time Lua script comes from Xiaobai's output/rime.lua at
https://github.com/HuanSoft-Open-Source-Community/xiaobai-t9/tree/06798b07f61e385f1805bac83202595fc61994d3
(GPLv3; see Xiaobai-GPL.txt).

Dictionary attribution is retained in the corresponding source files. The
dictionary identifies amzxyz's RIME-LMDG / Rime Wanxiang project as its source:
https://github.com/amzxyz/rime-wanxiang (see Wanxiang-CC-BY.txt). Xiaobai supplies
the adapted spelling data, T9 rules, additional vocabulary and symbol tables.
The stroke source retains its CNS11643 and contributor credits; OpenCC data
retain the OpenCC Apache 2.0 notice. GamePad T9 compiles these data and derives
an additional mixed letter/group spelling index without changing word entries.

Exact corresponding schema, dictionary, Lua and OpenCC source data are provided
as data/bundled-source.zip in the GamePad T9 source repository and as a separate
CI artifact beside the independent portable ZIP. They are not needed to run.
Source repository: https://github.com/Sonic853/GamePadT9
Reproduction: scripts/prepare-bundled-runtime.ps1; see data/BUNDLED-RUNTIME.md.
The vendor's essay.txt is also supplied under the essay-zh-hans.txt name
referenced by its dictionary, so both preset-vocabulary references resolve
offline during a clean build. This alias is the only source-data adaptation.
Source archive SHA256: 12E7030D892135C6015692064E758B8DCF31F4A745AE0D3A4B4DCDFB76EFDC8F
