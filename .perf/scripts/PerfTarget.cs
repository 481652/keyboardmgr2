// 性能测试用目标窗口：接收 UIA/WM_SETTEXT 写入的文本，并记录每次 Enter 按键的 QPC 时间戳
// 用法: target-form.exe <logPath>
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

static class PerfTarget
{
    static StreamWriter log;
    static readonly Stopwatch sw = Stopwatch.StartNew();

    static void Log(string line)
    {
        try { log.WriteLine(line); } catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        string logPath = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "kbmperf", "target.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)));
            log = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false));
        }
        catch
        {
            logPath = Path.Combine(Path.GetTempPath(), "kbmperf", "target.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            log = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false));
        }
        log.AutoFlush = true;

        var form = new Form
        {
            Text = "KBM Perf Target",
            Width = 760,
            Height = 560,
            StartPosition = FormStartPosition.Manual,
            Left = 120,
            Top = 80
        };
        var box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new System.Drawing.Font("Consolas", 11f)
        };
        form.Controls.Add(box);

        box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                double ms = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency * 1000.0;
                Log("ENTER\t" + ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
        };

        form.Shown += (s, e) =>
        {
            form.Activate();
            box.Focus();
            Log("READY\t" + form.Handle.ToInt64() + "\t" + Process.GetCurrentProcess().Id +
                "\t" + ((double)sw.ElapsedTicks / Stopwatch.Frequency * 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        };

        Application.Run(form);
    }
}
