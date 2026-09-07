# GamePad T9

Windows Xbox / XInput 手柄九键输入程序。主程序、手柄控制、候选面板和数字模式使用 **C#**；复用已安装的小白 T9 原版 Rime 引擎，用独立的 **C++ TSF 组件**向目标应用提交文字。

九键模式直接调用 Rime API，中文、标点和退格通过 TSF 编辑会话处理。只有数字模式输入 0–9 时调用 `SendInput` 发送小键盘数字；没有模拟九键键盘、中文按键、粘贴或退格键。Rime 内部的 `KP_*` 编码只是函数参数，不会发送到 Windows 键盘队列。

## 开始使用

1. 双击 [Start.cmd](Start.cmd)。程序在系统托盘运行，初始关闭输入。
2. 打开记事本等文本编辑程序，选择 Windows 输入法列表中的 **GamePad T9 验证**，将光标放入文本框。
3. 同时按 **View + Menu** 开启输入。也可以双击托盘图标，或使用托盘菜单的“开启输入”。
4. 开启时在当前屏幕右下角显示置顶九宫格和候选面板。右摇杆移动时高亮对应区域；浮窗不抢编辑框焦点，标题区域可拖动。
5. 右摇杆选区，按 **RT / R3** 输入字母组，按 **A** 确认当前候选。

关闭面板的 × 只会关闭手柄输入。长按 B 后，程序仍在托盘中等待再次开启；完全退出请使用托盘菜单“退出”。断开手柄会自动关闭输入，重新连接后需要再次开启。

本次已更新并注册组件。以后更新原生组件后，已经打开的目标程序可能仍使用旧 DLL，重新打开该程序即可加载新版本。

## 按键和布局

| 按键 | 九键模式 | 数字模式 |
|---|---|---|
| 右摇杆 | 选择九宫格区域 | 选择 1–9 |
| RT | 输入当前区域的字母组 / 打开符号 | 输入高亮数字 |
| R3（按下右摇杆） | 输入按下前的区域 | 输入 0 |
| A | 确认高亮候选 / 标点 | 无动作 |
| LB / RB | 上一个 / 下一个候选 | 无动作 |
| DPad 左 / 右 | 前一页 / 后一页 | 无动作 |
| X | 有编码时删除编码，否则删除已上屏文字 | 删除已上屏文字 |
| Y | 切换数字模式 | 返回九键模式 |
| 短按 B | 关闭候选并清除未提交编码，保留九宫格 | 清除暂存的九键编码，保留九宫格 |
| 长按 B 1 秒 | 关闭手柄输入并隐藏面板 | 同左 |
| View + Menu | 开启 / 关闭手柄输入 | 同左 |

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

摇杆松开时选择中间的 JKL / 5；数字 0 使用 R3。摇杆分区和 RT 都有滞回阈值，减少边界抖动；RT、R3、A、Y 不连发，同时按 RT 和 R3 只输入一次。X、LB、RB 按住 420 毫秒后以 110 毫秒间隔重复。短按 B 在释放时生效，长按关闭后释放不会再触发短按。

无编码时，左上角打开三页共 27 个常用符号，使用 LB / RB、DPad 和 A 选择上屏；已有编码时保留小白原方案的该键行为。数字模式右侧显示最近输入的数字记录，不是目标文档的完整内容。

Y 切换到数字模式会暂存九键编码，回到同一文本框时可以继续选词。切换文本框会清除未提交编码，防止误投到其他窗口。九宫格、候选和模式按钮也支持鼠标点击。

**输入“你好”**：依次选择 MNO → GHI → GHI → ABC → MNO，每次按 RT，再按 A。引擎使用原小白键位编码 `64486`，数字模式则使用上方 1–9 的顺序。

## 本次验证

2026-09-08，Release 构建通过，检测到 XInput 手柄槽位 0。此前用户已完成实体手柄的核心可行性验证；本次新增功能使用自动化状态样本验证，实际手感还需在新版中体验。

- [基础检查](artifacts/self-test.json)：18 项通过，覆盖连接时按键已按住、摇杆滞回、按键去重、长短 B、Y 切换、数字模拟模式限制，以及原版引擎生成“你好”。
- [记事本验证](artifacts/notepad-verification.json)和[独立编辑器验证](artifacts/editor-verification.json)：均为 `passed: true`、`completedAllChecks: true`。
- 端到端检查覆盖：置顶九宫格不抢焦点、可视候选、高亮切换、翻页、A 上屏“你好”、数字 `1234567890`、Y 保留编码、X 删除已上屏字符及完整 UTF-16 代理对表情、符号选择、B 保留九宫格、关闭后隐藏。
- 两组端到端检查均使用生产 `Controller` 和 `InputSession`，通过 TSF 编辑回执及 UI Automation 独立读回目标文本证明上屏结果。UI Automation 用于定位、聚焦、读取，不负责写字。
- 每组检查的模拟键盘事件恰为 20 次，全部来自数字 0–9 的按下和松开；九键输入、中文上屏、标点和退格没有产生模拟按键。

面板实测截图：[九键候选](artifacts/overlay-t9.png)、[数字模式](artifacts/overlay-numeric.png)。自动化记事本检查会留下以 `GamePadT9-TSF-` 开头的测试标签页，内容可能尚未保存到磁盘。

## 当前范围

- C# 宿主为 **x86**，匹配已安装小白 T9 的 32 位 `rime.dll`；TSF 组件为 **x64**，当前验证目标是 x64 记事本和 WPF 编辑器。32 位、ARM64 应用及其他特殊编辑控件尚未覆盖。
- 已上屏退格要求目标控件开放完整 TSF 文档；部分旧式控件仅提供临时输入上下文，会显示不支持直接删除的提示。支持删除选中文字、前一个普通字符、UTF-16 代理对和 CRLF；复杂组合表情不保证按整个字素删除。
- 候选和预编辑编码显示在浮窗；目标文本框在确认时收到文字，尚无原位预编辑下划线。
- 数字输入也需要选择本项目的输入法。程序不切换 NumLock，也不模拟 Enter、空格或其他功能键。
- 当前不屏蔽游戏收到的手柄操作。独占全屏游戏、管理员程序和其他输入框需要分别验证。
- 提交失败或回执超时不自动重发；面板会提示检查结果，按 B 清除后继续。
- 原版小白安装目录和用户词库不作修改。已部署方案的副本保存在 `data/installed/build`，本程序学习数据和日志单独保存在 `artifacts/rime-user`。

## 构建与复测

需要 .NET 10 SDK、x86 .NET 10 Windows Desktop Runtime、Visual C++ x64 工具链和 Windows SDK。`-Editor` 测试还需要 x64 .NET 10 Windows Desktop Runtime。原版小白输入法应已完成一次方案部署。

先从托盘退出正在运行的 GamePad T9，然后在本项目目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Notepad
```

`prepare.ps1` 默认引用 `C:/Program Files/Rime/xiaobait9-2026.08.04`，可指定 `-InstallRoot` 和 `-RimeUserRoot`。脚本复制已部署方案及 Lua 支持文件、生成本地 `gamepadt9.json`，不复制原用户学习数据库。互操作声明对应同级 `../xiaobai-t9/librime/src/rime_api.h`。

`build.ps1` 将原生组件按内容哈希放入 `artifacts/tsf/<hash>`，通过 `artifacts/app/bridge-path.txt` 记录本次版本；不会覆盖正在被目标进程加载的旧 DLL。`register.ps1` 弹出正常 UAC 授权窗口，注册本项目独立的 COM / TSF 条目。原生组件更新后需要重新注册，并重新打开需要使用新组件的目标程序。

只修改 C# 时可使用：

```powershell
dotnet build src/GamePadT9/GamePadT9.csproj -c Release -o artifacts/app
```

`-Editor` 启动并在结束时关闭本项目的空白 WPF 测试窗口。`-Notepad` 创建空白测试文档；若记事本仍加载旧组件，会在报告的 `skipped` 中注明未覆盖的退格检查，`completedAllChecks` 为 false。测试通过 TSF API 激活本项目输入法，会改变当前会话的输入法选择；完成后可自行切回。

## 代码结构

| 文件 | 职责 |
|---|---|
| `src/GamePadT9/GamePadApplication.cs` | 托盘、后台采样、连接状态与窗口生命周期 |
| `src/GamePadT9/Controller.cs` | 右摇杆分区、边沿、长按和连发 |
| `src/GamePadT9/InputSession.cs` | 模式、候选、焦点绑定与提交协调 |
| `src/GamePadT9/MainForm.cs` | 不抢焦点的置顶九宫格与候选绘制 |
| `src/GamePadT9/SymbolMenu.cs` | 常用符号菜单 |
| `src/GamePadT9/RimeEngine.cs` | 原版 librime C ABI 与候选状态 |
| `src/GamePadT9/NumericInput.cs` | 仅数字模式可用的小键盘 0–9 模拟 |
| `src/GamePadT9/TsfClient.cs` | 带焦点令牌、请求编号和回执的跨进程命令 |
| `native/Bridge.cpp` | TSF 输入服务、插入与退格编辑会话 |
| `native/Control.cpp` | x64 注册、注销和激活工具 |
| `src/GamePadT9/NotepadValidation.cs` | 使用生产输入路径的端到端验证 |
| `tests/TestEditor/` | 供自动化验证使用的独立 TSF 编辑窗口 |

## 卸载本项目输入法

关闭 GamePad T9，切换到其他输入法，然后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register.ps1 -Unregister
```

这只移除本项目的注册信息，不卸载原版小白 T9。关闭仍加载此组件的应用后，才能删除或移动项目文件夹。

Rime、小白 T9 和词库遵循各自原始许可，参见同级 `xiaobai-t9/LICENSE.txt`、`xiaobai-t9/librime/LICENSE` 和词库文件头。本项目未重新打包分发安装包内的引擎；`data/installed/sources.json` 记录本机引用的运行库及关键部署文件 SHA256。
