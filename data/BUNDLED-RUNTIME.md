# 独立便携包的内置 Rime 数据

独立版固定使用 **Rime 1.17.0**（2026-09-10 核对的官方最新稳定版），来自 [官方发行页](https://github.com/rime/librime/releases/tag/1.17.0) 的 `rime-33e7814-Windows-msvc-x86.7z`。使用原版 DLL，不修改小白安装目录。DLL 只依赖 Windows 的 `dbghelp.dll`、`KERNEL32.dll`、`USER32.dll`，已内置相关引擎库与 Lua、octagram、predict 模块，无需另装 VC++ 运行库。

| 文件 | 用途 |
|---|---|
| `bundled-runtime.zip` | 引擎、公开发行数据编译的词库、方案、Lua、OpenCC 数据、许可证及完整 SHA256 清单 |
| `bundled-mixed-cache.zip` | 与上述编译方案和词库匹配的混合拼音索引 |
| `bundled-source.zip` | 精确的方案、字词库、Lua 和 OpenCC 源数据，用于复现及分发对应源码，不进入便携运行包 |
| `bundled-licenses/` | 引擎、插件、词库的来源和许可声明 |

以上 ZIP 均使用 Git LFS；检出时需获取实体文件。来源和散列详见 [SOURCES.md](bundled-licenses/SOURCES.md)。字词库来源为小白 T9 2026.08.04 的公开发行数据，保留其万象词库和笔画数据署名。没有读取 AppData、个人词频、用户自定义短语、私有配置或注册备份；两个 `.custom.yaml` 是发行版原始默认配置。发行版的 `essay.txt` 同时以其字典引用的 `essay-zh-hans.txt` 名称提供，保证全新目录可离线部署。

日常打包只需：

```powershell
./scripts/package.ps1 -Independent
# 原版仍保持原有行为，不包含上述引擎与词库：
./scripts/package.ps1 -MixedCacheArchive ./data/mixed-cache.zip
```

构建时导入器会先校验所有文件、PE 架构及缓存对应关系，再写入发行目录。`-RuntimeArchive` 只允许与 `-Independent` 同用；`-MixedCacheArchive` 可指定匹配的索引归档。`-SkipMixedCache` 可省略索引，运行时从内置编译词库离线重建。

更新引擎或数据时，先核对官方最新稳定版本、下载校验值、依赖和许可证，更新 `prepare-bundled-runtime.ps1` 与来源说明，然后运行：

```powershell
./scripts/prepare-bundled-runtime.ps1
./tests/Packaging/BundledRuntimeArchive.Tests.ps1
./scripts/package.ps1 -Independent
$zip = (Get-Content artifacts/portable-independent-archive-path.txt -Raw).Trim()
./tests/Packaging/Independent.Tests.ps1 -Archive $zip -RegenerateCache
```

准备脚本在新的 `artifacts/bundled-prepare-*` 中解压源数据、部署词库，并使用同版本引擎生成缓存；只有指定的运行文件和索引被压缩到 `data`。源码归档独立提供，便携运行包不附带原始词库文本。普通 UI 修改无需重新生成这些归档。

独立运行时只读取 `runtime/rime/manifest.json` 中校验通过的文件，个人数据写入 `data/builtin-user`；原版继续检测本机小白并使用原有数据目录，两者不会覆盖彼此的学习数据。
