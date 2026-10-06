# 键鼠管家 KeyboardManager

键盘鼠标自动化工具，提供连点、连发、摸鱼(Boss Key)等功能。

<div align="center">
  
**[下载旧版（Win 10 1809以下用户）](https://github.com/481652/keyboardmgrclassic)** | **[访问项目官网](https://kbm.lcs123.top/)** | **[项目讨论帖](https://sysbbs.cn/d/512)**

</div>

## 功能特性

### 连点
 - 鼠标左右键与键盘单键的连续点击。
 - 点击间隔支持随机偏移
 - 鼠标点击位置可在屏幕内随机漂移
 - 支持模拟长按

### 连发
 - 文字与图片的列表式连发。
 - 每条内容发送后自动回车换行
 - 列表可保存为 .lcslst2 文件并重新加载

### 摸鱼
 - 用全局热键管理窗口。
 - 一键最小化其余窗口，仅保留选定窗口置顶（全局老板键）
 - 再次按键恢复全部窗口

## 开发中的功能
> [!TIP]
> 尚处于开发的功能，不保证将来功能实现与此处描述完全相符。
> 加*的项目尚处于设想阶段，未考虑其可行性，可能不会加入程序中。

### 悬浮窗信息显示
 - 在悬浮窗显示您键盘鼠标的一些信息，如光标坐标、光标移速、光标移动距离和键盘按键数等。
 - 可根据选择开启显示项

### 录制
 - 录制键鼠操作。

### 脚本
 - 使用powershell脚本来实现对键鼠自动化。
 - 提供丰富的操作接口
 - *可视化编辑器，用于快捷地编写脚本


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

## 设置存储位置

所有设置保存在注册表 `HKEY_CURRENT_USER\Software\LCS\keyboardmgr`。

如需完全重置，可删除该键树后重启程序。
