// ============================================================================
// 键鼠管家性能基准 —— 任务1：连点内核间隔精度基准（原生 OpCore64.dll vs 托管 OpCoreManaged）
//
// 安全设计（硬性约束）：
//   * 400x400 无边框置顶窗口，光标固定在窗口中心，所有注入点击都落在自己的窗口上；
//   * 每 10ms 巡检：光标越界立即拉回；WindowFromPoint(中心) 不是本窗口 → 立即 Stop 并作废该轮；
//   * 结束后 OpCore_Stop / ManagedKernelBridge.StopAll，光标移回原位置；
//   * 进程退出兜底再次 Stop；看门狗在超过“预热+测量+3s”后强制 Stop。
//
// 统计：WM_LBUTTONDOWN / WM_LBUTTONDBLCLK（视为一次点击，Windows 对同点快速连击会发双击消息）
//       到达时间由 Stopwatch.GetTimestamp()（QPC）在 WndProc 内记录。
//
// 输出：.perf\data\bench-click.json
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    internal const string BenchRoot = @"D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\bench";
    internal const string DataRoot = @"D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\data";

    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => CrashLog("UnhandledException", e.ExceptionObject as Exception);
        System.Windows.Forms.Application.ThreadException += (s, e) => CrashLog("ThreadException", e.Exception);
        try
        {
            return RealMain(args);
        }
        catch (Exception ex)
        {
            CrashLog("Main", ex);
            return 1;
        }
    }

    static void CrashLog(string where, Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("where=" + where);
            if (ex == null) { sb.AppendLine("exception object is null"); }
            else
            {
                sb.AppendLine("type=" + ex.GetType().FullName);
                sb.AppendLine("message=" + ex.Message);
                sb.AppendLine("stack=" + ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("inner.type=" + ex.InnerException.GetType().FullName);
                    sb.AppendLine("inner.message=" + ex.InnerException.Message);
                    sb.AppendLine("inner.stack=" + ex.InnerException.StackTrace);
                }
            }
            sb.AppendLine("baseException=" + Convert.ToString(ex, CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(BenchRoot, @"logs\crash-click.txt"), sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }

    static int RealMain(string[] args)
    {
        string outPath = Path.Combine(DataRoot, "bench-click.json");
        int repeats = 3;
        int testMs = 2000;
        int prewarmMs = 500;
        int[] intervals = { 1, 5, 10, 25, 50, 100 };
        string[] kernels = { "native", "managed" };
        bool doKeyboard = true;
        bool preflight = false;
        bool run = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": outPath = args[++i]; break;
                case "--repeats": repeats = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--duration": testMs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--prewarm": prewarmMs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--intervals": intervals = args[++i].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(); break;
                case "--kernels": kernels = args[++i].Split(','); break;
                case "--no-keyboard": doKeyboard = false; break;
                case "--preflight": preflight = true; break;
                case "--run": run = true; break;
            }
        }

        if (!run && !preflight)
        {
            Console.WriteLine("bench-click: safety gate - refusing to inject input without --run");
            Console.WriteLine("  --run           execute the full click/keyboard matrix (INJECTS INPUT)");
            Console.WriteLine("  --preflight     window/cursor self-check only, NO injection");
            Console.WriteLine("  optional: --out <path> --repeats <n> --duration <ms> --prewarm <ms> --intervals <a,b,c> --kernels <k1,k2> --no-keyboard");
            return 0;
        }

        Directory.CreateDirectory(DataRoot);
        NativeWin.SetProcessDPIAware();

        // ---- 窗口 ----
        var form = new BenchForm();
        int w = 400, h = 400;
        Rectangle scr = Screen.PrimaryScreen.Bounds;
        int left = scr.Left + (scr.Width - w) / 2;
        int top = scr.Top + (scr.Height - h) / 2;
        form.Bounds = new Rectangle(left, top, w, h);
        form.Show();
        form.Activate();
        NativeWin.SetForegroundWindow(form.Handle);
        NativeWin.SetWindowPos(form.Handle, NativeWin.HWND_TOPMOST, 0, 0, 0, 0,
            NativeWin.SWP_NOMOVE | NativeWin.SWP_NOSIZE | NativeWin.SWP_SHOWWINDOW);
        POINT origCursor;
        NativeWin.GetCursorPos(out origCursor);
        int cx = left + w / 2, cy = top + h / 2;
        NativeWin.SetCursorPos(cx, cy);
        for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(20); }

        if (preflight)
        {
            IntPtr hForm = form.Handle;
            Thread.Sleep(300);
            IntPtr hit = NativeWin.WindowFromPoint(new POINT { x = cx, y = cy });
            IntPtr fg = NativeWin.GetForegroundWindow();
            NativeWin.SetCursorPos(origCursor.x, origCursor.y);
            bool handleOk = hit == hForm;
            Console.WriteLine("[preflight] handleAtCenter=" + handleOk +
                              " formHandle=0x" + hForm.ToInt64().ToString("X") +
                              " hit=0x" + hit.ToInt64().ToString("X") +
                              " foregroundIsForm=" + (fg == hForm) +
                              " screen=" + scr.Width + "x" + scr.Height +
                              " cursorRestored=" + origCursor.x + "," + origCursor.y);
            form.Close();
            return handleOk ? 0 : 2;
        }

        // ---- 加载原生内核 ----
        string dllPath = Path.Combine(Program.BenchRoot, @"bin\OpCore64.dll");
        NativeCore.Load(dllPath);
        uint tMin, tMax, tCurBefore;
        NativeWin.NtQueryTimerResolution(out tMin, out tMax, out tCurBefore);

        var ctx = new BenchContext
        {
            Form = form,
            CenterX = cx,
            CenterY = cy,
            WindowRect = new Rectangle(left, top, w, h),
            Repeats = repeats,
            TestMs = testMs,
            PrewarmMs = prewarmMs,
            Intervals = intervals,
            Kernels = kernels,
            DoKeyboard = doKeyboard,
            OutPath = outPath,
            TimerResolutionBefore = tCurBefore,
            StartedAt = DateTime.UtcNow,
        };

        var workerDone = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            try { ctx.Run(); }
            finally { workerDone.Set(); }
        });
        worker.IsBackground = true;
        worker.Name = "bench-orchestrator";
        worker.Start();

        // 若 worker 已结束但消息泵因窗口关闭失败而挂住，5 秒后强制退出（避免进程残留抢前台）
        var exitWatchdog = new Thread(() =>
        {
            if (workerDone.Wait(600000))
            {
                Thread.Sleep(5000);
                Console.Error.WriteLine("[watchdog] worker finished but message pump still alive; forcing exit");
                Environment.Exit(ctx.ExitCode);
            }
        });
        exitWatchdog.IsBackground = true;
        exitWatchdog.Start();

        Application.Run(form);          // UI 线程跑消息泵，直到 worker 关闭窗口
        workerDone.Wait(2000);
        worker.Join(15000);             // 兜底等待 worker 收尾
        NativeCore.Stop();              // 兜底：确保内核停止
        try { ManagedKernelBridge.StopAll(); } catch { }
        NativeWin.SetCursorPos(origCursor.x, origCursor.y);
        Console.WriteLine("[main] cursor restored to " + origCursor.x + "," + origCursor.y + "; exit=" + ctx.ExitCode);
        Environment.Exit(ctx.ExitCode);
        return ctx.ExitCode;
    }
}

// ============================================================================
// 接收窗口
// ============================================================================
class BenchForm : Form
{
    public const int Cap = 400000;
    public readonly long[] Ts = new long[Cap];
    public readonly int[] Msgs = new int[Cap];
    public readonly int[] Vks = new int[Cap];
    public int Count;
    public bool Recording;
    public long T0, T1;

    public BenchForm()
    {
        Text = "KbmrBenchSink";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(18, 18, 20);
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
        UpdateStyles();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var f = new Font("Segoe UI", 10f))
        using (var b = new SolidBrush(Color.FromArgb(120, 200, 160)))
        {
            e.Graphics.DrawString("Kbmr bench sink\r\n请勿移动鼠标 / Do not move the mouse", f, b, 14, 14);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (Recording)
        {
            int c = Count;
            if (c < Cap)
            {
                Ts[c] = Stopwatch.GetTimestamp();
                Msgs[c] = m.Msg;
                Vks[c] = m.WParam.ToInt32();
                Count = c + 1;
            }
        }
        base.WndProc(ref m);
    }

    public void StartRec()
    {
        Count = 0;
        T0 = Stopwatch.GetTimestamp();
        Recording = true;
    }

    public void StopRec()
    {
        Recording = false;
        T1 = Stopwatch.GetTimestamp();
    }

    public SnapShot TakeSnapshot()
    {
        var s = new SnapShot();
        s.Count = Count;
        s.T0 = T0;
        s.T1 = T1;
        s.Ts = new long[Count];
        s.Msgs = new int[Count];
        s.Vks = new int[Count];
        Array.Copy(Ts, s.Ts, Count);
        Array.Copy(Msgs, s.Msgs, Count);
        Array.Copy(Vks, s.Vks, Count);
        return s;
    }
}

class SnapShot
{
    public int Count;
    public long T0, T1;
    public long[] Ts;
    public int[] Msgs;
    public int[] Vks;
}

// ============================================================================
// 编排器
// ============================================================================
class BenchContext
{
    public BenchForm Form;
    public int CenterX, CenterY;
    public Rectangle WindowRect;
    public int Repeats, TestMs, PrewarmMs;
    public int[] Intervals;
    public string[] Kernels;
    public bool DoKeyboard;
    public string OutPath;
    public uint TimerResolutionBefore;
    public DateTime StartedAt;
    public int ExitCode;

    readonly List<Dictionary<string, object>> _clickRuns = new List<Dictionary<string, object>>();
    readonly List<Dictionary<string, object>> _keyRuns = new List<Dictionary<string, object>>();
    readonly List<string> _order = new List<string>();
    readonly Random _rng = new Random(Environment.TickCount);
    int _sequence;

    public void Run()
    {
        try
        {
            var combos = new List<KeyValuePair<string, int>>();
            foreach (string k in Kernels)
                foreach (int iv in Intervals)
                    combos.Add(new KeyValuePair<string, int>(k, iv));

            for (int rep = 0; rep < Repeats; rep++)
            {
                var shuffled = combos.OrderBy(_ => _rng.Next()).ToList();
                foreach (var combo in shuffled)
                {
                    var run = RunClickWithRetries(combo.Key, combo.Value, rep);
                    _clickRuns.Add(run);
                    Console.WriteLine("[click] " + combo.Key + " @" + combo.Value + "ms rep" + rep +
                                      " valid=" + Get(run, "valid") + " delivered=" + Get(run, "deliveredClicks") +
                                      " mean=" + Fmt(Get(run, "stats")) + " cpu%=" + Get(run, "cpuPercentOfOneCore"));
                }
            }

            if (DoKeyboard)
            {
                // 附加对比：原生 vs 托管，单键 A(0x41) 与组合键 Ctrl(0x11)+A，间隔均 50ms
                var cases = new List<KeyValuePair<string, KeyValuePair<string, ushort[]>>>
                {
                    MakeKeyCase("native-single-A", "native", new ushort[] { 0x41 }),
                    MakeKeyCase("native-combo-Ctrl+A", "native", new ushort[] { 0x11, 0x41 }),
                    MakeKeyCase("managed-single-A", "managed", new ushort[] { 0x41 }),
                    MakeKeyCase("managed-combo-Ctrl+A", "managed", new ushort[] { 0x11, 0x41 }),
                };
                for (int rep = 0; rep < Repeats; rep++)
                {
                    var shuffled = cases.OrderBy(_ => _rng.Next()).ToList();
                    foreach (var c in shuffled)
                    {
                        var run = RunKeyWithRetries(c.Key, c.Value.Key, c.Value.Value, rep);
                        _keyRuns.Add(run);
                        Console.WriteLine("[key] " + c.Key + " rep" + rep + " valid=" + Get(run, "valid") +
                                          " period=" + Get(run, "periodMedianMs") + "ms n=" + Get(run, "aDownCount"));
                    }
                }
            }

            uint tMin, tMax, tCurAfter;
            NativeWin.NtQueryTimerResolution(out tMin, out tMax, out tCurAfter);

            var meta = new Dictionary<string, object>
            {
                { "tool", "bench-click.exe" },
                { "generatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "startedUtc", StartedAt.ToString("o", CultureInfo.InvariantCulture) },
                { "os", Environment.OSVersion.VersionString },
                { "processorCount", Environment.ProcessorCount },
                { "is64BitProcess", Environment.Is64BitProcess },
                { "clrVersion", Environment.Version.ToString() },
                { "stopwatchFrequency", Stopwatch.Frequency },
                { "stopwatchIsHighResolution", Stopwatch.IsHighResolution },
                { "nativeDll", Path.Combine(Program.BenchRoot, @"bin\OpCore64.dll") },
                { "nativeDllSize", new FileInfo(Path.Combine(Program.BenchRoot, @"bin\OpCore64.dll")).Length },
                { "managedKernelSource", "keyboardmgr2\\Modules\\OpCoreManaged.vb + UserInputHandler.vb + GlobalMouseHook.vb 原始源码直接编译（vbc Release optimize+），OpEngine 为 2 成员垫片" },
                { "timerResolution100nsBefore", TimerResolutionBefore },
                { "timerResolution100nsAfter", tCurAfter },
                { "windowRect", new Dictionary<string, object> { { "x", WindowRect.X }, { "y", WindowRect.Y }, { "w", WindowRect.Width }, { "h", WindowRect.Height } } },
                { "windowCenter", new Dictionary<string, object> { { "x", CenterX }, { "y", CenterY } } },
                { "testDurationMs", TestMs },
                { "prewarmMs", PrewarmMs },
                { "repeats", Repeats },
                { "intervalsMs", Intervals },
                { "kernels", Kernels },
                { "mouseButton", 1 },
                { "randomSpeedOffset", false },
                { "randomPosOffset", false },
                { "caveat", "合成输入经系统消息队列投递，QPC 时间戳包含消息队列与线程调度延迟；测量值系“送达时刻”上界，真实注入时刻早于该时间戳。" },
                { "doubleClickNote", "同一坐标快速连击时 Windows 会把第 2 次起的 WM_LBUTTONDOWN 替换为 WM_LBUTTONDBLCLK；本基准把 DOWN 与 DBLCLK 合计为送达点击次数。" },
            };

            var root = new Dictionary<string, object>
            {
                { "meta", meta },
                { "runOrder", _order.ToArray() },
                { "clickRuns", _clickRuns.ToArray() },
                { "keyRuns", _keyRuns.ToArray() },
                { "summary", BuildSummary() },
            };

            string json = Json.Serialize(root, true);
            File.WriteAllText(OutPath, json, new UTF8Encoding(false));
            Console.WriteLine("[done] raw data -> " + OutPath + " (" + json.Length + " chars)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[fatal] " + ex);
            ExitCode = 1;
            try
            {
                var root = new Dictionary<string, object>
                {
                    { "meta", new Dictionary<string, object> { { "tool", "bench-click.exe" }, { "error", ex.ToString() } } },
                    { "clickRuns", _clickRuns.ToArray() },
                    { "keyRuns", _keyRuns.ToArray() },
                };
                File.WriteAllText(OutPath, Json.Serialize(root, true), new UTF8Encoding(false));
            }
            catch { }
        }
        finally
        {
            SafeStopAll();
            try { NativeWin.SetCursorPos(CenterX, CenterY); } catch { }
            try
            {
                if (Form.IsHandleCreated)
                    Form.BeginInvoke(new Action(() => Form.Close()));
            }
            catch { }
        }
    }

    // ---------------- click ----------------
    Dictionary<string, object> RunClickWithRetries(string kernel, int interval, int rep)
    {
        Dictionary<string, object> last = null;
        var invalidReasons = new List<string>();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            last = RunClickOnce(kernel, interval, rep, attempt);
            if ((bool)last["valid"]) break;
            invalidReasons.Add(Convert.ToString(last["invalidReason"], CultureInfo.InvariantCulture));
            Console.Error.WriteLine("[warn] invalid click run " + kernel + "@" + interval + " rep" + rep +
                                    " attempt" + attempt + ": " + last["invalidReason"]);
            Thread.Sleep(400);
        }
        last["attempts"] = invalidReasons.Count + 1;
        last["invalidReasons"] = invalidReasons.ToArray();
        return last;
    }

    Dictionary<string, object> RunClickOnce(string kernel, int interval, int rep, int attempt)
    {
        bool native = kernel == "native";
        string orderTag = "click#" + (++_sequence) + " " + kernel + "@" + interval + "ms rep" + rep + " attempt" + attempt;
        _order.Add(orderTag);

        var run = new Dictionary<string, object>
        {
            { "orderIndex", _sequence },
            { "kernel", kernel },
            { "targetIntervalMs", interval },
            { "rep", rep },
            { "attempt", attempt },
            { "valid", false },
            { "invalidReason", "" },
        };

        // 窗口/光标就位校验
        string why;
        if (!EnsureWindowAtPoint(out why))
        {
            run["invalidReason"] = "precheck: " + why;
            return run;
        }

        var proc = Process.GetCurrentProcess();
        TimeSpan cpu0 = proc.TotalProcessorTime;
        long wall0 = Stopwatch.GetTimestamp();

        int startRc = 0;
        try
        {
            if (native) startRc = NativeCore.StartMouseClick(interval, 0, 0, CenterX, CenterY, 1);
            else ManagedKernelBridge.StartMouseClick(interval, false, false, CenterX, CenterY, 1);
        }
        catch (Exception ex)
        {
            run["invalidReason"] = "start failed: " + ex.Message;
            SafeStopAll();
            return run;
        }
        if (startRc != 0 && native)
        {
            run["invalidReason"] = "OpCore_StartMouseClick rc=" + startRc;
            return run;
        }
        run["startReturnCode"] = startRc;

        // 安全看门狗：超时强制停止
        var watchdog = new System.Threading.Timer(_ =>
        {
            try { SafeStopAll(); } catch { }
        }, null, PrewarmMs + TestMs + 3000, Timeout.Infinite);

        bool badWindow = false, badCursor = false;
        string badReason = "";
        int cursorMoves = 0;
        try
        {
            Thread.Sleep(PrewarmMs);
            long measureWall0 = Stopwatch.GetTimestamp();
            TimeSpan measureCpu0 = proc.TotalProcessorTime;
            Ui(() => Form.StartRec());
            long deadline = Stopwatch.GetTimestamp() + (long)(TestMs / 1000.0 * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                Thread.Sleep(10);
                POINT p;
                NativeWin.GetCursorPos(out p);
                if (!WindowRect.Contains(p.x, p.y))
                {
                    badCursor = true;
                    cursorMoves++;
                    NativeWin.SetCursorPos(CenterX, CenterY);
                }
                IntPtr h = NativeWin.WindowFromPoint(new POINT { x = CenterX, y = CenterY });
                if (h != Form.Handle)
                {
                    badWindow = true;
                    badReason = "WindowFromPoint(center)=" + h.ToInt64().ToString("X", CultureInfo.InvariantCulture) +
                                " != form " + Form.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
                    break;
                }
            }

            long wallStop = Stopwatch.GetTimestamp();
            SafeStopAll();
            TimeSpan cpu1 = proc.TotalProcessorTime;
            long t1 = Ui(() => { Form.StopRec(); return Form.T1; });

            Thread.Sleep(120); // 让队列中可能残留的输入消息被处理完
            SnapShot snap = Ui(() => Form.TakeSnapshot());

            // 窗口/光标终检
            bool endWindowOk = NativeWin.WindowFromPoint(new POINT { x = CenterX, y = CenterY }) == Form.Handle;
            POINT pe; NativeWin.GetCursorPos(out pe);
            bool cursorOk = WindowRect.Contains(pe.x, pe.y);
            IntPtr fg = NativeWin.GetForegroundWindow();

            long t0 = snap.T0;
            double elapsedMs = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
            double wallMs = (wallStop - wall0) * 1000.0 / Stopwatch.Frequency;
            double measureWallMs = (wallStop - measureWall0) * 1000.0 / Stopwatch.Frequency;
            double cpuMs = (cpu1 - cpu0).TotalMilliseconds;
            double measureCpuMs = (cpu1 - measureCpu0).TotalMilliseconds;

            bool valid = !badWindow && !badCursor && endWindowOk && cursorOk && elapsedMs > TestMs * 0.5;
            if (!valid)
            {
                string r = "";
                if (badWindow) r += "during-run window lost(" + badReason + "); ";
                if (badCursor) r += "cursor left window " + cursorMoves + " times; ";
                if (!endWindowOk) r += "end window mismatch; ";
                if (!cursorOk) r += "end cursor outside; ";
                if (elapsedMs <= TestMs * 0.5) r += "measure window too short (" + elapsedMs.ToString("F1", CultureInfo.InvariantCulture) + "ms); ";
                run["invalidReason"] = r;
            }
            else run["valid"] = true;

            run["measureWindowMs"] = Round(elapsedMs, 3);
            run["wallWindowMs"] = Round(wallMs, 3);
            run["cpuMs"] = Round(cpuMs, 3);
            run["cpuPercentOfOneCore"] = Round(cpuMs / wallMs * 100.0, 3);
            run["cpuPercentOfMachine"] = Round(cpuMs / wallMs * 100.0 / Environment.ProcessorCount, 3);
            run["windowOkStart"] = true;
            run["windowOkEnd"] = endWindowOk;
            run["cursorOkEnd"] = cursorOk;
            run["foregroundWasForm"] = fg == Form.Handle;
            run["cursorForcedBackCount"] = cursorMoves;
            run["endWindowMismatch"] = !endWindowOk;
            run["dllThreadsAfterRun"] = proc.Threads.Count;

            AnalyzeClick(snap, interval, elapsedMs, run);
        }
        finally
        {
            watchdog.Dispose();
        }
        return run;
    }

    static void AnalyzeClick(SnapShot snap, int target, double elapsedMs, Dictionary<string, object> run)
    {
        double freq = Stopwatch.Frequency;
        int downCount = 0, dblCount = 0, upCount = 0, other = 0;
        var downTimes = new List<double>(snap.Count);
        for (int i = 0; i < snap.Count; i++)
        {
            int m = snap.Msgs[i];
            if (m == 0x0201) { downCount++; downTimes.Add((snap.Ts[i] - snap.T0) * 1000.0 / freq); }
            else if (m == 0x0203) { dblCount++; downTimes.Add((snap.Ts[i] - snap.T0) * 1000.0 / freq); }
            else if (m == 0x0202) upCount++;
            else other++;
        }
        int delivered = downCount + dblCount;
        double plannedExact = elapsedMs / target;
        int planned = (int)Math.Round(plannedExact);
        var intervals = new List<double>(Math.Max(0, downTimes.Count - 1));
        for (int i = 1; i < downTimes.Count; i++) intervals.Add(downTimes[i] - downTimes[i - 1]);

        run["deliveredClicks"] = delivered;
        run["downCount"] = downCount;
        run["dblClkCount"] = dblCount;
        run["upCount"] = upCount;
        run["otherMessages"] = other;
        run["downUpMismatch"] = (downCount + dblCount) - upCount;
        run["plannedClicks"] = planned;
        run["plannedClicksExact"] = Round(plannedExact, 2);
        run["lossRate"] = planned > 0 ? Round((planned - delivered) / (double)planned, 5) : 0.0;
        run["effectiveClicksPerSec"] = elapsedMs > 0 ? Round(delivered / (elapsedMs / 1000.0), 3) : 0.0;
        run["intervalsMs"] = intervals.Select(v => Round(v, 4)).ToArray();
        run["stats"] = ComputeStats(intervals, target);
    }

    // ---------------- keyboard ----------------
    static KeyValuePair<string, KeyValuePair<string, ushort[]>> MakeKeyCase(string name, string kernel, ushort[] keys)
    {
        return new KeyValuePair<string, KeyValuePair<string, ushort[]>>(name, new KeyValuePair<string, ushort[]>(kernel, keys));
    }

    static object Get(Dictionary<string, object> dict, string key)
    {
        object value;
        return dict != null && dict.TryGetValue(key, out value) ? value : null;
    }

    Dictionary<string, object> RunKeyWithRetries(string caseName, string kernel, ushort[] keys, int rep)
    {
        Dictionary<string, object> last = null;
        var invalidReasons = new List<string>();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            last = RunKeyOnce(caseName, kernel, keys, rep, attempt);
            if ((bool)last["valid"]) break;
            invalidReasons.Add(Convert.ToString(last["invalidReason"], CultureInfo.InvariantCulture));
            Thread.Sleep(400);
        }
        last["attempts"] = invalidReasons.Count + 1;
        last["invalidReasons"] = invalidReasons.ToArray();
        return last;
    }

    Dictionary<string, object> RunKeyOnce(string caseName, string kernel, ushort[] keys, int rep, int attempt)
    {
        int seq = ++_sequence;
        _order.Add("key#" + seq + " " + caseName + " rep" + rep + " attempt" + attempt);
        var run = new Dictionary<string, object>
        {
            { "orderIndex", seq },
            { "case", caseName },
            { "kernel", kernel },
            { "keysHex", keys.Select(k => "0x" + k.ToString("X2", CultureInfo.InvariantCulture)).ToArray() },
            { "targetIntervalMs", 50 },
            { "rep", rep },
            { "attempt", attempt },
            { "valid", false },
            { "invalidReason", "" },
        };

        string why;
        if (!EnsureWindowAtPoint(out why))
        {
            run["invalidReason"] = "precheck: " + why;
            return run;
        }
        if (NativeWin.GetForegroundWindow() != Form.Handle)
        {
            run["invalidReason"] = "precheck: form not foreground";
            return run;
        }

        var proc = Process.GetCurrentProcess();
        TimeSpan cpu0 = proc.TotalProcessorTime;
        long wall0 = Stopwatch.GetTimestamp();
        int rc = 0;
        try
        {
            if (kernel == "native") rc = NativeCore.StartKeyClick(50, keys, keys.Length);
            else ManagedKernelBridge.StartKeyClick(50, keys);
        }
        catch (Exception ex) { run["invalidReason"] = "start failed: " + ex.Message; return run; }
        run["startReturnCode"] = rc;
        if (kernel == "native" && rc != 0) { run["invalidReason"] = "OpCore_StartKeyClick rc=" + rc; return run; }

        var watchdog = new System.Threading.Timer(_ => { try { SafeStopAll(); } catch { } }, null, PrewarmMs + TestMs + 3000, Timeout.Infinite);
        bool foregroundLost = false;
        try
        {
            Thread.Sleep(PrewarmMs);
            TimeSpan measureCpu0 = proc.TotalProcessorTime;
            Ui(() => Form.StartRec());
            long deadline = Stopwatch.GetTimestamp() + (long)(TestMs / 1000.0 * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                Thread.Sleep(10);
                if (NativeWin.GetForegroundWindow() != Form.Handle) { foregroundLost = true; break; }
            }
            long wallStop = Stopwatch.GetTimestamp();
            SafeStopAll();
            TimeSpan cpu1 = proc.TotalProcessorTime;
            long t1 = Ui(() => { Form.StopRec(); return Form.T1; });
            Thread.Sleep(120);
            SnapShot snap = Ui(() => Form.TakeSnapshot());

            double elapsedMs = (t1 - snap.T0) * 1000.0 / Stopwatch.Frequency;
            double wallMs = (wallStop - wall0) * 1000.0 / Stopwatch.Frequency;
            double cpuMs = (cpu1 - cpu0).TotalMilliseconds;
            double measureCpuMs = (cpu1 - measureCpu0).TotalMilliseconds;

            bool valid = !foregroundLost && elapsedMs > TestMs * 0.5;
            if (!valid) run["invalidReason"] = foregroundLost ? "foreground lost during run" : "measure window too short";
            else run["valid"] = true;

            run["measureWindowMs"] = Round(elapsedMs, 3);
            run["wallWindowMs"] = Round(wallMs, 3);
            run["cpuMs"] = Round(cpuMs, 3);
            run["cpuPercentOfOneCore"] = Round(cpuMs / wallMs * 100.0, 3);

            AnalyzeKeys(snap, keys, run);
        }
        finally { watchdog.Dispose(); }
        return run;
    }

    void AnalyzeKeys(SnapShot snap, ushort[] keys, Dictionary<string, object> run)
    {
        double freq = Stopwatch.Frequency;
        var events = new List<KeyEvent>();
        for (int i = 0; i < snap.Count; i++)
        {
            int m = snap.Msgs[i];
            if (m == 0x0100 || m == 0x0104 || m == 0x0101 || m == 0x0105)
            {
                events.Add(new KeyEvent
                {
                    T = (snap.Ts[i] - snap.T0) * 1000.0 / freq,
                    Msg = m,
                    Vk = snap.Vks[i],
                    Down = (m == 0x0100 || m == 0x0104),
                });
            }
        }
        int primaryVk = keys[keys.Length - 1];
        int ctrlVk = 0x11;
        var primaryDowns = events.Where(e => e.Down && e.Vk == primaryVk).Select(e => e.T).ToList();
        var periods = new List<double>();
        for (int i = 1; i < primaryDowns.Count; i++) periods.Add(primaryDowns[i] - primaryDowns[i - 1]);

        // 每个周期内部：主键 press->release 时长、release->下一次 press 间隔
        var holdMs = new List<double>();
        var gapMs = new List<double>();
        var cycles = new List<object>();
        for (int i = 0; i < primaryDowns.Count; i++)
        {
            double d = primaryDowns[i];
            int idx = events.FindIndex(e => e.Down && e.Vk == primaryVk && Math.Abs(e.T - d) < 1e-9);
            if (idx < 0) continue;
            int upIdx = -1;
            for (int j = idx + 1; j < events.Count; j++)
                if (!events[j].Down && events[j].Vk == primaryVk) { upIdx = j; break; }
            double? hold = upIdx >= 0 ? (double?)(events[upIdx].T - d) : null;
            double? gap = (upIdx >= 0 && i + 1 < primaryDowns.Count) ? (double?)(primaryDowns[i + 1] - events[upIdx].T) : null;
            if (hold.HasValue) holdMs.Add(hold.Value);
            if (gap.HasValue) gapMs.Add(gap.Value);
            var cyc = new Dictionary<string, object>
            {
                { "cycleStartMs", Round(d, 4) },
                { "hangMs", hold.HasValue ? (object)Round(hold.Value, 4) : null },
                { "gapToNextMs", gap.HasValue ? (object)Round(gap.Value, 4) : null },
            };
            cycles.Add(cyc);
        }

        // 修饰键：Ctrl down -> 主键 down 的间隔（组合键顺序验证）
        var ctrlToPrimary = new List<double>();
        var ctrlDowns = events.Where(e => e.Down && e.Vk == ctrlVk).Select(e => e.T).ToList();
        foreach (double ct in ctrlDowns)
        {
            double? firstPrimary = primaryDowns.Where(t => t >= ct).Cast<double?>().FirstOrDefault();
            if (firstPrimary.HasValue) ctrlToPrimary.Add(firstPrimary.Value - ct);
        }

        run["aDownCount"] = primaryDowns.Count;
        run["periodsMs"] = periods.Select(v => Round(v, 4)).ToArray();
        run["periodMedianMs"] = Round(Median(periods), 3);
        run["periodMeanMs"] = Round(Mean(periods), 3);
        run["holdMedianMs"] = Round(Median(holdMs), 3);
        run["gapMedianMs"] = Round(Median(gapMs), 3);
        run["ctrlToPrimaryMedianMs"] = Round(Median(ctrlToPrimary), 3);
        run["ctrlDownCount"] = ctrlDowns.Count;
        run["eventCount"] = events.Count;
        run["cycles"] = cycles.ToArray();
        run["periodStats"] = ComputeStats(periods, keys.Length > 1 ? 100.0 : 50.0);
        run["events"] = events.Take(4000).Select(e => new Dictionary<string, object>
        {
            { "tMs", Round(e.T, 4) },
            { "msg", "0x" + e.Msg.ToString("X4", CultureInfo.InvariantCulture) },
            { "vk", "0x" + e.Vk.ToString("X2", CultureInfo.InvariantCulture) },
            { "down", e.Down },
        }).ToArray();
    }

    // ---------------- shared ----------------
    bool EnsureWindowAtPoint(out string why)
    {
        why = "";
        int w = WindowRect.Width, h = WindowRect.Height;
        Rectangle scr = Screen.PrimaryScreen.Bounds;
        var candidates = new List<Rectangle>();
        AddCand(candidates, scr, new Rectangle(scr.Left + (scr.Width - w) / 2, scr.Top + (scr.Height - h) / 2, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Left + 40, scr.Top + 40, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Right - w - 40, scr.Top + 40, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Left + 40, scr.Bottom - h - 40, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Right - w - 40, scr.Bottom - h - 40, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Left + (scr.Width - w) / 2, scr.Top + 40, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Left + 40, scr.Top + (scr.Height - h) / 2, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Right - w - 40, scr.Top + (scr.Height - h) / 2, w, h));
        AddCand(candidates, scr, new Rectangle(scr.Left + (scr.Width - w) / 2, scr.Bottom - h - 40, w, h));

        foreach (Rectangle rect in candidates)
        {
            int ccx = rect.X + rect.Width / 2, ccy = rect.Y + rect.Height / 2;
            try
            {
                Form.Invoke(new Action(() =>
                {
                    if (Form.Bounds != rect) Form.Bounds = rect;
                    NativeWin.SetForegroundWindow(Form.Handle);
                    NativeWin.SetWindowPos(Form.Handle, NativeWin.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeWin.SWP_NOMOVE | NativeWin.SWP_NOSIZE | NativeWin.SWP_SHOWWINDOW);
                    Form.Activate();
                }));
            }
            catch (Exception ex) { why = "invoke: " + ex.Message; return false; }
            NativeWin.SetCursorPos(ccx, ccy);
            Thread.Sleep(70);
            IntPtr hit = NativeWin.WindowFromPoint(new POINT { x = ccx, y = ccy });
            POINT p;
            NativeWin.GetCursorPos(out p);
            if (hit == Form.Handle && p.x == ccx && p.y == ccy)
            {
                CenterX = ccx;
                CenterY = ccy;
                WindowRect = rect;
                return true;
            }
            why = "candidate(" + rect.X + "," + rect.Y + ") blocked: hit=0x" +
                  hit.ToInt64().ToString("X", CultureInfo.InvariantCulture) +
                  " cursor=" + p.x + "," + p.y +
                  " form=0x" + Form.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
            Thread.Sleep(40);
        }
        return false;
    }

    static void AddCand(List<Rectangle> list, Rectangle scr, Rectangle r)
    {
        if (r.Left < scr.Left || r.Top < scr.Top || r.Right > scr.Right || r.Bottom > scr.Bottom) return;
        if (!list.Contains(r)) list.Add(r);
    }

    void SafeStopAll()
    {
        try { NativeCore.Stop(); } catch { }
        try { ManagedKernelBridge.StopAll(); } catch { }
    }

    T Ui<T>(Func<T> fn) { return (T)Form.Invoke(fn); }
    void Ui(Action fn) { Form.Invoke(fn); }

    static Dictionary<string, object> ComputeStats(List<double> values, double target)
    {
        var stats = new Dictionary<string, object>();
        if (values.Count == 0)
        {
            stats["n"] = 0;
            return stats;
        }
        var sorted = values.OrderBy(v => v).ToList();
        double mean = Mean(values);
        double std = values.Count > 1 ? Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1)) : 0;
        double cumulativeDrift = values.Count > 1 ? (values[values.Count - 1] - values[0]) - (values.Count - 1) * target : 0;
        // 用首末时间算总漂移更准确：sum(intervals) == last-first
        cumulativeDrift = values.Sum() - values.Count * target;
        int late = values.Count(v => v > target * 1.5);
        int veryLate = values.Count(v => v > target * 2.0);
        int early = values.Count(v => v < target * 0.5);
        stats["n"] = values.Count;
        stats["meanMs"] = Round(mean, 4);
        stats["stdMs"] = Round(std, 4);
        stats["minMs"] = Round(sorted[0], 4);
        stats["p50Ms"] = Round(Percentile(sorted, 50), 4);
        stats["p95Ms"] = Round(Percentile(sorted, 95), 4);
        stats["p99Ms"] = Round(Percentile(sorted, 99), 4);
        stats["maxMs"] = Round(sorted[sorted.Count - 1], 4);
        stats["meanDeviationMs"] = Round(mean - target, 4);
        stats["cumulativeDriftMs"] = Round(cumulativeDrift, 4);
        stats["driftPerIntervalMs"] = values.Count > 0 ? Round((mean - target), 4) : 0;
        stats["lateCount"] = late;
        stats["lateRate"] = Round(late / (double)values.Count, 4);
        stats["veryLateCount"] = veryLate;
        stats["earlyCount"] = early;
        stats["earlyRate"] = Round(early / (double)values.Count, 4);
        return stats;
    }

    static double Mean(List<double> values) { return values.Count == 0 ? 0 : values.Sum() / values.Count; }
    static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var s = values.OrderBy(v => v).ToList();
        int mid = s.Count / 2;
        return s.Count % 2 == 1 ? s[mid] : (s[mid - 1] + s[mid]) / 2.0;
    }
    static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(sorted.Count - 1, idx))];
    }
    static double Round(double v, int digits) { return Math.Round(v, digits, MidpointRounding.AwayFromZero); }

    static string Fmt(object statsObj)
    {
        var st = statsObj as Dictionary<string, object>;
        if (st == null || !st.ContainsKey("meanMs")) return "-";
        return Convert.ToString(st["meanMs"], CultureInfo.InvariantCulture) + "±" +
               Convert.ToString(st["stdMs"], CultureInfo.InvariantCulture);
    }

    Dictionary<string, object> BuildSummary()
    {
        var root = new Dictionary<string, object>();
        foreach (string kernel in Kernels)
        {
            var rows = new List<object>();
            foreach (int interval in Intervals)
            {
                var valid = _clickRuns.Where(r => (string)r["kernel"] == kernel && (int)r["targetIntervalMs"] == interval && (bool)r["valid"]).ToList();
                if (valid.Count == 0) continue;
                rows.Add(new Dictionary<string, object>
                {
                    { "intervalMs", interval },
                    { "validReps", valid.Count },
                    { "deliveredMedian", Median(valid.Select(r => (double)(int)r["deliveredClicks"]).ToList()) },
                    { "rateMedian", Median(valid.Select(r => Convert.ToDouble(r["effectiveClicksPerSec"], CultureInfo.InvariantCulture)).ToList()) },
                    { "meanMedian", Median(valid.Select(r => Convert.ToDouble(((Dictionary<string, object>)r["stats"])["meanMs"], CultureInfo.InvariantCulture)).ToList()) },
                    { "stdMedian", Median(valid.Select(r => Convert.ToDouble(((Dictionary<string, object>)r["stats"])["stdMs"], CultureInfo.InvariantCulture)).ToList()) },
                    { "p95Median", Median(valid.Select(r => Convert.ToDouble(((Dictionary<string, object>)r["stats"])["p95Ms"], CultureInfo.InvariantCulture)).ToList()) },
                    { "maxMedian", Median(valid.Select(r => Convert.ToDouble(((Dictionary<string, object>)r["stats"])["maxMs"], CultureInfo.InvariantCulture)).ToList()) },
                    { "lateRateMedian", Median(valid.Select(r => Convert.ToDouble(((Dictionary<string, object>)r["stats"])["lateRate"], CultureInfo.InvariantCulture)).ToList()) },
                    { "lossRateMedian", Median(valid.Select(r => Convert.ToDouble(r["lossRate"], CultureInfo.InvariantCulture)).ToList()) },
                    { "cpuPercentMedian", Median(valid.Select(r => Convert.ToDouble(r["cpuPercentOfOneCore"], CultureInfo.InvariantCulture)).ToList()) },
                });
            }
            root[kernel] = rows.ToArray();
        }
        var keyRows = new List<object>();
        foreach (string c in _keyRuns.Select(r => (string)r["case"]).Distinct())
        {
            var valid = _keyRuns.Where(r => (string)r["case"] == c && (bool)r["valid"]).ToList();
            if (valid.Count == 0) continue;
            keyRows.Add(new Dictionary<string, object>
            {
                { "case", c },
                { "validReps", valid.Count },
                { "periodMedianMs", Median(valid.Select(r => Convert.ToDouble(r["periodMedianMs"], CultureInfo.InvariantCulture)).ToList()) },
                { "holdMedianMs", Median(valid.Select(r => Convert.ToDouble(r["holdMedianMs"], CultureInfo.InvariantCulture)).ToList()) },
                { "gapMedianMs", Median(valid.Select(r => Convert.ToDouble(r["gapMedianMs"], CultureInfo.InvariantCulture)).ToList()) },
                { "ctrlToPrimaryMedianMs", Median(valid.Select(r => Convert.ToDouble(r["ctrlToPrimaryMedianMs"], CultureInfo.InvariantCulture)).ToList()) },
            });
        }
        root["key"] = keyRows.ToArray();
        return root;
    }
}

class KeyEvent
{
    public double T;
    public int Msg;
    public int Vk;
    public bool Down;
}

// ============================================================================
// 原生内核 P/Invoke（LoadLibrary + GetProcAddress，x64 stdcall）
// ============================================================================
static class NativeCore
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int StartMouseClickFn(int intervalMs, int randomSpeedOffset, int randomPosOffset, int baseX, int baseY, int mouseButton);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int StartKeyClickFn(int intervalMs, [MarshalAs(UnmanagedType.LPArray)] ushort[] keys, int keyCount);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int StopFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int IsRunningFn();

    static IntPtr _lib;
    static StartMouseClickFn _startMouseClick;
    static StartKeyClickFn _startKeyClick;
    static StopFn _stop;
    static IsRunningFn _isRunning;

    public static void Load(string path)
    {
        _lib = LoadLibraryW(path);
        if (_lib == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary failed: " + path);
        _startMouseClick = Get<StartMouseClickFn>("OpCore_StartMouseClick");
        _startKeyClick = Get<StartKeyClickFn>("OpCore_StartKeyClick");
        _stop = Get<StopFn>("OpCore_Stop");
        _isRunning = Get<IsRunningFn>("OpCore_IsRunning");
    }

    static T Get<T>(string name) where T : class
    {
        IntPtr p = GetProcAddress(_lib, name);
        if (p == IntPtr.Zero) throw new EntryPointNotFoundException("GetProcAddress failed: " + name);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
    }

    public static int StartMouseClick(int intervalMs, int rs, int rp, int x, int y, int button)
    { return _startMouseClick(intervalMs, rs, rp, x, y, button); }

    public static int StartKeyClick(int intervalMs, ushort[] keys, int keyCount)
    { return _startKeyClick(intervalMs, keys, keyCount); }

    public static int Stop() { return _stop(); }
    public static int IsRunning() { return _isRunning(); }
}

// ============================================================================
// user32 / ntdll
// ============================================================================
struct POINT { public int x; public int y; }

static class NativeWin
{
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("ntdll.dll")]
    public static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
}

// ============================================================================
// 极简 JSON 序列化
// ============================================================================
static class Json
{
    public static string Serialize(object value, bool pretty)
    {
        var sb = new StringBuilder();
        Write(sb, value, pretty, 0);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, object value, bool pretty, int depth)
    {
        if (value == null) { sb.Append("null"); return; }
        if (value is string) { WriteString(sb, (string)value); return; }
        if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
        if (value is int || value is long || value is short || value is byte || value is ushort || value is uint || value is ulong)
        { sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
        if (value is float || value is double || value is decimal)
        {
            double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }
        var dict = value as IDictionary<string, object>;
        if (dict != null)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, pretty, depth + 1);
                WriteString(sb, kv.Key);
                sb.Append(':');
                if (pretty) sb.Append(' ');
                Write(sb, kv.Value, pretty, depth + 1);
            }
            if (dict.Count > 0) NewLine(sb, pretty, depth);
            sb.Append('}');
            return;
        }
        var enumerable = value as System.Collections.IEnumerable;
        if (enumerable != null)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, pretty, depth + 1);
                Write(sb, item, pretty, depth + 1);
            }
            if (!first) NewLine(sb, pretty, depth);
            sb.Append(']');
            return;
        }
        WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    static void NewLine(StringBuilder sb, bool pretty, int depth)
    {
        if (!pretty) return;
        sb.Append('\n');
        sb.Append(' ', depth * 2);
    }

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
