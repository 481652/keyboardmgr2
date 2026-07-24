# keyboardmgr2

键盘鼠标自动化工具，提供连点、连发、摸鱼(Boss Key)等功能。

## 构建环境

- **VS 2022** 或 **MSBuild 17.x**
- **目标框架**: .NET Framework 4.7.2
- **语言**: VB.NET / WPF + WinForms(窗体选取器)
- 解决方案包含两个工程：`keyboardmgr2`（主程序 WPF）和 `WindowSelector`（WinForms 选择器类库）

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

## 系统要求

- **Windows 10 1809 (Build 17763) 或更高版本**
- Mica 云母效果需要 Win11 22H2 (Build 22621+)，低于此版本自动降级
- x86/x64 均支持

## 主要功能

1. **连点** — 左键、右键、或自定义键盘按键的自动连续点击，支持随机速度偏移和坐标偏移
2. **连发** — 批量文本短语连续发送（多条目、循环开关），可保存/加载预设
3. **摸鱼(Boss Key)** — 一键最小化所有非工作窗口、将指定工作窗口前置；再按恢复
4. **悬浮窗** — 屏幕顶部悬浮控制栏，支持始终显示/始终隐藏/自动收缩三种模式
5. **全局热键** — 终止任务(9000)、摸鱼(9001)、连点开关(9002)、连发开关(9003)，支持自定义组合键
6. **深浅色主题** — 自动跟随系统或手动选择，支持 Mica 云母效果
7. **托盘图标** — 右键菜单直达各功能，左键显示主窗体

## 工程结构

```
keyboardmgr2/
  Application.xaml(.vb)     — 应用程序入口
  MainWindow.xaml(.vb)      — 主设置窗体
  FloatingWindow.xaml(.vb)  — 悬浮窗 + 全局热键注册/处理
  expWindow(.vb)            — 异常信息对话框
  HelpWindow(.vb)           — 帮助弹窗
  MyMsgbox(.vb)             — 自定义消息框
  MyTrayicon.vb             — 托盘图标
  GlobalMouseHook.vb        — 全局鼠标钩子（窗体选取用）
  Modules/
    DlgModule.vb            — 对话框 + 全局状态变量
    LoafModule.vb           — 摸鱼模式（窗口最小化/恢复）
    SettingsModule.vb       — 注册表读写
    ThemeModule.vb          — 主题切换 + Mica
    UserInputHandler.vb     — 键盘输入、按键发送、鼠标操作
WindowSelector/
  Selector.vb               — 枚举可见窗口
  WindowSelectorDlg(.vb)    — WinForms 窗口选取对话框
```

## 设置存储位置

所有设置保存在注册表 `HKEY_CURRENT_USER\Software\LCS\keyboardmgr`。

如需完全重置，可删除该键树后重启程序。
