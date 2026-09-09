# GamePad T9

<img width="780" height="520" alt="image" src="https://github.com/user-attachments/assets/4588694a-ac49-4ee1-9f1f-b43cd26c3a17" />

Windows Xbox、PS4（DualShock 4）和 PS5（DualSense）手柄九键输入程序。启动 C# 主程序，在目标文本框同时按下两枚菜单键（Xbox：**View + Menu**；PS4：**Share + Options**；PS5：**Create + Options**）。直接输入模式记录原输入法并优先切换到 **GamePad T9**；该条目不可用时切换到 **小白 T9 输入法**。关闭手柄输入后恢复原输入法。也可按程序配置失焦输入或外部输入框。

主程序、手柄控制、九宫格和候选面板使用 **C#**。小白联动扩展通过标准 **C++ TSF 接口**加载本机原版小白 DLL，沿用其名称和标识，不依赖固定版本的内部 IPC。项目也提供“GamePad T9”独立输入法条目；两种方式互斥，均支持 x64 和 x86 目标程序。

## 复制到其他电脑

运行 `scripts/package.ps1` 生成 `dist/GamePadT9-Portable-Windows-x64-NoRuntime-<时间>.zip`，包含主程序、SDL、SVG 图标及两种架构的输入组件，**不包含 .NET 运行时**。目标电脑需安装 [.NET 10 桌面运行时（Windows x86）](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)，再解压运行 `Start.cmd`，无需安装 SDK 或开发工具。主程序为 32 位，仅安装 x64 运行时不满足要求。便携包自动检测本机的小白安装目录，不带开发电脑的绝对路径、引擎、词库或注册表备份；未识别时可选择目录。

从 **设置 → 输入法组件** 点击“注册/卸载 GamePad T9 输入法”或“注入/还原小白 T9 输入”。已有一种方式时另一种的安装按钮禁用；先还原/卸载才能切换。详细要求、目录和使用方法见 [便携版说明](PORTABLE.md)。版本兼容取决于标准 TSF 接口、x86 Rime API 和 `xiaobai_simp` 方案；不承诺所有未来版本。

发行包的主程序使用不含运行时的单文件发布，`GamePadT9.dll`、`Svg.dll`、`ExCSS.dll`、`SDL3.dll`、`GamePadT9.Backdrop.dll`、`.deps.json` 和 `.runtimeconfig.json` 均整合到 `GamePadT9.exe`。SDL3 和背景模糊组件启动时自动释放到用户临时缓存；`components` 中供 Windows 和目标进程加载的输入法组件保持独立，分发时仍需复制整个文件夹。开发构建保留独立 DLL，`scripts/package.ps1` 负责合并发行产物。[.NET 单文件发布说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## 开始使用

设置、程序列表与外部输入框采用 [WPF UI 4.3.0](https://github.com/lepoco/wpfui/tree/4.3.0) 的 Fluent 窗口和控件：设置侧栏分为“手柄与输入”“面板外观”“输入法组件”，外观页提供可见度数字、滑块和实时预览；程序列表支持搜索、全局默认、每个 EXE 的独立配置，以及从运行中的窗口添加程序。输入方式和完成操作仍为下拉单选。保存后生效，取消或 Esc 保留原有配置；输入法组件操作仍立即生效且相互排斥。

WPF UI 及其 Abstractions 依赖也随发行包整合进主程序 EXE，继续使用系统安装的 .NET 10 桌面运行时 x86。界面预览：[手柄设置](artifacts/settings-window.png)、[面板外观](artifacts/settings-appearance-window.png)、[程序列表](artifacts/program-profiles-window.png)。

全局默认使用“外部输入框”，完成后填入目标程序；选词和编辑先在外部输入栏中完成，再按 View + Menu 或长按 A 提交。以下步骤说明可选的“无屏蔽操作”直接输入模式，可在下方的“程序列表”中切换。

1. 双击 [Start.cmd](Start.cmd)。程序在系统托盘运行，初始关闭手柄输入。
2. 打开目标程序，将光标放入文本框，保持原先使用的输入法即可。
3. 同时按两枚菜单键开启手柄输入，自动记录并切换输入法，也可以双击托盘图标。有多只手柄时，先在托盘的 **设置… → 已连接手柄** 选择设备并保存。
4. 置顶九宫格和候选面板出现后，用右摇杆选区，按 **RT / R3**（PS：**R2 / R3**）输入字母组，按 **A**（PS：**×**）确认候选。面板使用所选设备对应的 Steam 按键图标。

需安装至少一种输入组件。本机当前使用小白联动入口，备用独立条目已于 2026-09-08 取消注册。**更新输入组件前已打开的目标程序可能需要重新打开**，才能加载新组件；不用重新添加输入法。自动切换作用于实际获得键盘焦点的编辑控件线程，支持新版记事本中主窗口与编辑区分属不同线程的情况，不使用整个桌面范围的激活。

面板不抢文本框焦点，标题区域可拖动，九宫格和候选也可点击。再次按 View + Menu、长按 B、面板右上角 ×、托盘关闭输入、手柄断开或正常退出程序，都会关闭输入并恢复先前的输入法。短按 B 只关闭候选，不恢复输入法。重连手柄后需再次开启。

启用期间切换程序或同一窗口内的焦点控件时，会按实际输入线程记录并切换输入法；关闭时分别恢复。返回已访问的线程会重新确认切换及组件就绪，但不会覆盖首次记录的原输入法。原先已经选择 GamePad T9 时，关闭后仍保持 GamePad T9。

首次开启时，只有输入法切换成功、焦点仍匹配且该编辑线程的输入组件就绪，才显示面板。记录、切换或就绪检查失败时，程序会尝试恢复并在托盘提示原因。请确认光标位于可编辑区域，并先完成或取消实体键盘正在输入的拼音。

## 程序列表与输入方式

从托盘菜单 **程序列表…** 或 **设置 → 程序列表…** 打开。也可以运行 `artifacts/app/GamePadT9.exe --programs`。左侧固定包含“全局配置”，支持浏览 EXE 或从运行中的程序添加独立配置。按程序完整路径匹配（忽略大小写），独立配置优先；移除独立配置后恢复继承全局。程序更新改变 EXE 路径时需要重新添加。

每个配置的“输入方式”使用单选下拉框：

| 选项 | 行为 |
|---|---|
| **无屏蔽操作** | 保留原来的不抢焦点面板，直接向目标输入。 |
| **游戏失去焦点** | 只显示九宫格和候选面板，由该面板获得焦点，不显示额外输入窗口；确认候选或输入数字后，松开按键与扳机即可切回原文本框填入，无需摇杆回中，收到提交回执后再让输入面板获得焦点。删除已上屏文字也经过切焦流程。 |
| **使用外部输入框** | 全局默认值。面板上方显示可编辑输入栏并获得焦点；选词、数字、退格均在输入栏中完成。完成时再统一处理整段文字。 |

只有选择“使用外部输入框”时，才启用“外部输入框完成操作”下拉框：

| 完成操作（只能选择一个） | 行为 |
|---|---|
| **完成输入后复制到剪切板** | 复制整段文字并关闭输入、返回原程序，不自动填入。此模式不要求目标提供 TSF 文本接口，也不切换目标的输入法。 |
| **完成输入后填入至目标程序** | 恢复原文本框焦点，切换所需输入法，通过现有 TSF 通道提交整段文字，然后恢复原输入法。不额外发送 Enter。 |

外部输入框中，**短按 A（PS：×）在松开时选词；长按 1 秒或再次按 View + Menu 完成输入并关闭**（PS4：Share + Options；PS5：Create + Options）。完成时按配置复制到剪贴板或填入原目标程序。若仍有候选，会先确认当前高亮候选；有剩余编码时继续选词，不丢弃未完成的部分。长按不会再触发一次短按，返回目标前等待按键释放。窗口也提供带 Steam SVG 图标的完成按钮，托盘的“完成并关闭输入”执行同一流程。

外部输入框使用紧凑的 Fluent 窗口，置于九宫格上方，按所在显示器 DPI 定位。窗口不显示标题栏，也不使用显示/隐藏淡入淡出动画；默认高度为 112 DIP。文本框固定为一行高度，长文本随光标滚动，仍保留换行内容；下方将目标程序、完成方式、状态和字数合并到一行，底部紧凑排列“复制”“清空”“保留并关闭”和完成按钮。确认键图标跟随 Xbox / PS4 / PS5 手柄切换；鼠标点击完成按钮也可提交。Enter 在草稿中换行，关闭按钮保留草稿并执行焦点与输入法恢复。等待提交期间禁用编辑、复制、清空和重复完成操作。目标路径读取支持 x86 / x64 程序，目标名称与独立配置使用同一识别方式。

短按 B 关闭候选并保留输入栏；长按 B、点击“保留并关闭”或关闭窗口会保留草稿。View + Menu 提交成功后清空已完成草稿；填入失败、超出长度限制或目标焦点变化时，保留文字与输入窗口供处理。提交过程中重复按组合键不会取消或重复提交。草稿按程序暂存在本次主程序进程内，重开输入可继续，退出主程序后不保留。可使用“复制保留的文字”另行保存，或用“清空草稿”明确放弃。提交结果不确定时停止重复提交；先核对目标，复制或清空保留的文字后继续。

“游戏失去焦点”模式提交失败时，文字仍会保留；右键九宫格面板可选择“复制保留的文字”或“清空草稿”，不会弹出外部输入窗口。

失焦模式的候选文字、符号和数字提交允许左右摇杆保持偏转；按键和扳机仍需松开。关闭输入、删除已上屏文字及外部输入框完成操作仍等待摇杆回中。

失焦模式和外部输入框的“填入至目标程序”会在打开输入时记录原输入法。关闭时先返回原目标、隐藏输入界面，再恢复并核对原输入法，避免切焦覆盖恢复结果；组合键关闭、长按 B、外部输入完成和手柄断开都使用同一恢复流程。恢复失败会通过托盘提示并保留记录供重试。“复制到剪切板”仍不切换目标输入法。

配置保存在 `program-profiles.json`，与手柄及可见度设置分开保存。默认全局为“使用外部输入框”，完成操作默认为“填入至目标程序”；已有全局和独立程序配置仍按保存值加载。配置在开启输入时确定，本轮目标不会随输入窗口获得焦点而改变。手动切到其他窗口时暂停输入，不自动向新窗口填字。

**失焦不是驱动级屏蔽**：允许后台手柄输入的游戏仍可能响应，开启组合键和切焦瞬间也可能被游戏读取。独占全屏游戏可能最小化或关闭聊天框；窗口化、无边框及原文本框能恢复焦点的程序更适合使用。输入栏保持实体 PS / Xbox 连接，不创建虚拟手柄。

前台切换首先使用 Windows 窗口 API；必要时，复用现有短时目标线程辅助组件，让仍处于前台的目标进程调用 `AllowSetForegroundWindow`。只授予本程序，不改变全局焦点策略；辅助组件不拦截手柄 API。目标拒绝辅助组件或前台切换时，面板提示点击输入窗口后继续。填入前重新核对原窗口、可识别的焦点控件及 TSF 通道；失败保留文字。

单次自动填入沿用 TSF 接口的 **1024 个 UTF-16 单元**上限，超过时保留草稿供缩短或复制，不分块冒险重复提交。模拟数字或兼容 Backspace 仍依赖目标实际处理按键；逐次切焦模式不提供对任意游戏的输入保证。

截图：[程序列表](artifacts/program-profiles-window.png)、[外部输入栏（Xbox）](artifacts/external-input-window.png)、[外部输入栏（PS5）](artifacts/external-input-ps5-window.png)。

## 备用输入法条目

“GamePad T9”注册到 Windows 输入法列表后，是自动开启时的优先入口；没有该条目或该条目未启用时，回退到小白 T9。九宫格、候选和所有手柄按键在两种方式下相同，不需要另外选择运行模式。

独立入口使用单独的 TSF 文本组件，不依赖小白联动组件或小白服务端；C# 主程序仍需加载已安装的原版 `rime.dll` 和本地方案。它适合在小白联动入口不可用时继续输入，不是传统的实体键盘拼音输入法。两种入口切换时，未提交的编码会清除。

备用组件存放在 `C:\Program Files\GamePadT9\standalone\<架构-哈希>`，使用原型的独立标识 `{595B67E9-48A3-4C82-B7B1-64E4A35C9D92}`，不会替换小白联动组件的注册路径。

需要重新构建、安装或验证备用方式时，在本项目目录执行：

```powershell
# 先从托盘退出主程序；跳过已安装的小白组件构建。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipXiaobai -Standalone
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register-standalone.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor -Standalone
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor -Standalone -Architecture x86
```

安装备用条目需要正常 UAC 授权；已注入小白时须先还原，才能注册独立条目。移除独立条目时执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register-standalone.ps1 -Unregister
```

## 设置

点击输入面板右上方的 **设置**，或在托盘图标菜单选择 **设置…**。可选择已连接手柄，摇杆和扳机可以独立选择，界面按键图标会跟随手柄类型和绑定更新。

| 设置项 | 默认值 | 可调整范围 |
|---|---|---|
| 已连接手柄 | 自动选择，保持当前手柄 | 当前识别的设备，支持热插拔刷新 |
| 选择九宫格区域 | 右摇杆 | 左摇杆 / 右摇杆 |
| 确认区域输入 | 右扳机 RT / R2 | 左扳机 LT / L2 或右扳机 RT / R2 |
| 面板背景可见度 | 50% | 0–100%，两个输入界面共用 |
| 九宫格可见度 | 80% | 0–100%，同时用于外部输入栏和普通按钮 |
| 高亮区域可见度 | 90% | 0–100%，用于九格、候选高亮和外部完成按钮 |
| 背景模糊程度 | 0%（关闭） | 0–100%，输入界面与外部输入框共用 |

**可见度越高越清晰**：0% 完全透明，100% 完全不透明。三个区域的可见度独立生效，文字保持清晰。设置窗口提供实时效果预览，可拖动滑块或填写百分比。

外部输入框与底部输入界面共用这三档可见度，输入栏和按钮的背景不会与整块面板的透明度叠乘。**背景模糊程度**在“设置 → 面板外观”中通过一个滑块共同调整：0% 关闭，100% 最强，文字和 Steam 按键图标保持清晰。模糊实时作用于窗口背后的画面；背景不透明时会遮住模糊效果。旧版分别设置的配置沿用“输入界面”的值，统一应用到两处；再次保存时移除旧的外部输入框模糊字段。没有模糊设置的配置默认为 0%，保留原有可见度。

模糊使用 Windows Composition 的高斯模糊，由一个不接收焦点和输入的效果窗口显示，随对应面板移动、隐藏和关闭。无需另装图形运行时；Windows 合成接口不可用时保留半透明显示。当前已在 Windows 11 验证。预览：[外观与模糊](artifacts/settings-appearance-window.png)、[两个界面效果预览](artifacts/settings-effects-preview.png)。

选择左摇杆后，L3 负责按下摇杆输入；选择右摇杆后使用 R3。数字模式下，所选摇杆按下输入 0，所选扳机输入区域数字 1–9。未选中的摇杆和扳机不触发区域输入。

打开设置会关闭本轮手柄输入并恢复原输入法。点击 **保存** 后，在目标文本框按 View + Menu 重新开启输入。**恢复默认** 修改当前表单，点击保存后生效；取消保留原设置。

设置保存到项目目录的 `user-settings.json`，下次启动自动加载。首次启动采用上述默认值；配置损坏时会提示并使用默认值。也可用 `artifacts/app/GamePadT9.exe --settings` 启动并打开设置窗口。

截图：[设置窗口](artifacts/settings-window.png)、[透明面板效果](artifacts/overlay-transparency.png)。

自动选择会保留当前已连接设备，新接入的手柄不会抢占；当前设备断开后，输入先关闭，再选择可用设备，需重新按组合键开启。手动指定设备时只接收该设备的输入：断开后保留“未连接”条目并等待重连，不改用其他手柄。保存时优先记住设备序列号的哈希，否则使用设备路径或槽位；更换 USB 接口、连接方式或驱动后，如果系统提供的身份发生变化，需要重新选择。系统未提供唯一身份的同型号设备只能在本次连接期间区分。

手柄通过 SDL3 直接读取，PS4 / PS5 支持 USB 和蓝牙路径，无需通过 Steam 启动本程序。若 Steam Input、DS4Windows 或设备隐藏工具把 PS 手柄仅暴露为虚拟 Xbox 设备，列表与图标会显示系统可见的 Xbox 身份；要显示 PS 图标，请选择可直接读取的 PS 设备。图标来自本机 Steam 客户端并已嵌入程序，运行时不要求 Steam 在线或运行。来源和许可证见 [第三方说明](THIRD-PARTY-NOTICES.md)。

## 按键和布局

主界面与设置窗口使用 Steam 原始 SVG 按键图标，按实际显示尺寸绘制矢量路径，保留原有配色和透明背景，支持系统缩放。图标会随所选 Xbox、PS4、PS5 手柄切换。

Steam 的完整图标素材库已按 Xbox、PS3 / PS4 / PS5、任天堂系列、Steam Controller 和 Steam Controller 2015 分类存放在 [`src/GamePadT9/Assets/Steam`](src/GamePadT9/Assets/Steam/README.md)。各分类仅保留现有主题的 SVG 原图；具体目录、共用图标及来源校验见该目录的说明和 `manifest.json`。素材收集不改变当前程序的手柄支持范围。

下表为默认的右摇杆与右扳机配置。修改设置后，RT、R3 分别对应所选扳机和摇杆按下动作。

PS 手柄对应关系如下，操作逻辑相同，程序内显示 Steam 图标：

| Xbox | PS4 / PS5 | 功能 |
|---|---|---|
| A | ×（叉号） | 确认候选 |
| B | ○（圆圈） | 短按关闭候选，长按 1 秒关闭输入 |
| X | □（方块） | 退格 |
| Y | △（三角） | 切换九键 / 数字 |
| LB / RB | L1 / R1 | 上下选择候选 |
| LT / RT | L2 / R2 | 所选扳机确认区域输入 |
| View + Menu | PS4：Share + Options；PS5：Create + Options | 开启 / 关闭输入；外部输入框开启时为完成并关闭 |

左右摇杆、按下摇杆和十字键左右的操作相同。触摸板、PS 键、静音键未分配输入动作。

| 按键 | 九键模式 | 数字模式 |
|---|---|---|
| 右摇杆 | 选择九宫格区域 | 选择 1–9 |
| RT | 输入当前字母组 / 打开符号 | 输入高亮数字 |
| R3（按下右摇杆） | 输入按下前的区域 | 输入 0 |
| A | 确认高亮候选 / 标点 | 无动作 |
| LB / RB | 上一个 / 下一个候选 | 无动作 |
| DPad 左 / 右 | 前一页 / 后一页 | 无动作 |
| X | 有编码时删除编码，否则删除已上屏文字 | 删除已上屏文字 |
| Y | 切换数字模式 | 返回九键模式 |
| 短按 B | 关闭候选并清除未提交编码，保留九宫格 | 清除暂存的九键编码，保留九宫格 |
| 长按 B 1 秒 | 关闭手柄输入并隐藏面板 | 同左 |
| View + Menu | 开启 / 关闭手柄输入；外部输入框为完成并关闭 | 同左 |

九键布局：

| 左列 | 中列 | 右列 |
|---|---|---|
| 符号 | ABC | DEF |
| GHI | JKL | MNO |
| PQRS | TUV | WXYZ |

数字布局：

| 左列 | 中列 | 右列 |
|---|---|---|
| 1 | 2 | 3 |
| 4 | 5 | 6 |
| 7 | 8 | 9 |

摇杆松开时选择中间的 JKL / 5；数字 0 使用 R3。摇杆分区和 RT 使用滞回阈值，减少边界抖动。RT、R3、A、Y 不连发，同时按 RT 和 R3 只输入一次。X、LB、RB 按住 420 毫秒后以 110 毫秒间隔重复。短按 B 在释放时生效，长按关闭后释放不会再次触发短按。

无编码时，左上角打开三页共 27 个常用符号，使用 LB / RB、DPad 和 A 选择上屏；已有编码时保留小白原方案的该键行为。数字模式右侧显示最近输入的数字记录，不代表目标文档的完整内容。

Y 切换到数字模式会暂存九键编码，在同一文本框返回九键模式后可以继续选词。切换文本框会清除未提交编码，避免提交到其他窗口。

**输入“你好”**：依次选择 MNO → GHI → GHI → ABC → MNO，每次按 RT，再按 A。候选标题下的编码显示为 `64 426`，与顶部 `1 2 3`、中间 `4 5 6`、底部 `7 8 9` 的顺序一致。引擎内部继续使用小白原键位编码 `64486`，候选词和词库匹配不受显示转换影响。

## 输入方式

手柄 → SDL3 设备读取 → C# 控制器 → 原版 librime API → C# 候选面板 → 当前所选入口的 TSF 编辑会话 → 当前文本框。

自动切换通过 `GetGUIThreadInfo().hwndFocus` 找到实际接收键盘输入的控件，再使用一个短时、指定线程的 Windows 消息钩子，在该控件所属线程中调用 TSF API，读取原输入法的完整标识（或英文等键盘布局句柄），并执行切换和恢复。辅助组件在目标线程再次检查焦点；C# 只连接同一输入线程的 TSF 端点。32 位和 64 位目标使用各自的辅助组件。它不是键盘钩子，不记录按键，也不模拟 Win + Space、Alt + Shift 等快捷键；请求完成即卸下钩子。

接口依据：[GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo)、[SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw)、[GetActiveProfile](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-getactiveprofile)、[ActivateProfile](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-activateprofile)。

面板通过 [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) 使用每个像素的 Alpha 合成；背景、九格、高亮分别绘制自己的可见度，保持置顶及不抢输入焦点的行为。

九键模式直接调用 Rime API，中文和标点通过 TSF 处理，不模拟拼音按键或粘贴。Rime 内部的 `KP_*` 编码只是函数参数，不会发送到 Windows 键盘队列。正在输入的拼音编码由引擎直接退格，不发送 Backspace。

数字模式输入 0–9 时使用 `SendInput` 发送小键盘数字。事件带有专用标记，目标组件在确认当前焦点后临时放行这些数字，避免小白把它们再次解释为拼音编码；实体小键盘事件仍由原有逻辑处理。程序不切换 NumLock，也不模拟 Enter、空格等其他功能键。

备用 TSF 组件不拦截键盘按键，数字经过焦点检查后直接交给文本框；无需小白专用的数字放行接口。

**已上屏文字的退格兼容**：X 优先请求 TSF 删除；如果组件不提供该能力，或目标返回“不支持此编辑操作”（原先显示“目标控件未开放完整文档”），则自动发送一组 Backspace 按下和松开。在九键、数字模式下都适用，面板显示“已发送 Backspace”。这是小键盘数字之外明确允许的按键模拟。

发送 Backspace 前重新核对目标文本框及焦点令牌，并检查 Ctrl、Alt、Shift、Win 等修饰键。焦点变化、一般编辑失败或结果不明的超时不会补发 Backspace，避免误删或重复删除。兼容模式的具体删除行为由目标控件决定，与该控件处理实体 Backspace 一致。

手柄使用独立的 Rime 会话和学习数据。**它与实体键盘共用小白输入法入口，但不共用正在输入的拼音、候选状态或词频数据库。** 实体键盘正在组词时，手柄提交暂停，以免插入到该预编辑内容中。

## 安装方式和恢复

设置窗口和安装脚本共用 C# 组件管理器。小白注入只更改现有 COM 类 `{A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A}` 在 32 位和 64 位视图中的 `InprocServer32` 路径，指向 `Program Files\GamePadT9\components\<架构-哈希>\GamePadT9.Xiaobai.dll`。该扩展按本机备份路径加载原 DLL，保留原服务端和实体键盘输入。

小白原输入法配置文件 `{3D02CAB6-2B8E-4781-BA20-1C9267529467}`、Windows 键盘布局及原版 DLL 文件保留。原始组件路径和校验值备份在 `C:\Program Files\GamePadT9\components\original-registration.json`。

组件安装需要当前用户正常的 Windows UAC 授权。管理器先核对架构与发行包 SHA256，暂存两种架构的组件、保存本机原注册信息，再修改注册项；发生失败会回滚本次变更。还原时不会覆盖小白更新程序已设置的新路径。开发构建应先通过下方隔离测试。

关闭 GamePad T9 后，可以恢复小白原组件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-xiaobai.ps1 -Restore
```

恢复后重新打开目标程序。此操作不卸载小白 T9，也不删除用户词库。旧命令 `scripts/register.ps1` 现在转交联动组件安装，`-Unregister` 转交恢复，不再注册独立输入法。

## 构建与复测

需要 .NET 10 SDK、x86 .NET 10 Windows Desktop Runtime、Visual C++ x64/x86 工具链和 Windows SDK。64 位测试编辑器还需要 x64 .NET 10 Windows Desktop Runtime。原版小白输入法应已完成一次方案部署。只有旧源码联动方式才需要 ATL、Boost 和同级 `../xiaobai-t9` 源码。

先从托盘退出 GamePad T9，再在本项目目录执行：

```powershell
# 首次准备本地方案与设置；已有配置时不必重复。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare.ps1

# 构建 C# 主程序及两种架构的小白联动组件。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Standalone

# 基础检查及隔离测试：尚不更改系统组件路径。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Proxy
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Proxy -Architecture x86
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Components

# 制作不含 .NET 运行时的便携包；目标电脑需要 .NET 10 桌面运行时 x86。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

`prepare.ps1` 默认读取注册表中的小白安装目录，可指定 `-InstallRoot` 和 `-RimeUserRoot`。它为开发环境复制已部署方案与 Lua 支持文件到 `data/installed`，生成本地 `gamepadt9.json`，不复制原用户学习数据库。便携版无需运行该脚本，启动时会自动准备本机数据到 `data/local`。

`build-xiaobai.ps1` 从同级仓库复制 WeaselTSF、WeaselIPC、WeaselUI 等源码到 `artifacts/xiaobai-source-<架构>` 后应用补丁，不修改原仓库。首次构建自动下载并校验官方 Boost 1.84.0 压缩包；这个版本与已安装小白服务端的序列化格式 20 匹配。构建使用 C++17、静态运行库，不要求把原组件转成 C#。

默认 `build.ps1` 构建标准 TSF 扩展，产物位置记录在 `artifacts/proxy-<架构>-path.txt`；旧 `build-xiaobai.ps1` 仅作为历史源码方案保留，可用 `-LegacyXiaobaiSource` 构建，不随便携包分发。输入法切换辅助组件位置记录在 `artifacts/input-method-<架构>-path.txt`，无需注册。只改 C# 时可运行 `scripts/build.ps1 -SkipXiaobai -SkipInputMethodControl`。

项目包含固定版本 SDL 3.4.16 的 x86 DLL，构建时自动复制到主程序旁边。Steam 图标按 `SteamGlyphs.props` 只嵌入当前界面使用的 34 个 SVG，由 SVG.NET 3.4.8 绘制；首次构建需要从 NuGet 还原 `Svg` 及其 ExCSS 依赖。升级本次手柄和图标功能只需重新构建 C# 主程序，无需重新注册输入法组件。

主程序构建会自动调用 `scripts/build-backdrop.ps1`，增量生成 x86 `GamePadT9.Backdrop.dll`。该小型 C++20 组件使用 Windows SDK 中的 C++/WinRT、Composition 和 Direct2D，需 Visual C++ 构建工具及对应 Windows SDK；不依赖 Win2D 或 Windows App SDK。主程序与界面逻辑仍为 C#，发布时模糊组件合并进 EXE，无需注册。`-SkipXiaobai -SkipInputMethodControl` 仍会更新这个界面组件。

本次新增焦点模式还更新了 x64 / x86 输入法辅助组件；升级请运行 `scripts/build.ps1 -SkipXiaobai`，包含主程序和辅助组件构建，无需重新注册输入法。随后可以用以下检查验证程序配置、外部草稿、长按完成和逐次切焦：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Focus
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Focus -Architecture x86
```

检查只操作自己创建的测试文本框，使用临时程序配置；验证真实前台焦点、TSF 文字、数字与退格、原输入法恢复、只读和已关闭目标、取消待提交操作及草稿保留。复制检查会暂时写入测试文字，并尝试恢复预先读取的剪切板内容。

多手柄与 Steam 图标专项检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Controllers
```

此检查使用 SDL 进程内虚拟 Xbox、PS4、PS5 设备，经生产读取层验证全部输入动作、九格方向、左右控制、按住保护、设备隔离、断开重连、设置选择与保存，以及三套图标。虚拟设备只存在于测试进程，不安装系统虚拟驱动。报告另列实际检测到的硬件；虚拟测试不替代 PS 真机 USB / 蓝牙验证。图标预览：[Xbox](artifacts/controller-Xbox-t9.png)、[PS4](artifacts/controller-PS4-t9.png)、[PS5](artifacts/controller-PS5-t9.png)。

界面设置、控制映射及透明效果检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Settings
```

该检查覆盖四种左右摇杆 / 扳机组合、未选控制不触发输入、切换配置时的按住保护、真实设置控件保存 / 取消 / 恢复默认、配置加载，以及叠加在独立测试窗口上的实际屏幕像素。同时检查 Xbox、PS4、PS5 全部界面 SVG 在 100%、125%、150%、200% 缩放下的绘制、透明背景、边界及绘图状态恢复，并核对原始颜色和嵌入资源。检查使用临时配置，不更改用户保存的设置。

外观检查还覆盖外部输入框的 0%、50/80/90%、100% 实际背景混合、共用模糊滑块的保存与旧配置兼容，以及两处界面背后的真实条纹在 0%、10%、80% 模糊下的边缘变化、输入焦点保持和效果窗口关闭。`--verify-focus` 在共用模糊设置开启时回归完整输入流程。

自动切换和恢复的专项检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -InputMethod
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -InputMethod -Architecture x86
```

此检查只操作自己创建的 WPF 窗口，目标程序通过自身 TSF 管理器独立记录当前输入法。回退检查模拟 GamePad T9 不可用，不卸载或禁用本机已安装条目。

旧式文本框的 Backspace 兼容检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Backspace
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Backspace -Xiaobai
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Backspace -Architecture x86
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Backspace -Architecture x86 -Xiaobai
```

此检查使用真实 Win32 EDIT 文本框触发不支持 TSF 删除的回执，验证每次 X 只发送一组 Backspace、拼音编码退格不发送按键，以及过期焦点不会删除文字。

`-Editor` 使用测试进程私有的 COM 清单加载组件，只在该进程内激活小白输入法；结束时关闭该测试窗口。`-Installed` 使用系统实际注册路径，测试同时核对加载路径及文件哈希，防止把隔离副本误当成安装结果。

`scripts/test.ps1 -Notepad` 会创建专用空白测试文档，通过生产控制器的 View + Menu 事件自动切换其编辑线程到 GamePad T9，验证中文、候选、数字、退格和长按 B 恢复。无需预先手动选择输入法。成功后清空测试文字，并只关闭本次创建的标签页；不关闭其他用户文档。记事本已有进程可能继续使用更新前的输入组件 DLL。

## 验证结果

2026-09-10 背景模糊共用设置：两处界面及预览统一读取一个模糊值，设置页合并为一个滑块。Release 构建零警告、零错误；77 项设置与实际背景效果检查通过，包含旧配置沿用输入界面值、再次保存移除旧字段，以及两处界面的模糊变化和焦点保持。

2026-09-10 半透明与背景模糊：Release 构建零警告、零错误；74 项设置及实际屏幕效果检查通过，x64 / x86 目标各 135 项焦点回归在两处模糊开启时通过。验证外部输入栏独立使用 50/80/90% 可见度、0–100% 边界值、真实背景模糊随程度变化、首次开启不抢焦点，以及关闭后无残留效果窗口。单文件便携包约 4.62 MiB，不包含 .NET 运行时；新目录解压后从临时工作目录运行，74 项外观检查通过，缓存中正确释放 SDL3 与模糊组件，10 个输入组件哈希一致。见 [背景效果与发行验证报告](artifacts/backdrop-verification.json)。

2026-09-10 外部输入框组合键完成：View + Menu 改为完成并关闭，按配置复制或填入目标；提交中重复按组合键不会取消或重复填入。x64 / x86 目标各 135 项程序配置与焦点检查、91 项手柄检查通过，覆盖按键释放等待、候选确认、精确一次填入、输入法恢复，以及只读目标、焦点变化和超长文字时保留草稿。长按 B 和“保留并关闭”继续执行取消操作。

2026-09-09 紧凑外部输入框：窗口高度从 276 调整为 112 DIP，文本框固定为 34 DIP，移除标题栏，目标程序、完成方式、状态和字数合并为一行；仅为该窗口关闭系统显示/隐藏过渡动画。Release 构建零警告、零错误，131 项程序配置与焦点检查通过，已核对 Xbox / PS5 实际窗口截图。

2026-09-09 WPF UI 外部输入框：Release 构建零警告、零错误；x64 / x86 目标各 131 项程序配置与焦点检查、52 项设置检查、91 项手柄检查通过。新增验证覆盖窗口与九宫格不重叠、窄布局按钮、目标程序名称、Steam 确认图标、连续插入与选区替换、组合字符及 Emoji 退格、原生键盘换行、复制和完成按钮、提交期间锁定编辑，以及标题栏关闭后保留草稿并恢复焦点。截图为实际窗口捕获；焦点验证使用自建编辑器，不代表所有游戏均能接受自动填入。

包含新版外部输入框的单文件便携包约 4.55 MiB，不包含 .NET 运行时。解压搬迁后从临时工作目录启动，18 项基础检查、16 项组件检查、131 项输入与焦点检查通过，10 个原生组件哈希一致。见 [外部输入框与发行验证报告](artifacts/external-ui-verification.json)。

2026-09-09 WPF UI 面板：Release 构建零警告、零错误；52 项设置检查、112 项程序配置与焦点回归、91 项手柄检查通过。面板检查包括真实键盘方向键和 Esc、保存失败保留草稿、搜索与重复路径处理、移除独立配置恢复全局继承，以及实际窗口截图。输入法组件互斥继续通过临时注册表测试，不会为界面验证修改真实安装。

2026-09-09 单文件主程序：`GamePadT9.exe` 整合 SVG、ExCSS、SDL3 和启动配置，压缩包仍约 2.16 MiB，不包含 .NET 运行时。解压搬迁后，18 项基础检查、91 项手柄与 SVG 检查、16 项组件检查通过；启动追踪确认使用本机 x86 桌面运行时，缓存中仅释放原版 SDL3，10 个输入组件哈希一致。见 [单文件发行验证报告](artifacts/portable-single-file-verification.json)。

2026-09-09 无运行时便携版：压缩包约 2.16 MiB，使用系统已安装的 .NET 10 桌面运行时 x86。解压后从临时工作目录启动，16 项组件检查通过；已核对运行时依赖声明、10 个原生组件哈希及包内无 .NET 运行时文件。见 [无运行时发行验证报告](artifacts/portable-no-runtime-verification.json)。

2026-09-09 便携版：自包含发行包搬到另一目录、从临时工作目录启动且不使用系统 .NET 路径，18 项基础检查通过。x64 / x86 标准 TSF 扩展隔离输入检查、16 项组件互斥与失败回滚检查、45 项设置检查及 109 项焦点回归通过。组件管理测试使用临时注册表实现和文件目录，本轮未修改本机实际输入法注册；尚未逐个验证其他小白版本。发行文件和校验记录见 [便携版验证报告](artifacts/portable-verification.json)。

2026-09-09：Release 构建零警告、零错误；x64 / x86 程序配置与焦点专项检查、91 项手柄检查、45 项设置与 SVG 检查、18 项基础检查和记事本回归均通过。新模式验证使用自建 WPF 编辑器，尚未在具体游戏中验证失焦后的手柄响应。专项报告：[x64](artifacts/focus-verification.json)、[x86](artifacts/focus-x86-verification.json)。

2026-09-08，Release 构建、18 项基础检查、91 项多手柄检查、45 项设置与 SVG 检查通过；切换 SVG 后重新通过后两项检查。新版记事本此前通过完整输入及恢复检查，使用小白回退入口。本次 SDL 读取层检测到 Xbox 360 Controller；PS4 / PS5 使用进程内虚拟设备验证，尚未连接真机复测。两种架构的输入法隔离测试、实际安装测试已有通过记录：

| 测试 | 报告 |
|---|---|
| 基础控制器和原版引擎 | [self-test.json](artifacts/self-test.json) |
| SDL 多手柄、选择重连与 Steam 图标 | [controller-verification.json](artifacts/controller-verification.json) |
| 设置、左右控制组合及真实透明合成 | [settings-verification.json](artifacts/settings-verification.json) |
| x64 隔离组件 | [editor-isolated-verification.json](artifacts/editor-isolated-verification.json) |
| x86 隔离组件 | [editor-x86-isolated-verification.json](artifacts/editor-x86-isolated-verification.json) |
| x64 实际安装组件 | [editor-verification.json](artifacts/editor-verification.json) |
| x86 实际安装组件 | [editor-x86-verification.json](artifacts/editor-x86-verification.json) |
| x64 备用输入法条目 | [editor-standalone-verification.json](artifacts/editor-standalone-verification.json) |
| x86 备用输入法条目 | [editor-standalone-x86-verification.json](artifacts/editor-standalone-x86-verification.json) |
| x64 自动切换和恢复 | [input-method-x64-verification.json](artifacts/input-method-x64-verification.json) |
| x86 自动切换和恢复 | [input-method-x86-verification.json](artifacts/input-method-x86-verification.json) |
| 新版记事本自动切换、输入和恢复 | [notepad-verification.json](artifacts/notepad-verification.json) |
| x64 / x86 独立入口 Backspace 兼容 | [x64 报告](artifacts/backspace-standalone-x64-verification.json)、[x86 报告](artifacts/backspace-standalone-x86-verification.json) |
| x64 / x86 小白入口 Backspace 兼容 | [x64 报告](artifacts/backspace-xiaobai-x64-verification.json)、[x86 报告](artifacts/backspace-xiaobai-x86-verification.json) |

恢复备用入口后，两种架构的备用输入测试和小白联动回归测试均通过。测试核对了实际使用的入口，防止备用测试误连到小白组件。

自动切换专项检查覆盖 View + Menu 开启、立即输入中文、短按 B 保持输入法、长按 B 恢复英文布局、多个窗口分别恢复、返回已访问线程重新切换且不覆盖原始记录、原先已选 GamePad T9、切换过程中关闭、小白回退、只读文本框就绪失败后恢复，以及正常关闭恢复，均无模拟键盘事件。

测试覆盖置顶九宫格不抢焦点、可见候选、候选移动和翻页、A 上屏“你好”、数字 `1234567890`、Y 保留编码、X 删除已上屏字符和完整 UTF-16 代理对表情、标点上屏、B 保留九宫格、关闭后隐藏及过期焦点拒绝。

端到端检查使用归一化手柄状态驱动生产 `Controller` 和 `InputSession`，通过 TSF 编辑回执和 UI Automation 独立读取文档确认结果。UI Automation 用于定位、聚焦、读取及关闭测试标签页；输入文字使用生产输入路径。完整 TSF 文档的 WPF 检查中，每组模拟键盘事件恰为 20 次，全部来自数字 0–9；Backspace 兼容专项检查中，三次上屏退格产生 6 次键盘事件。候选编码的顶部 123 显示也已验证。本轮自动化检查不等同于逐项实体手柄体验测试。

截图：[九键候选](artifacts/overlay-t9.png)、[数字模式](artifacts/overlay-numeric.png)。

新版记事本现已通过完整检查：主窗口与编辑区位于不同线程，View + Menu 将编辑线程切换到 GamePad T9，主窗口线程保持原输入法；成功上屏“你好”和 `1234567890`，长按 B 恢复编辑线程原输入法，英文布局和原中文输入法均已验证。报告包含两个线程的标识、切换前后及恢复后的输入法，以及文档独立读回结果。早期“面板已开启但记事本未切换”的原因是选中了主窗口线程，现已修复；详见 [记事本报告](artifacts/notepad-verification.json)。

## 当前范围

- C# 宿主为 x86，匹配安装包中的 32 位 `rime.dll`；TSF 组件覆盖 x64 和 x86。ARM64 尚未验证。
- 候选和编码显示在浮窗；确认时文字进入目标文本框，尚无目标内的预编辑下划线。
- 完整 TSF 文档支持直接删除选中文本、普通字符、UTF-16 代理对和 CRLF；仅提供临时输入上下文的旧控件自动使用 Backspace 兼容处理。复杂组合表情的删除粒度不作统一保证。
- 当前不屏蔽游戏收到的手柄操作。独占全屏游戏、管理员程序及其他特殊编辑控件需要分别验证，不能保证所有程序兼容。
- 自动切换需要目标允许消息钩子和 TSF 调用；权限更高或阻止外部钩子的程序可能拒绝操作。强制结束进程或系统异常时不能保证执行恢复。
- 提交失败或回执超时不自动重发；面板会提示检查结果，按 B 清除后继续。
- 更新小白安装包后，其安装程序可能恢复自身组件路径；新扩展会保留新路径，可在设置中再次注入。原版标准接口或引擎 ABI 变化时仍需要适配并验证。

首次集成时发现 Boost 版本与原服务端不匹配，曾导致加载该版本的部分程序异常退出。已恢复原组件后修复版本匹配和异常边界，并改为先做进程隔离验证；上表结果来自修复后的组件。

## 代码结构

| 文件 | 职责 |
|---|---|
| `src/GamePadT9/GamePadApplication.cs` | 托盘、后台采样、连接状态与窗口生命周期 |
| `src/GamePadT9/Controller.cs` | 所选摇杆分区、边沿、长按和连发 |
| `src/GamePadT9/GamepadDevices.cs` | SDL3 读取、Xbox / PS 识别、设备选择和热插拔 |
| `src/GamePadT9/ButtonGlyphs.cs` | Steam SVG 加载、缓存及矢量按键提示绘制 |
| `src/GamePadT9/SteamGlyphs.props` | 界面使用的 34 个 Steam SVG 嵌入资源清单 |
| `src/GamePadT9/ControllerValidation.cs` | 通过 SDL 虚拟设备验证多手柄读取与选择 |
| `src/GamePadT9/InputSession.cs` | 模式、候选、焦点绑定与提交协调 |
| `src/GamePadT9/FocusedInputSession.cs` | 外部编辑栏、目标焦点切换、完成及草稿保留 |
| `src/GamePadT9/ProgramProfiles.cs` | 程序独立配置、全局回退和持久化 |
| `src/GamePadT9/ProgramProfilesForm.cs` | 程序列表与输入方式、完成操作下拉框 |
| `src/GamePadT9/FocusValidation.cs` | 程序配置、长按完成及真实焦点与文本验证 |
| `src/GamePadT9/MainForm.cs` | 不抢焦点的置顶九宫格与候选绘制 |
| `src/GamePadT9/RimeEngine.cs` | 原版 librime C ABI 与独立会话 |
| `src/GamePadT9/NumericInput.cs` | 数字模式限定的小键盘 0–9 事件 |
| `src/GamePadT9/BackspaceInput.cs` | 带焦点和修饰键检查的 Backspace 兼容处理 |
| `src/GamePadT9/TsfClient.cs` | 带焦点令牌、请求编号和回执的跨进程命令 |
| `src/GamePadT9/InputMethodSwitcher.cs` | 原输入法记录、自动切换、窗口跟随和恢复 |
| `src/GamePadT9/UserSettings.cs` | 左右控制及可见度设置的保存、加载和默认值 |
| `src/GamePadT9/SettingsForm.cs` | 设置窗口与可见度实时预览 |
| `src/GamePadT9/LayeredWindow.cs` | 独立区域可见度的透明窗口合成 |
| `src/GamePadT9/ExternalInputBackground.cs` | 外部输入框按区域绘制独立可见度 |
| `src/GamePadT9/BlurBackdrop.cs`、`native/Backdrop.cpp` | 不抢焦点的实时背景高斯模糊 |
| `src/GamePadT9/SettingsValidation.cs` | 控制映射、设置持久化及实际屏幕像素验证 |
| `native/InputMethodControl.cpp` | 在目标线程读取及切换 TSF 输入法、授予面板前台权限的短时消息钩子 |
| `scripts/build-input-method.ps1` | 构建两种架构的输入法切换辅助组件 |
| `src/GamePadT9/InputMethodValidation.cs` | 自动切换和恢复的端到端检查 |
| `native/Bridge.cpp` | TSF 插入、退格编辑会话及数字放行接口 |
| `native/xiaobai/Integration.cpp` | 在原小白服务生命周期中挂接手柄接口 |
| `scripts/build-xiaobai.ps1` | 原组件源码暂存、兼容补丁与双架构构建 |
| `scripts/install-xiaobai.ps1` | 已验证组件安装、原始路径备份和恢复 |
| `scripts/build-standalone.ps1` | 独立备用 TSF 组件的双架构构建 |
| `scripts/register-standalone.ps1` | 安装或移除备用输入法条目 |
| `native/Control.cpp` | 备用组件注册和注销工具 |
| `src/GamePadT9/NotepadValidation.cs` | 使用生产输入路径的端到端验证 |
| `src/GamePadT9/BackspaceValidation.cs`、`tests/BackspaceEditor/` | 使用真实旧式文本框验证退格兼容行为 |
| `tests/TestEditor/` | 隔离及安装验证使用的 TSF 编辑窗口 |

Rime、小白 T9 和词库遵循各自原始许可，参见同级 `xiaobai-t9/LICENSE.txt`、`xiaobai-t9/librime/LICENSE` 和词库文件头。`data/installed/sources.json` 记录本机引用的运行库及关键部署文件 SHA256。
