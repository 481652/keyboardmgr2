#pragma once

#ifdef OPCORE_EXPORTS
#define OPCORE_API __declspec(dllexport)
#else
#define OPCORE_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

// 鼠标按键类型
#define OPCORE_MOUSE_LEFT  1
#define OPCORE_MOUSE_RIGHT 2

// ---- 连点循环（长按由 Stop 释放） ----

// 开始鼠标连点（左键/右键）
//   intervalMs          基础发送间隔（毫秒）
//   randomSpeedOffset   是否启用随机速度偏移（±10ms）
//   randomPosOffset     是否启用随机坐标偏移（±15px）
//   baseX/baseY         基准坐标（仅当 randomPosOffset 或自定义位置时有效）
//   mouseButton         1=左键 2=右键
OPCORE_API int __stdcall OpCore_StartMouseClick(int intervalMs, int randomSpeedOffset, int randomPosOffset, int baseX, int baseY, int mouseButton);

// 开始键盘连点（1~4 键组合）
OPCORE_API int __stdcall OpCore_StartKeyClick(int intervalMs, const unsigned short* keys, int keyCount);

// 开始鼠标长按（立即按下，由 Stop 释放）
OPCORE_API int __stdcall OpCore_StartMouseHold(int button);

// 开始键盘长按（修饰键先按下，由 Stop 释放）
OPCORE_API int __stdcall OpCore_StartKeyHold(const unsigned short* keys, int keyCount);

// 停止所有连点/长按并释放按住状态（幂等，可随时调用）
OPCORE_API int __stdcall OpCore_Stop(void);

// 是否正在运行（1=运行中，0=空闲）
OPCORE_API int __stdcall OpCore_IsRunning(void);

// ---- 通用键鼠操作 ----

// 移动鼠标到屏幕坐标 (x, y)
OPCORE_API int __stdcall OpCore_MouseMove(int x, int y);

// 鼠标单击：button 1=左键 2=右键；x/y 为负时在当前光标位置点击
OPCORE_API int __stdcall OpCore_MouseClick(int button, int x, int y);

// 键盘单击（按下并释放虚拟键码 vk）
OPCORE_API int __stdcall OpCore_KeyTap(unsigned short vk);

// 按下/释放虚拟键码 vk
OPCORE_API int __stdcall OpCore_KeyDown(unsigned short vk);
OPCORE_API int __stdcall OpCore_KeyUp(unsigned short vk);

// 发送组合键（修饰键先按下、逆序释放，中间保持 50ms）
OPCORE_API int __stdcall OpCore_KeyCombo(const unsigned short* keys, int keyCount);

// 输入文本（SendInput 逐字符 Unicode 注入，回车/换行转换为 Enter）
OPCORE_API int __stdcall OpCore_TypeText(const wchar_t* text);

#ifdef __cplusplus
}
#endif
