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

// 组合键按下→抬起之间的保持时长（毫秒，1~20，默认 3）。T14 (F-03)：替代原先固定的 Sleep(50)
volatile LONG g_comboHoldMs = 3;

LONGLONG QpcFrequency()
{
    static LONGLONG freq = 0;
    if (freq == 0)
    {
        LARGE_INTEGER f;
        QueryPerformanceFrequency(&f);
        freq = f.QuadPart;
    }
    return freq;
}

// 当前时间（100ns 单位，基于 QPC）
LONGLONG Now100ns()
{
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    LONGLONG freq = QpcFrequency();
    return (c.QuadPart / freq) * 10000000LL + (c.QuadPart % freq) * 10000000LL / freq;
}

// 用高精度可等待定时器等待 wait100ns，期间可被 stopEvent 打断。返回 false 表示应退出。
bool WaitOrStop(HANDLE timer, HANDLE stopEvent, LONGLONG wait100ns)
{
    if (wait100ns <= 0)
    {
        return WaitForSingleObject(stopEvent, 0) != WAIT_OBJECT_0;
    }
    LARGE_INTEGER due;
    due.QuadPart = -wait100ns;
    if (!SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE)) return false;
    HANDLE handles[2] = { timer, stopEvent };
    DWORD result = WaitForMultipleObjects(2, handles, FALSE, INFINITE);
    return result == WAIT_OBJECT_0;
}

// 绝对期限调度：nextDue += interval；落后超过一个周期时跳拍（不补发堆积的点击）
LONGLONG AdvanceDeadline(LONGLONG nextDue, LONGLONG interval100ns)
{
    nextDue += interval100ns;
    LONGLONG now = Now100ns();
    if (now - nextDue > interval100ns) nextDue = now + interval100ns;
    return nextDue;
}

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

    LONGLONG nextDue = Now100ns() + (LONGLONG)currentInterval * 10000;
    for (;;)
    {
        if (!WaitOrStop(g_timer, g_stopEvent, nextDue - Now100ns())) break;

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
        nextDue = AdvanceDeadline(nextDue, (LONGLONG)currentInterval * 10000);
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
    LONGLONG interval100ns = (LONGLONG)baseInterval * 10000;

    LONGLONG nextDue = Now100ns() + interval100ns;
    for (;;)
    {
        if (!WaitOrStop(g_timer, g_stopEvent, nextDue - Now100ns())) break;

        if (keyCount == 1)
        {
            SendKeyDown(localKeys[0]);
            SendKeyUp(localKeys[0]);
        }
        else
        {
            // T14 (F-03)：按下 → 高精度保持 g_comboHoldMs → 抬起；保持期间也可被 Stop 打断
            SendKeyComboDown(localKeys, keyCount);
            bool keepRunning = WaitOrStop(g_timer, g_stopEvent, (LONGLONG)g_comboHoldMs * 10000);
            SendKeyComboUp(localKeys, keyCount);
            if (!keepRunning) break;
        }
        // 期限从本轮开始时刻累加，按住时长计入周期，不再叠加到间隔上
        nextDue = AdvanceDeadline(nextDue, interval100ns);
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
    // T14 (F-03)：用独立的高精度定时器保持 g_comboHoldMs，替代 Sleep(50)
    HANDLE holdTimer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    if (holdTimer)
    {
        LARGE_INTEGER due;
        due.QuadPart = -((LONGLONG)g_comboHoldMs * 10000);
        if (SetWaitableTimer(holdTimer, &due, 0, nullptr, nullptr, FALSE))
        {
            WaitForSingleObject(holdTimer, 1000);
        }
        CloseHandle(holdTimer);
    }
    else
    {
        Sleep((DWORD)g_comboHoldMs);
    }
    SendKeyComboUp(localKeys, count);
    return 0;
}

int __stdcall OpCore_SetComboHoldMs(int holdMs)
{
    if (holdMs < 1 || holdMs > 20) return 2;
    InterlockedExchange(&g_comboHoldMs, holdMs);
    return 0;
}

int __stdcall OpCore_TypeText(const wchar_t* text)
{
    if (!text)
    {
        return 1;
    }
    INPUT inputs[256] = {};
    UINT count = 0;
    auto flush = [&]() -> bool
    {
        if (count == 0) return true;
        UINT requested = count;
        UINT sent = SendInput(count, inputs, sizeof(INPUT));
        count = 0;
        return sent == requested;
    };

    for (const wchar_t* p = text; *p; ++p)
    {
        if (*p == L'\r' || *p == L'\n')
        {
            // 回车/换行统一发送一次 Enter，跳过 \r\n 重复
            if (*p == L'\r' && p[1] == L'\n') ++p;
            INPUT down = {};
            down.type = INPUT_KEYBOARD;
            down.ki.wVk = VK_RETURN;
            inputs[count++] = down;
            down.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[count++] = down;
        }
        else
        {
            INPUT down = {};
            down.type = INPUT_KEYBOARD;
            down.ki.wScan = (WORD)*p;
            down.ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[count++] = down;
            down.ki.dwFlags |= KEYEVENTF_KEYUP;
            inputs[count++] = down;
        }
        if (count == (UINT)(sizeof(inputs) / sizeof(inputs[0])) && !flush()) return 1;
    }
    return flush() ? 0 : 1;
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
