# keyboardmgr2

键盘鼠标自动化工具，提供连点、连发、摸鱼(Boss Key)等功能。

## 构建环境

- **VS 2022** 或 **MSBuild 17.x**
- **目标框架**: .NET Framework 4.7.2
- **语言**: VB.NET / WPF + WinForms(窗体选取器) + **C++(键鼠操作内核)**
- 解决方案包含三个工程：`keyboardmgr2`（主程序 WPF）、`WindowSelector`（WinForms 选择器类库）、`OpCore`（键鼠操作内核原生 DLL，C++）
- 构建 `OpCore` 需要安装 **"使用 C++ 的桌面开发"** 工作负载（MSVC v143/v145 工具集 + Windows 10 SDK），否则该工程无法编译；缺失时主程序会自动回退到兼容内核

## 依赖包 (NuGet)

| 包名 | 版本 | 用途 |
|------|------|------|
| ControlzEx | 4.4.0 | 窗口效果增强 |
| Microsoft.Web.WebView2 | 1.0.2903.40 | WebView2 组件 |
| Microsoft.Windows.SDK.BuildTools | 10.0.22621.756 | WinRT API 互操作 |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.19 | WPF 行为交互 |
| System.Resources.Extensions | 4.7.1 | 资源扩展 |
| System.ValueTuple | 4.5.0 | 值元组支持 |

## 构建命令

```powershell
# 还原包并编译（Debug）
MSBuild.exe keyboardmgr2.sln /t:Rebuild /p:Configuration=Debug /v:minimal

# Release
MSBuild.exe keyboardmgr2.sln /t:Rebuild /p:Configuration=Release /v:minimal
```

构建 `OpCore` 时会在 x64 构建完成后自动联动构建 Win32，并把 `OpCore64.dll` / `OpCore32.dll` 复制到 `keyboardmgr2\bin\$(Configuration)\` 下供主程序按进程位数加载。

## 系统要求

- **Windows 10 1809 (Build 17763) 或更高版本**
- Mica 云母效果需要 Win11 22H2 (Build 22621+)，低于此版本自动降级
- x86/x64 均支持

## 主要功能

1. **连点** — 左键、右键、或自定义键盘按键的自动连续点击，支持随机速度偏移和坐标偏移。执行由操作内核（OpCore.dll，C++ 高精度计时）完成，可在选项中选择原生/兼容内核
2. **连发** — 批量文本短语连续发送（多条目、循环开关）：自动识别目标窗口（可手动选取），文本经 UI Automation 直接填充到目标输入框（回退 WM_SETTEXT / 操作内核模拟键盘），发送方式支持快捷键（Enter/Shift+Enter）、点击“发送”按钮、自动（先按钮后快捷键）；图片经剪贴板粘贴；可保存/加载预设
3. **摸鱼(Boss Key)** — 一键最小化所有非工作窗口、将指定工作窗口前置；再按恢复
4. **录制** — 全局键盘/鼠标钩子捕获按键与鼠标按钮，鼠标位置按用户设置的频率（1-1000ms）采样；支持开始/停止录制（全局热键）、平滑插值回放、倍速与循环、回放时阻止用户输入；录制文件为 `.kbmr` 二进制格式（版本号、采样频率、分辨率、事件流、Deflate 压缩与 SHA256 校验）；可导出为 PowerShell 脚本
5. **脚本** — 内置 PowerShell 5.1 运行器，提供键鼠模拟、阻止用户输入（BlockInput）、屏幕取色与区域截屏、窗口枚举与控制、剪贴板、启动程序等自动化命令；导入/首次运行时进行 AST 静态安全分析并列出脚本将执行的操作，用户可勾选信任（按文件 SHA256 记忆，文件修改后重新提醒）
6. **悬浮窗** — 屏幕顶部悬浮控制栏，支持始终显示/始终隐藏/自动收缩三种模式
7. **全局热键** — 终止全部任务(9000)、摸鱼(9001)、连点开关(9002)、连发开关(9003)、主界面显示/隐藏(9004)、录制开关(9005，默认 Ctrl+F4)，支持自定义组合键
8. **深浅色主题** — 自动跟随系统或手动选择，支持 Mica 云母效果
9. **托盘图标** — 右键菜单直达各功能，左键显示主窗体

## 工程结构

```
keyboardmgr2/
  Application.xaml(.vb)     — 应用程序入口
  MainWindow.xaml(.vb)      — 主设置窗体（含录制页与脚本页）
  FloatingWindow.xaml(.vb)  — 悬浮窗 + 全局热键注册/处理
  expWindow(.vb)            — 异常信息对话框
  HelpWindow(.vb)           — 帮助弹窗
  MyMsgbox(.vb)             — 自定义消息框
  MyTrayicon.vb             — 托盘图标
  GlobalMouseHook.vb        — 全局鼠标钩子（窗体选取用）
  ScriptSecurityWindow.xaml(.vb) — 脚本安全提醒对话框
  Modules/
    DlgModule.vb            — 对话框 + 全局状态变量
    LoafModule.vb           — 摸鱼模式（窗口最小化/恢复）
    SettingsModule.vb       — 注册表读写
    ThemeModule.vb          — 主题切换 + Mica
    UserInputHandler.vb     — 键盘输入、按键发送、鼠标操作、文本直插、发送按钮查找
    OpEngine.vb             — 操作内核分发（原生/兼容），统一键鼠操作接口
    OpCoreNative.vb         — 原生操作内核 P/Invoke 封装
    OpCoreManaged.vb        — 兼容（托管）操作内核
    MacroModel.vb           — 录制事件与录制对象模型
    KbmrCodec.vb            — .kbmr 录制文件编解码（Deflate + SHA256）
    InputRecorder.vb        — 录制捕获（全局钩子 + 鼠标位置采样）
    MacroPlayer.vb          — 录制回放（插值平滑/倍速/循环/BlockInput）+ 脚本导出
    KeyNameMapper.vb        — 按键名称与虚拟键码互转
    ScriptHost.vb           — 脚本宿主 API（输入/取色/截屏/窗口/剪贴板）
    ScriptRunner.vb         — PowerShell 5.1 运行器（停止/输出重定向）
    ScriptSecurity.vb       — 脚本 AST 安全分析 + SHA256 信任列表
OpCore/
  OpCore.h / OpCore.cpp     — 键鼠操作原生内核（高精度连点循环 + 鼠标/键盘/文本注入）
  OpCore.vcxproj            — 输出 OpCore32.dll / OpCore64.dll
WindowSelector/
  Selector.vb               — 枚举可见窗口
  WindowSelectorDlg(.vb)    — WinForms 窗口选取对话框
```

## 脚本说明

脚本页直接运行 PowerShell 5.1 脚本（引用系统自带 `System.Management.Automation`，无需额外安装）。可用命令：

| 类别 | 命令 |
|------|------|
| 基础输入 | `Delay` `Move` `Click` `DoubleClick` `MouseDown` `MouseUp` `Wheel` `Drag` `KeyDown` `KeyUp` `KeyTap` `Combo` `TypeText` |
| 阻止输入 | `BlockInput $true/$false`（Ctrl+Alt+Del 可强制解除） |
| 屏幕读取 | `Get-CursorPos` `Get-PixelColor` `Get-CursorColor` `Save-ScreenRegion` |
| 窗口 | `Get-Windows` `Get-ForegroundWindow` `Set-ForegroundWindow` `Get-WindowRect` `Close-Window` `Move-Window` |
| 其他 | `Get-ClipboardText` `Set-ClipboardText` `Start-App`，其余可直接使用原生 PowerShell/.NET |

安全机制：导入或运行未信任脚本时弹出安全提醒，按 AST 分析列出脚本将执行的操作分类；勾选“信任此脚本”后按文件 SHA256 记忆，文件修改后需重新确认。脚本以当前用户权限运行，请只运行来源可信的脚本。

## 设置存储位置

所有设置保存在注册表 `HKEY_CURRENT_USER\Software\LCS\keyboardmgr`。

如需完全重置，可删除该键树后重启程序。
