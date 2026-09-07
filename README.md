# GamePad T9 最小验证

Windows Xbox / XInput 手柄九键输入验证程序。C# 读取右摇杆和按键，直接调用**已安装的小白 T9 原版 32 位 Rime 引擎**。选词后，由独立的原生 TSF 组件通过编辑会话写入当前应用。

九键输入、选候选及中文上屏均不调用 `SendInput`、`keybd_event`、`SendKeys`、剪贴板粘贴或键盘消息模拟。Rime 内部的 `KP_*` 编码只是引擎函数参数，不进入 Windows 键盘消息队列。

## 运行

1. 双击 `Start.cmd`，或运行 `artifacts/app/GamePadT9.exe`。
2. 打开记事本，在 Windows 输入法列表中选择 **GamePad T9 验证**。小白 T9 与验证输入法是两个独立条目。
3. 将光标放入记事本文本框，连接 Xbox / XInput 手柄。
4. 同时按 **View + Menu** 开启输入；也可以点击浮窗的“开启手柄输入”。浮窗不会抢走文本框焦点。
5. 右摇杆选区，**RT 或 R3** 输入编码；**A** 确认当前高亮候选。

| 左列 | 中列 | 右列 |
|---|---|---|
| 符号入口（原版 7，包含字母候选） | ABC | DEF |
| GHI | JKL | MNO |
| PQRS | TUV | WXYZ |

摇杆松开时选中 JKL。按住 RT 不连发；RT 与 R3 同时按下只输入一次。R3 使用按下前一采样的选区，减少按压造成的偏移。

| 操作 | 本版行为 |
|---|---|
| LB / RB | 上一个 / 下一个候选 |
| 十字键左 / 右 | 前一页 / 后一页 |
| X | 删除尚未提交的编码 |
| B 短按 | 取消当前组合及候选 |
| B 持续 1 秒 | 关闭手柄输入 |
| View + Menu | 开启 / 关闭手柄输入 |

**输入“你好”**：依次选择 MNO → GHI → GHI → ABC → MNO，每个区域按一次 RT，再按 A。原版编码为 `64486`。

本版集中验证九键链路，尚未实现 Y 切换数字、小键盘数字模拟、已上屏文字退格、游戏手柄屏蔽、其他扩展组合键。候选和编码显示在浮窗中；目标文本框仅在确认候选时收到文字，没有原位预编辑下划线。

## 已验证与限制

- C# 与原生组件 Release 编译通过。
- 实际加载安装包 `C:/Program Files/Rime/xiaobait9-2026.08.04/rime.dll`，使用 `xiaobai_simp` 方案。
- 自动检查覆盖连接时已按住按钮、RT 去重、分区迟滞、RT/R3 去重、长短 B 分离及真实引擎候选/退格。
- `--verify-notepad` 创建专用空白文档，用手柄状态样本经过生产 Controller 和 Rime 代码生成“你好”，通过生产 TSF 通道提交，并用 UI Automation 独立读取记事本文本确认结果。UI Automation 只用于定位、聚焦和读取，不负责写字。
- 实测当时没有连接 XInput 手柄，实体手感、蓝牙重连和按键时序还需要手柄实测。状态样本不是系统键盘模拟。
- 当前 TSF DLL 为 **x64**，已验证 x64 记事本；C# 宿主为 **x86**，匹配安装包的原版 Rime。其他位数应用、游戏、管理员应用、特殊编辑控件尚未验证。
- XInput 读取不会屏蔽游戏收到手柄操作，本版不安装手柄驱动。
- 原始小白安装目录及原用户词库不做修改。预编译方案复制到 `data/installed/build`，本程序学习数据和日志单独保存在 `artifacts/rime-user`。
- 失去原文本框焦点时取消未提交编码；提交失败或回执超时不自动重试，避免重复上屏。

结果文件：`artifacts/self-test.json`、`artifacts/notepad-verification.json`。记事本验证留下一个以 `GamePadT9-TSF-` 开头的测试标签页，文字可能尚未保存到磁盘。

## 构建与复测

需要 .NET 10 SDK、x86 .NET 10 Windows Desktop Runtime、Visual C++ x64 工具链和 Windows SDK。已安装的原版小白输入法需要完成一次方案部署。

在项目目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Notepad
```

`prepare.ps1` 可接受 `-InstallRoot` 和 `-RimeUserRoot`。它引用安装包运行库、复制已部署 `.yaml` / `.bin` 和 Lua 支持脚本，生成本地 `gamepadt9.json`；不复制原用户学习数据库。源代码中的 Rime 互操作声明对应同级 `../xiaobai-t9/librime/src/rime_api.h`。

`register.ps1` 通过正常 UAC 提权注册独立 TSF 输入法；注册写入本输入法 GUID 对应的 COM 和 TSF 项。注册之后，组件可能被记事本等进程加载，修改 C++ 代码再构建前应关闭这些程序。C# 部分可独立使用 `dotnet build src/GamePadT9/GamePadT9.csproj -c Release -o artifacts/app`。

测试记事本时使用 TSF API 激活验证输入法，会改变当前会话所选输入法，完成后可以从 Windows 输入法列表切回小白。不替换 Windows 默认输入法配置。

## 卸载验证输入法

关闭 GamePad T9，切换到其他输入法，然后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register.ps1 -Unregister
```

卸载只移除本验证输入法的注册信息，不卸载原版小白 T9。关闭仍加载此组件的应用后，才能删除或移动项目文件夹。

## 结构与引用

```text
src/GamePadT9/Controller.cs        C# 手柄采样、分区、边沿和长按
src/GamePadT9/RimeEngine.cs        原版 librime C ABI 与候选状态
src/GamePadT9/TsfClient.cs         带焦点令牌、提交编号和回执的跨进程命令
src/GamePadT9/MainForm.cs          不激活目标窗口的九宫格与候选浮窗
native/Bridge.cpp                独立 TSF 服务与 ITfInsertAtSelection 编辑会话
native/Control.cpp               x64 注册 / 注销 / 激活工具
```

Rime 属于其原作者；小白 T9 及词库遵循各自原始许可，参见同级 `xiaobai-t9/LICENSE.txt`、`xiaobai-t9/librime/LICENSE` 和词库文件头。本验证不重新打包分发安装包内的引擎。`data/installed/sources.json` 记录本机引用的运行库及关键部署文件 SHA256。
