param([string]$SteamRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destinationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\GamePadT9\Assets\Steam'))
if (!$SteamRoot) { $SteamRoot = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam').SteamPath }
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $SteamRoot 'controller_base\images\api'))
if (!(Test-Path -LiteralPath $sourceRoot -PathType Container)) { throw "Steam glyph directory missing: $sourceRoot" }

# Read Steam's own model-to-glyph mapping, rather than guessing from filename prefixes.
$mappingFile = $null
foreach ($candidate in Get-ChildItem -LiteralPath (Join-Path $SteamRoot 'steamui') -Filter '*.js' -File) {
    $candidateText = [IO.File]::ReadAllText($candidate.FullName)
    if ($candidateText.Contains('/steaminputglyphs/sc_l1.svg') -and $candidateText.Contains('/steaminputglyphs/ps4_button_share.svg')) {
        $mappingFile = $candidate; $mappingText = $candidateText; break
    }
}
if (!$mappingFile) { throw 'Steam UI glyph mapping not found. Review the current Steam client before importing.' }
$maps = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($match in [regex]::Matches($mappingText, '\[o\.([A-Za-z0-9_$]+)\]:\[(.*?)\](?=,\[|\})')) {
    $names = @([regex]::Matches($match.Groups[2].Value, '/steaminputglyphs/([^"/]+)\.svg') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if ($names.Count) { $maps[$match.Groups[1].Value] = $names }
}
$modelKeys = @{}
foreach ($match in [regex]::Matches($mappingText, '\[s\.([A-Za-z0-9_$]+)\]:"(controller_[^"]+)"')) {
    $key = $match.Groups[1].Value; $model = $match.Groups[2].Value
    if ($maps.ContainsKey($key)) { $modelKeys[$model] = $key }
}
$stickAndGyro = '^shared_(?:[lr]stick|[lr]3$|gyro)'
$psFace = '^ps_(?:(?:color_|outlined_|color_outlined_)?button_(?:x|circle|square|triangle)$|dpad)'
$nintendoFace = '^shared_(?:buttons_[ensw]$|(?:(?:color_|outlined_|color_outlined_)?button_[abxy]$)|gyro)'
$categories = @(
    @{ Path = 'Shared'; Label = '跨手柄共用'; Models = @(); Extra = '^shared_|^qam_' },
    @{ Path = 'Xbox'; Label = 'Xbox 360 / One / Series / Elite'; Models = @('controller_xboxone','controller_xboxelite'); Extra = '^xbox|^shared_(?:(?:color_|outlined_|color_outlined_)?button_[abxy]$|dpad|[lr]stick|[lr]3$)' },
    @{ Path = 'PlayStation/PS3'; Label = 'PS3 / DualShock 3（Steam 共用映射）'; Models = @('controller_ps3'); Extra = "$psFace|$stickAndGyro|^ps4_button_logo$" },
    @{ Path = 'PlayStation/PS4'; Label = 'PS4 / DualShock 4'; Models = @('controller_ps4'); Extra = "^ps4_|$psFace|$stickAndGyro" },
    @{ Path = 'PlayStation/PS5'; Label = 'PS5 / DualSense / DualSense Edge'; Models = @('controller_ps5','controller_ps5_edge'); Extra = "^ps5_|^ps_|^ps4_button_logo$|$stickAndGyro" },
    @{ Path = 'Nintendo/SwitchPro'; Label = 'Nintendo Switch Pro'; Models = @('controller_switch_pro'); Extra = "^switchpro_|$nintendoFace" },
    @{ Path = 'Nintendo/Switch2Pro'; Label = 'Nintendo Switch 2 Pro'; Models = @('controller_switch2_pro'); Extra = "^switchpro_|$nintendoFace" },
    @{ Path = 'Nintendo/JoyConLeft'; Label = 'Nintendo Joy-Con 左手柄'; Models = @('controller_switch_joycon_left'); Extra = "$nintendoFace|^switchpro_button_(?:home|capture)$|^joyconpair_left_" },
    @{ Path = 'Nintendo/JoyConRight'; Label = 'Nintendo Joy-Con 右手柄'; Models = @('controller_switch_joycon_right'); Extra = "$nintendoFace|^switchpro_button_home$|^joyconpair_right_" },
    @{ Path = 'Nintendo/JoyConPair'; Label = 'Nintendo Joy-Con 双手柄'; Models = @('controller_switch_joycon_pair'); Extra = "^joyconpair_|^switchpro_|$nintendoFace" },
    @{ Path = 'SteamController/Current'; Label = 'Steam Controller（Triton）'; Models = @('controller_steamcontroller_triton'); Extra = '^sc_[lr](?:1|2(?:_half)?|4|5)$|^sd_|^sc_button_steam$|^qam_' },
    @{ Path = 'SteamController/2015'; Label = 'Steam Controller 2015（Gordon）'; Models = @('controller_steamcontroller_gordon'); Extra = '^sc_(?![lr](?:1|2(?:_half)?|4|5)$)|^shared_(?:(?:color_|outlined_|color_outlined_)?button_[abxy]$|[lr]stick|gyro)' }
)
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object { $_.Extension -eq '.svg' } | Sort-Object FullName)
$sourceInfo = @{}
foreach ($file in $sourceFiles) {
    # Preserve SVG names such as sc_lg (left grip).
    $stem = $file.BaseName
    $stem = $stem -replace '-\d+$', ''
    $relative = $file.FullName.Substring($sourceRoot.Length + 1).Replace('\','/')
    $theme = ($relative -split '/')[0]
    if ($theme -notin @('light','dark','knockout')) { throw "Unrecognized Steam theme: $relative" }
    $format = 'svg'
    $sourceInfo[$relative] = @{ File = $file; Stem = $stem; Theme = $theme; Format = $format }
}
# Validate every requested model and create a complete copy plan before writing assets.
$plan = [Collections.Generic.List[object]]::new()
$summaries = [Collections.Generic.List[object]]::new()
foreach ($category in $categories) {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($model in $category.Models) {
        if (!$modelKeys.ContainsKey($model)) { throw "Steam mapping unavailable for $model; no model substitution will be made." }
        foreach ($name in $maps[$modelKeys[$model]]) { $null = $names.Add($name) }
    }
    $missing = @($names | Where-Object { $name = $_; !($sourceInfo.Values | Where-Object { $_.Stem -eq $name } | Select-Object -First 1) })
    if ($missing.Count) { throw "Steam glyph files missing for $($category.Path): $($missing -join ', ')" }
    $count = 0; $stems = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($relative in $sourceInfo.Keys | Sort-Object) {
        $info = $sourceInfo[$relative]
        if (!$names.Contains($info.Stem) -and $info.Stem -notmatch $category.Extra) { continue }
        $path = $category.Path + '/' + $info.Theme + '/' + $info.Format + '/' + $info.File.Name
        $absolute = [IO.Path]::GetFullPath((Join-Path $destinationRoot $path))
        if (!$absolute.StartsWith($destinationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid output path: $absolute" }
        $plan.Add([pscustomobject]@{ path = $path; source = $relative; category = $category.Path })
        $null = $stems.Add($info.Stem); $count++
    }
    $summaries.Add([pscustomobject]@{ path = $category.Path; label = $category.Label; models = @($category.Models); mappingKeys = @($category.Models | ForEach-Object { $modelKeys[$_] }); glyphs = $stems.Count; files = $count })
}
# All files in the relevant Steam filename families must be represented, including shared art.
$expected = @($sourceInfo.Keys | Where-Object { $sourceInfo[$_].File.Name -match '^(shared_|qam_|xbox|ps_|ps4_|ps5_|switchpro_|joyconpair_|sc_|sd_)' })
$plannedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($item in $plan) { $null = $plannedSources.Add($item.source) }
foreach ($relative in $expected) { if (!$plannedSources.Contains($relative)) { throw "Unclassified source glyph: $relative" } }

$hashes = @{}
foreach ($relative in $plannedSources) { $hashes[$relative] = (Get-FileHash -LiteralPath $sourceInfo[$relative].File.FullName -Algorithm SHA256).Hash }
New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
$manifestFiles = [Collections.Generic.List[object]]::new()
foreach ($item in $plan) {
    $destination = Join-Path $destinationRoot $item.path
    $directory = [IO.Path]::GetDirectoryName($destination)
    if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hashes[$item.source]) { throw "Existing asset differs; left unchanged: $destination" }
    } else { Copy-Item -LiteralPath $sourceInfo[$item.source].File.FullName -Destination $destination }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hashes[$item.source]) { throw "Copy verification failed: $destination" }
    $manifestFiles.Add([pscustomobject]@{ path = $item.path; source = $item.source; sha256 = $hashes[$item.source] })
}
$manifest = [ordered]@{
    version = 1; importedAt = [DateTimeOffset]::Now.ToString('o'); sourceRoot = $sourceRoot
    mappingSource = @{ path = 'steamui/' + $mappingFile.Name; sha256 = (Get-FileHash -LiteralPath $mappingFile.FullName -Algorithm SHA256).Hash }
    uniqueSourceFiles = $plannedSources.Count; copiedFiles = $plan.Count; verifiedSha256 = $true
    categories = @($summaries.ToArray()); files = @($manifestFiles.ToArray())
}
[IO.File]::WriteAllText((Join-Path $destinationRoot 'manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$table = @('| 分类目录 | 手柄 | 图形名称数 | 文件数 |', '|---|---|---:|---:|')
foreach ($summary in $summaries) { $table += "| [$($summary.path)]($($summary.path)/) | $($summary.label) | $($summary.glyphs) | $($summary.files) |" }
$readme = @'
# Steam 手柄按键图标

从本机 Steam 的 `controller_base/images/api` 原样复制，仅收集 SVG，按手柄系列和主题分类。原始文件名和 SVG 内容均未修改。详细来源路径与 SHA256 见 [manifest.json](manifest.json)。

## 目录

'@ + "`n" + ($table -join "`n") + "`n`n" + @'
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
'@
[IO.File]::WriteAllText((Join-Path $destinationRoot 'README.md'), $readme, [Text.UTF8Encoding]::new($false))
$summaries | Format-Table path,glyphs,files -AutoSize
Write-Output "Imported $($plan.Count) files from $($plannedSources.Count) unique Steam source files. All SHA256 checks passed."
