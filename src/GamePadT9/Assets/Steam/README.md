# Steam 手柄按键图标

从本机 Steam 的 `controller_base/images/api` 原样复制，仅收集 SVG，按手柄系列和主题分类。原始文件名和 SVG 内容均未修改。详细来源路径与 SHA256 见 [manifest.json](manifest.json)。

## 目录

| 分类目录 | 手柄 | 图形名称数 | 文件数 |
|---|---|---:|---:|
| [Shared](Shared/) | 跨手柄共用 | 71 | 217 |
| [Xbox](Xbox/) | Xbox 360 / One / Series / Elite | 53 | 163 |
| [PlayStation/PS3](PlayStation/PS3/) | PS3 / DualShock 3（Steam 共用映射） | 50 | 150 |
| [PlayStation/PS4](PlayStation/PS4/) | PS4 / DualShock 4 | 74 | 222 |
| [PlayStation/PS5](PlayStation/PS5/) | PS5 / DualSense / DualSense Edge | 79 | 237 |
| [Nintendo/SwitchPro](Nintendo/SwitchPro/) | Nintendo Switch Pro | 56 | 170 |
| [Nintendo/Switch2Pro](Nintendo/Switch2Pro/) | Nintendo Switch 2 Pro | 56 | 170 |
| [Nintendo/JoyConLeft](Nintendo/JoyConLeft/) | Nintendo Joy-Con 左手柄 | 39 | 119 |
| [Nintendo/JoyConRight](Nintendo/JoyConRight/) | Nintendo Joy-Con 右手柄 | 38 | 116 |
| [Nintendo/JoyConPair](Nintendo/JoyConPair/) | Nintendo Joy-Con 双手柄 | 60 | 182 |
| [SteamController/Current](SteamController/Current/) | Steam Controller（Triton） | 68 | 208 |
| [SteamController/2015](SteamController/2015/) | Steam Controller 2015（Gordon） | 64 | 197 |

每个分类采用以下结构：

```text
手柄分类/
  light/          # Steam light 主题，通常适合深色界面
    svg/          # 矢量原图
  dark/           # Steam dark 主题
  knockout/       # Steam knockout 主题
```

仅复制 Steam 实际提供的 SVG 主题，不生成缺失变体；例如部分 Joy-Con SL / SR 仅有 knockout 版本。带 `color`、`outlined`、`soft`、`half`、`click`、`touch`、方向和数字后缀的变体均保留。图形名称数合并了文件名末尾的数字副本后缀。

## 共用与型号说明

- `Shared` 保留完整跨手柄共用素材，包括面键、方向键、摇杆、陀螺仪和触摸等图标。各型号目录也包含其映射实际引用的共用图标，因此同一源文件可能出现在多个分类。
- PS3 没有独立 `ps3_*` 源文件。`PlayStation/PS3` 按 Steam 客户端 PS3 映射收集 `ps_*`、`ps4_*` 和 `shared_*` 图标，并保留这些原名；其中菜单键也遵循 Steam 现有共用映射，不伪造 PS3 专属图标。
- PS5 目录包含 DualSense Edge 的额外肩键和 Fn 图标；Xbox 目录包含 360、One / Series 与 Elite 背键。
- 任天堂按 Steam 的 Switch Pro、Switch 2 Pro、左右 Joy-Con 和双 Joy-Con 分类。两代 Pro 的部分映射和源文件相同；`switchpro_button_c`、`switchpro_gl`、`switchpro_gr` 等新变体完整保留。当前本机图标库未提供独立 Wii、Wii U、GameCube 图标集。
- `SteamController/Current` 为 Triton，`SteamController/2015` 为 Gordon，依据 Steam 客户端各自的映射区分。Triton 使用 `sc_l1` / `sc_r1` 等新图标，也复用 `sd_*` 的菜单、触控板和握持图标；完整 `sd_*` 家族一并放入 Current 以保留这些共用素材。

## 与主程序的关系

`ButtonGlyphs` 通过 SVG.NET 绘制分类目录中的原始 SVG，主界面与设置窗口共用这套绘制逻辑。`../../SteamGlyphs.props` 只选择当前界面所需的 34 个 SVG 嵌入程序；完整素材库不会全部打包。PNG 按键图标已清理，导入脚本也只收集 SVG。分类库不改变程序的输入映射或设备支持范围。

重新收集同一客户端版本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/import-steam-glyphs.ps1
```

在项目根目录运行；也可传入 `-SteamRoot`。脚本校验实际映射、相关源文件覆盖率和每一份复制文件的 SHA256；遇到不同内容的已有分类文件会停止并保留该文件，不覆盖自定义修改，不删除任何文件。Steam 客户端升级导致映射格式变化时，脚本会报错，需先检查新格式。

图标来源：[Steamworks 图标说明](https://partner.steamgames.com/doc/features/steam_controller/getting_started_for_devs#3.3)。权利说明见项目根目录的 [THIRD-PARTY-NOTICES.md](../../../../THIRD-PARTY-NOTICES.md)。