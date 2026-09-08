# GamePad T9

<img width="780" height="520" alt="PixPin_2026-09-08_16-34-28" src="https://github.com/user-attachments/assets/1daa2826-2e65-4894-bbc6-501a91aa988f" />

Windows Xbox / XInput 手柄九键输入程序。启动 C# 主程序，在目标文本框按 **View + Menu**，程序记录原输入法并优先切换到 **GamePad T9**；该条目不可用时切换到 **小白 T9 输入法**。关闭手柄输入后恢复原输入法。

主程序、手柄控制、九宫格和候选面板使用 **C#**。联动方式在小白原有 **C++ TSF 组件**中加入手柄提交接口，沿用小白的输入法名称和标识。本机也保留“GamePad T9”独立输入法条目作为备用；两种方式均支持 x64 和 x86 目标程序。

## 开始使用

1. 双击 [Start.cmd](Start.cmd)。程序在系统托盘运行，初始关闭手柄输入。
2. 打开目标程序，将光标放入文本框，保持原先使用的输入法即可。
3. 同时按 **View + Menu** 开启手柄输入，自动记录并切换输入法，也可以双击托盘图标。
4. 置顶九宫格和候选面板出现后，用右摇杆选区，按 **RT / R3** 输入字母组，按 **A** 确认候选。

本机已安装两种输入组件。**更新输入组件前已打开的目标程序可能需要重新打开**，才能加载新组件；不用重新添加输入法。自动切换作用于实际获得键盘焦点的编辑控件线程，支持新版记事本中主窗口与编辑区分属不同线程的情况，不使用整个桌面范围的激活。

面板不抢文本框焦点，标题区域可拖动，九宫格和候选也可点击。再次按 View + Menu、长按 B、面板右上角 ×、托盘关闭输入、手柄断开或正常退出程序，都会关闭输入并恢复先前的输入法。短按 B 只关闭候选，不恢复输入法。重连手柄后需再次开启。

启用期间切换程序或同一窗口内的焦点控件时，会按实际输入线程记录并切换输入法；关闭时分别恢复。返回已访问的线程会重新确认切换及组件就绪，但不会覆盖首次记录的原输入法。原先已经选择 GamePad T9 时，关闭后仍保持 GamePad T9。

首次开启时，只有输入法切换成功、焦点仍匹配且该编辑线程的输入组件就绪，才显示面板。记录、切换或就绪检查失败时，程序会尝试恢复并在托盘提示原因。请确认光标位于可编辑区域，并先完成或取消实体键盘正在输入的拼音。

## 备用输入法条目

“GamePad T9”已注册到 Windows 输入法列表，现在是自动开启时的优先入口；没有该条目或该条目未启用时，回退到小白 T9。九宫格、候选和所有手柄按键在两种方式下相同，不需要另外选择运行模式。

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

安装备用条目需要正常 UAC 授权，不会自动切换整个桌面的输入法。仅移除备用条目时执行以下命令，小白联动方式继续保留：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register-standalone.ps1 -Unregister
```

## 按键和布局

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

摇杆松开时选择中间的 JKL / 5；数字 0 使用 R3。摇杆分区和 RT 使用滞回阈值，减少边界抖动。RT、R3、A、Y 不连发，同时按 RT 和 R3 只输入一次。X、LB、RB 按住 420 毫秒后以 110 毫秒间隔重复。短按 B 在释放时生效，长按关闭后释放不会再次触发短按。

无编码时，左上角打开三页共 27 个常用符号，使用 LB / RB、DPad 和 A 选择上屏；已有编码时保留小白原方案的该键行为。数字模式右侧显示最近输入的数字记录，不代表目标文档的完整内容。

Y 切换到数字模式会暂存九键编码，在同一文本框返回九键模式后可以继续选词。切换文本框会清除未提交编码，避免提交到其他窗口。

**输入“你好”**：依次选择 MNO → GHI → GHI → ABC → MNO，每次按 RT，再按 A。候选标题下的编码显示为 `64 426`，与顶部 `1 2 3`、中间 `4 5 6`、底部 `7 8 9` 的顺序一致。引擎内部继续使用小白原键位编码 `64486`，候选词和词库匹配不受显示转换影响。

## 输入方式

手柄 → C# 控制器 → 原版 librime API → C# 候选面板 → 当前所选入口的 TSF 编辑会话 → 当前文本框。

自动切换通过 `GetGUIThreadInfo().hwndFocus` 找到实际接收键盘输入的控件，再使用一个短时、指定线程的 Windows 消息钩子，在该控件所属线程中调用 TSF API，读取原输入法的完整标识（或英文等键盘布局句柄），并执行切换和恢复。辅助组件在目标线程再次检查焦点；C# 只连接同一输入线程的 TSF 端点。32 位和 64 位目标使用各自的辅助组件。它不是键盘钩子，不记录按键，也不模拟 Win + Space、Alt + Shift 等快捷键；请求完成即卸下钩子。

接口依据：[GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo)、[SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw)、[GetActiveProfile](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-getactiveprofile)、[ActivateProfile](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-activateprofile)。

九键模式直接调用 Rime API，中文和标点通过 TSF 处理，不模拟拼音按键或粘贴。Rime 内部的 `KP_*` 编码只是函数参数，不会发送到 Windows 键盘队列。正在输入的拼音编码由引擎直接退格，不发送 Backspace。

数字模式输入 0–9 时使用 `SendInput` 发送小键盘数字。事件带有专用标记，目标组件在确认当前焦点后临时放行这些数字，避免小白把它们再次解释为拼音编码；实体小键盘事件仍由原有逻辑处理。程序不切换 NumLock，也不模拟 Enter、空格等其他功能键。

备用 TSF 组件不拦截键盘按键，数字经过焦点检查后直接交给文本框；无需小白专用的数字放行接口。

**已上屏文字的退格兼容**：X 优先请求 TSF 删除；如果组件不提供该能力，或目标返回“不支持此编辑操作”（原先显示“目标控件未开放完整文档”），则自动发送一组 Backspace 按下和松开。在九键、数字模式下都适用，面板显示“已发送 Backspace”。这是小键盘数字之外明确允许的按键模拟。

发送 Backspace 前重新核对目标文本框及焦点令牌，并检查 Ctrl、Alt、Shift、Win 等修饰键。焦点变化、一般编辑失败或结果不明的超时不会补发 Backspace，避免误删或重复删除。兼容模式的具体删除行为由目标控件决定，与该控件处理实体 Backspace 一致。

手柄使用独立的 Rime 会话和学习数据。**它与实体键盘共用小白输入法入口，但不共用正在输入的拼音、候选状态或词频数据库。** 实体键盘正在组词时，手柄提交暂停，以免插入到该预编辑内容中。

## 安装方式和恢复

安装脚本只更改小白现有 COM 类 `{A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A}` 在 32 位和 64 位注册表视图中的 `InprocServer32` 路径，指向 `C:\Program Files\GamePadT9\components\<架构-哈希>\weasel-gamepad.dll`。

小白原输入法配置文件 `{3D02CAB6-2B8E-4781-BA20-1C9267529467}`、Windows 键盘布局及原版 DLL 文件保留。原始组件路径和校验值备份在 `C:\Program Files\GamePadT9\components\original-registration.json`。

组件安装需要正常的 Windows UAC 授权。脚本在修改路径前要求两种架构的当前构建均通过隔离输入测试，并按 SHA256 核对测试版本；安装失败会回退本次路径修改。

关闭 GamePad T9 后，可以恢复小白原组件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-xiaobai.ps1 -Restore
```

恢复后重新打开目标程序。此操作不卸载小白 T9，也不删除用户词库。旧命令 `scripts/register.ps1` 现在转交联动组件安装，`-Unregister` 转交恢复，不再注册独立输入法。

## 构建与复测

需要 .NET 10 SDK、x86 .NET 10 Windows Desktop Runtime、Visual C++ x64/x86 工具链、ATL 和 Windows SDK。64 位测试编辑器还需要 x64 .NET 10 Windows Desktop Runtime。同级 `../xiaobai-t9` 源码应存在，原版小白输入法应已完成一次方案部署。

先从托盘退出 GamePad T9，再在本项目目录执行：

```powershell
# 首次准备本地方案与设置；已有配置时不必重复。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare.ps1

# 构建 C# 主程序及两种架构的小白联动组件。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1

# 基础检查及隔离测试：尚不更改系统组件路径。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor -Architecture x86

# 安装通过上述测试的版本，再验证实际安装路径下的组件。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-xiaobai.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor -Installed
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Editor -Installed -Architecture x86
```

`prepare.ps1` 默认引用 `C:/Program Files/Rime/xiaobait9-2026.08.04`，可指定 `-InstallRoot` 和 `-RimeUserRoot`。它复制已部署方案与 Lua 支持文件到 `data/installed`，生成本地 `gamepadt9.json`，不复制原用户学习数据库。手柄学习数据与日志单独保存在 `artifacts/rime-user`。

`build-xiaobai.ps1` 从同级仓库复制 WeaselTSF、WeaselIPC、WeaselUI 等源码到 `artifacts/xiaobai-source-<架构>` 后应用补丁，不修改原仓库。首次构建自动下载并校验官方 Boost 1.84.0 压缩包；这个版本与已安装小白服务端的序列化格式 20 匹配。构建使用 C++17、静态运行库，不要求把原组件转成 C#。

构建产物使用带哈希的目录，当前路径记录在 `artifacts/xiaobai-x64-path.txt` 和 `artifacts/xiaobai-x86-path.txt`。`build.ps1` 同时构建 x64 / x86 输入法切换辅助组件，路径记录在 `artifacts/input-method-<架构>-path.txt`；这些辅助组件无需注册。只改 C# 时可以运行 `scripts/build.ps1 -SkipXiaobai -SkipInputMethodControl`。

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

2026-09-08，Release 构建和 18 项基础检查通过，检测到 XInput 手柄槽位 0。两种架构的隔离测试、实际安装测试均通过：

| 测试 | 报告 |
|---|---|
| 基础控制器和原版引擎 | [self-test.json](artifacts/self-test.json) |
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

端到端检查使用合成 XInput 状态驱动生产 `Controller` 和 `InputSession`，通过 TSF 编辑回执和 UI Automation 独立读取文档确认结果。UI Automation 用于定位、聚焦、读取及关闭测试标签页；输入文字使用生产输入路径。完整 TSF 文档的 WPF 检查中，每组模拟键盘事件恰为 20 次，全部来自数字 0–9；Backspace 兼容专项检查中，三次上屏退格产生 6 次键盘事件。候选编码的顶部 123 显示也已验证。本轮自动化检查不等同于逐项实体手柄体验测试。

截图：[九键候选](artifacts/overlay-t9.png)、[数字模式](artifacts/overlay-numeric.png)。

新版记事本现已通过完整检查：主窗口与编辑区位于不同线程，View + Menu 将编辑线程切换到 GamePad T9，主窗口线程保持原输入法；成功上屏“你好”和 `1234567890`，长按 B 恢复编辑线程原输入法，英文布局和原中文输入法均已验证。报告包含两个线程的标识、切换前后及恢复后的输入法，以及文档独立读回结果。早期“面板已开启但记事本未切换”的原因是选中了主窗口线程，现已修复；详见 [记事本报告](artifacts/notepad-verification.json)。

## 当前范围

- C# 宿主为 x86，匹配安装包中的 32 位 `rime.dll`；TSF 组件覆盖 x64 和 x86。ARM64 尚未验证。
- 候选和编码显示在浮窗；确认时文字进入目标文本框，尚无目标内的预编辑下划线。
- 完整 TSF 文档支持直接删除选中文本、普通字符、UTF-16 代理对和 CRLF；仅提供临时输入上下文的旧控件自动使用 Backspace 兼容处理。复杂组合表情的删除粒度不作统一保证。
- 当前不屏蔽游戏收到的手柄操作。独占全屏游戏、管理员程序及其他特殊编辑控件需要分别验证，不能保证所有程序兼容。
- 自动切换需要目标允许消息钩子和 TSF 调用；权限更高或阻止外部钩子的程序可能拒绝操作。强制结束进程或系统异常时不能保证执行恢复。
- 提交失败或回执超时不自动重发；面板会提示检查结果，按 B 清除后继续。
- 更新小白安装包后，其安装程序可能恢复自身组件路径；需按匹配的新版本重新构建和验证联动组件。

首次集成时发现 Boost 版本与原服务端不匹配，曾导致加载该版本的部分程序异常退出。已恢复原组件后修复版本匹配和异常边界，并改为先做进程隔离验证；上表结果来自修复后的组件。

## 代码结构

| 文件 | 职责 |
|---|---|
| `src/GamePadT9/GamePadApplication.cs` | 托盘、后台采样、连接状态与窗口生命周期 |
| `src/GamePadT9/Controller.cs` | 右摇杆分区、边沿、长按和连发 |
| `src/GamePadT9/InputSession.cs` | 模式、候选、焦点绑定与提交协调 |
| `src/GamePadT9/MainForm.cs` | 不抢焦点的置顶九宫格与候选绘制 |
| `src/GamePadT9/RimeEngine.cs` | 原版 librime C ABI 与独立会话 |
| `src/GamePadT9/NumericInput.cs` | 数字模式限定的小键盘 0–9 事件 |
| `src/GamePadT9/BackspaceInput.cs` | 带焦点和修饰键检查的 Backspace 兼容处理 |
| `src/GamePadT9/TsfClient.cs` | 带焦点令牌、请求编号和回执的跨进程命令 |
| `src/GamePadT9/InputMethodSwitcher.cs` | 原输入法记录、自动切换、窗口跟随和恢复 |
| `native/InputMethodControl.cpp` | 在目标线程读取及切换 TSF 输入法的短时消息钩子 |
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
