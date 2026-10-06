#include "OpCore.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <algorithm>
#include <mutex>
#include <thread>
#include <vector>
#include <random>

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

#ifndef TIMER_ALL_ACCESS
#define TIMER_ALL_ACCESS 0x1F0003
#endif

namespace {

std::mutex g_mutex;
std::thread g_worker;
HANDLE g_timer = nullptr;
HANDLE g_stopEvent = nullptr;
bool g_running = false;

bool g_holdingMouse = false;
int g_heldMouseButton = OPCORE_MOUSE_LEFT;
bool g_holdingKeys = false;
std::vector<unsigned short> g_heldKeys;

bool IsModifierKey(unsigned short key)
{
    return key == 0xA0 || key == 0xA1 || key == 0xA2 || key == 0xA3 ||
           key == 0xA4 || key == 0xA5 || key == 0x10 || key == 0x11 ||
           key == 0x12 || key == 0x5B || key == 0x5C;
}

void SendKeyDown(unsigned short vk)
{
    INPUT input = {};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = vk;
    SendInput(1, &input, sizeof(INPUT));
}

void SendKeyUp(unsigned short vk)
{
    INPUT input = {};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = vk;
    input.ki.dwFlags = KEYEVENTF_KEYUP;
    SendInput(1, &input, sizeof(INPUT));
}

void SendKeyComboDown(const unsigned short* keys, int count)
{
    for (int i = 0; i < count; ++i)
    {
        if (IsModifierKey(keys[i])) SendKeyDown(keys[i]);
    }
    for (int i = 0; i < count; ++i)
    {
        if (!IsModifierKey(keys[i])) SendKeyDown(keys[i]);
    }
}

void SendKeyComboUp(const unsigned short* keys, int count)
{
    for (int i = count - 1; i >= 0; --i)
    {
        SendKeyUp(keys[i]);
    }
}

DWORD MouseDownFlag(int button)
{
    return (button == OPCORE_MOUSE_RIGHT) ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
}

DWORD MouseUpFlag(int button)
{
    return (button == OPCORE_MOUSE_RIGHT) ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
}

void SendMouseEvent(DWORD flag)
{
    INPUT input = {};
    input.type = INPUT_MOUSE;
    input.mi.dwFlags = flag;
    SendInput(1, &input, sizeof(INPUT));
}

void ReleaseHolds()
{
    if (g_holdingMouse)
    {
        SendMouseEvent(MouseUpFlag(g_heldMouseButton));
        g_holdingMouse = false;
        g_heldMouseButton = OPCORE_MOUSE_LEFT;
    }
    if (g_holdingKeys)
    {
        SendKeyComboUp(g_heldKeys.data(), (int)g_heldKeys.size());
        g_holdingKeys = false;
        g_heldKeys.clear();
    }
}

void CleanupHandles()
{
    if (g_timer)
    {
        CloseHandle(g_timer);
        g_timer = nullptr;
    }
    if (g_stopEvent)
    {
        CloseHandle(g_stopEvent);
        g_stopEvent = nullptr;
    }
}

// 注意：调用前必须已持有 g_mutex，且工作线程不持有该锁，因此可安全 join。
void StopInternal()
{
    if (g_stopEvent) SetEvent(g_stopEvent);
    if (g_worker.joinable()) g_worker.join();
    ReleaseHolds();
    CleanupHandles();
    g_running = false;
}

unsigned MakeSeed()
{
    return (unsigned)GetTickCount64() ^ (unsigned)(size_t)GetCurrentThreadId();
}

void MouseClickWorker(int intervalMs, int randomSpeedOffset, int randomPosOffset, int baseX, int baseY, int mouseButton)
{
    int baseInterval = (intervalMs > 0) ? intervalMs : 1;
    int currentInterval = baseInterval;
    std::mt19937 rng(MakeSeed());
    std::uniform_int_distribution<int> speedDist(-10, 10);
    std::uniform_int_distribution<int> posDist(-15, 15);

    for (;;)
    {
        LARGE_INTEGER due;
        due.QuadPart = -((LONGLONG)currentInterval * 10000);
        SetWaitableTimer(g_timer, &due, 0, nullptr, nullptr, FALSE);

        HANDLE handles[2] = { g_timer, g_stopEvent };
        DWORD result = WaitForMultipleObjects(2, handles, FALSE, INFINITE);
        if (result == WAIT_OBJECT_0 + 1) break;
        if (result != WAIT_OBJECT_0) break;

        if (randomPosOffset)
        {
            int x = baseX + posDist(rng);
            int y = baseY + posDist(rng);
            SetCursorPos(x, y);
        }

        SendMouseEvent(MouseDownFlag(mouseButton));
        SendMouseEvent(MouseUpFlag(mouseButton));

        if (randomSpeedOffset)
        {
            currentInterval = baseInterval + speedDist(rng);
            if (currentInterval < 1) currentInterval = 1;
        }
        else
        {
            currentInterval = baseInterval;
        }
    }
}

void KeyClickWorker(int intervalMs, std::vector<unsigned short> keys)
{
    int keyCount = (int)keys.size();
    if (keyCount < 1) keyCount = 1;
    if (keyCount > 4) keyCount = 4;
    unsigned short localKeys[4] = { 0, 0, 0, 0 };
    for (int i = 0; i < keyCount; ++i) localKeys[i] = keys[i];

    int baseInterval = (intervalMs > 0) ? intervalMs : 1;

    for (;;)
    {
        LARGE_INTEGER due;
        due.QuadPart = -((LONGLONG)baseInterval * 10000);
        SetWaitableTimer(g_timer, &due, 0, nullptr, nullptr, FALSE);

        HANDLE handles[2] = { g_timer, g_stopEvent };
        DWORD result = WaitForMultipleObjects(2, handles, FALSE, INFINITE);
        if (result != WAIT_OBJECT_0) break;

        if (keyCount == 1)
        {
            SendKeyDown(localKeys[0]);
            SendKeyUp(localKeys[0]);
        }
        else
        {
            SendKeyComboDown(localKeys, keyCount);
            Sleep(50);
            SendKeyComboUp(localKeys, keyCount);
        }
    }
}

} // namespace

// ---- 连点循环 ----

int __stdcall OpCore_StartMouseClick(int intervalMs, int randomSpeedOffset, int randomPosOffset, int baseX, int baseY, int mouseButton)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    StopInternal();

    g_timer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_timer || !g_stopEvent)
    {
        CleanupHandles();
        return 1;
    }

    g_running = true;
    g_worker = std::thread(MouseClickWorker, intervalMs, randomSpeedOffset, randomPosOffset, baseX, baseY, mouseButton);
    return 0;
}

int __stdcall OpCore_StartKeyClick(int intervalMs, const unsigned short* keys, int keyCount)
{
    if (!keys || keyCount < 1)
    {
        return 2;
    }

    std::lock_guard<std::mutex> lock(g_mutex);
    StopInternal();

    std::vector<unsigned short> keyCopy;
    keyCopy.reserve(4);
    for (int i = 0; i < keyCount && i < 4; ++i) keyCopy.push_back(keys[i]);
    if (keyCopy.empty()) return 2;

    g_timer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_timer || !g_stopEvent)
    {
        CleanupHandles();
        return 1;
    }

    g_running = true;
    g_worker = std::thread(KeyClickWorker, intervalMs, keyCopy);
    return 0;
}

int __stdcall OpCore_StartMouseHold(int button)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    StopInternal();

    int heldButton = (button == OPCORE_MOUSE_RIGHT) ? OPCORE_MOUSE_RIGHT : OPCORE_MOUSE_LEFT;
    SendMouseEvent(MouseDownFlag(heldButton));
    g_holdingMouse = true;
    g_heldMouseButton = heldButton;
    g_running = true;
    return 0;
}

int __stdcall OpCore_StartKeyHold(const unsigned short* keys, int keyCount)
{
    if (!keys || keyCount < 1)
    {
        return 2;
    }

    std::lock_guard<std::mutex> lock(g_mutex);
    StopInternal();

    g_heldKeys.clear();
    g_heldKeys.reserve(4);
    for (int i = 0; i < keyCount && i < 4; ++i) g_heldKeys.push_back(keys[i]);
    if (g_heldKeys.empty()) return 2;

    SendKeyComboDown(g_heldKeys.data(), (int)g_heldKeys.size());
    g_holdingKeys = true;
    g_running = true;
    return 0;
}

int __stdcall OpCore_Stop(void)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    StopInternal();
    return 0;
}

int __stdcall OpCore_IsRunning(void)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_running ? 1 : 0;
}

// ---- 通用键鼠操作 ----

int __stdcall OpCore_MouseMove(int x, int y)
{
    return SetCursorPos(x, y) ? 0 : 1;
}

int __stdcall OpCore_MouseClick(int button, int x, int y)
{
    if (x >= 0 && y >= 0)
    {
        SetCursorPos(x, y);
    }
    SendMouseEvent(MouseDownFlag(button));
    SendMouseEvent(MouseUpFlag(button));
    return 0;
}

int __stdcall OpCore_KeyTap(unsigned short vk)
{
    SendKeyDown(vk);
    SendKeyUp(vk);
    return 0;
}

int __stdcall OpCore_KeyDown(unsigned short vk)
{
    SendKeyDown(vk);
    return 0;
}

int __stdcall OpCore_KeyUp(unsigned short vk)
{
    SendKeyUp(vk);
    return 0;
}

int __stdcall OpCore_KeyCombo(const unsigned short* keys, int keyCount)
{
    if (!keys || keyCount < 1)
    {
        return 2;
    }
    int count = (std::min)(keyCount, 4);
    unsigned short localKeys[4] = { 0, 0, 0, 0 };
    for (int i = 0; i < count; ++i) localKeys[i] = keys[i];
    SendKeyComboDown(localKeys, count);
    Sleep(50);
    SendKeyComboUp(localKeys, count);
    return 0;
}

int __stdcall OpCore_TypeText(const wchar_t* text)
{
    if (!text)
    {
        return 1;
    }
    for (const wchar_t* p = text; *p; ++p)
    {
        if (*p == L'\r' || *p == L'\n')
        {
            // 回车/换行统一发送一次 Enter，跳过 \r\n 重复
            if (*p == L'\r' && p[1] == L'\n') ++p;
            SendKeyDown(VK_RETURN);
            SendKeyUp(VK_RETURN);
            continue;
        }
        INPUT input[2] = {};
        input[0].type = INPUT_KEYBOARD;
        input[0].ki.wScan = (WORD)*p;
        input[0].ki.dwFlags = KEYEVENTF_UNICODE;
        input[1] = input[0];
        input[1].ki.dwFlags |= KEYEVENTF_KEYUP;
        SendInput(2, input, sizeof(INPUT));
    }
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_DETACH)
    {
        // 进程退出兜底：停止工作线程并释放按住的键鼠，避免“卡键”或线程未 join 导致异常。
        if (g_stopEvent) SetEvent(g_stopEvent);
        if (g_worker.joinable()) g_worker.join();
        ReleaseHolds();
    }
    return TRUE;
}
