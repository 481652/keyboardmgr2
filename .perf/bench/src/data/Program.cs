// ============================================================================
// 键鼠管家性能基准 —— 任务2：数据通路微基准（不注入任何键鼠输入）
//
// 被测代码：.perf\bench\bin\KbmrBench.Data.dll
//   其中 KbmrCodec.vb / MacroModel.vb / RecordingStore.vb / KeyNameMapper.vb /
//   SettingsModule.vb 均为 keyboardmgr2 项目原始源码直接编译（vbc，Release，
//   optimize+，debug-），仅 DlgModule.ShowExpdlg 为垫片。
//
// 输出：.perf\data\bench-data.json（完整原始数据）
// ============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

static class Program
{
    const string BenchRoot = @"D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\bench";
    const string DataRoot = @"D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\data";
    const string TmpDir = BenchRoot + @"\tmp";

    [STAThread]
    static int Main(string[] args)
    {
        string outPath = Path.Combine(DataRoot, "bench-data.json");
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--out") outPath = args[i + 1];
        }

        Directory.CreateDirectory(TmpDir);
        Directory.CreateDirectory(DataRoot);

        var meta = new Dictionary<string, object>();
        meta["tool"] = "bench-data.exe";
        meta["generatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        meta["os"] = Environment.OSVersion.VersionString;
        meta["processorCount"] = Environment.ProcessorCount;
        meta["is64BitProcess"] = Environment.Is64BitProcess;
        meta["clrVersion"] = Environment.Version.ToString();
        meta["stopwatchFrequency"] = Stopwatch.Frequency;
        meta["note"] = "任务2为纯数据通路微基准，不注入键鼠输入，可在 GO 前执行。";
        uint tMin, tMax, tCur;
        try { NtQueryTimerResolution(out tMin, out tMax, out tCur); meta["timerResolution100ns"] = tCur; }
        catch { meta["timerResolution100ns"] = -1; }

        var root = new Dictionary<string, object>();
        root["meta"] = meta;

        // ------------------------------------------------------------------
        // 1) KbmrCodec 序列化/反序列化
        // ------------------------------------------------------------------
        var codecRows = new List<object>();
        foreach (int eventCount in new[] { 10000, 100000 })
        {
            Console.WriteLine($"[codec] n={eventCount} 构造录制...");
            var rec = DataBridge.MakeRecording(eventCount, 12345 + eventCount);
            long payloadBytes = ComputePayloadSize(rec);
            var ser = new List<double>();
            var deser = new List<double>();
            byte[] lastBytes = null;
            bool roundtripOk = true;
            bool deterministic = true;
            long fileBytes = 0, compressedBytes = 0;
            int actualEvents = 0;
            bool metadataOk = true;
            double metadataMs = 0;

            // 预热 1 次（JIT + Deflate 初始化），不计入
            byte[] warm = DataBridge.Serialize(rec);
            var warmBack = DataBridge.Deserialize(warm);
            if (warmBack.Events.Count != eventCount) roundtripOk = false;

            for (int rep = 0; rep < 3; rep++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                byte[] bytes = null;
                double tSer = TimeMs(() => { bytes = DataBridge.Serialize(rec); });
                ser.Add(tSer);
                if (bytes == null) throw new Exception("Serialize 返回 null");
                if (lastBytes == null) { lastBytes = bytes; fileBytes = bytes.Length; compressedBytes = ReadCompressedLength(bytes); }
                else if (!lastBytes.SequenceEqual(bytes)) deterministic = false;

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                KbmrRecording back = null;
                double tDeser = TimeMs(() => { back = DataBridge.Deserialize(bytes); });
                deser.Add(tDeser);
                if (back == null || back.Events.Count != eventCount) roundtripOk = false;
                else if (!back.Events[0].OffsetMs.Equals(rec.Events[0].OffsetMs) || back.Events[eventCount - 1].Kind != rec.Events[eventCount - 1].Kind) roundtripOk = false;
                actualEvents = back == null ? -1 : back.Events.Count;
            }

            string metadataPath = Path.Combine(TmpDir, "bench-metadata-" + eventCount + ".kbmr");
            File.WriteAllBytes(metadataPath, lastBytes);
            KbmrRecording metadata = null;
            metadataMs = TimeMs(() => { metadata = DataBridge.LoadMetadata(metadataPath); });
            if (metadata == null || metadata.EventsLoaded || metadata.Events.Count != 0 || metadata.EventCount != eventCount) metadataOk = false;
            bool metadataDecodeOk = false;
            if (metadata != null)
                metadataDecodeOk = DataBridge.LoadEvents(metadata) && metadata.EventsLoaded && metadata.Events.Count == eventCount;
            File.Delete(metadataPath);

            // 校验：离线推算的 payload 字节数 == 真实文件头中的压缩块解压后大小
            long payloadCheck = -1;
            try
            {
                byte[] payload = BuildPayloadMirror(rec);
                payloadCheck = payload.Length;
                using (var ms = new MemoryStream())
                {
                    // 与 KbmrCodec.Serialize 保持一致：Fastest + 单流
                    using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true)) { ds.Write(payload, 0, payload.Length); }
                    if (ms.Length != compressedBytes) payloadCheck = -2; // 镜像不一致
                }
            }
            catch (Exception ex) { payloadCheck = -3; Console.WriteLine("  payload mirror failed: " + ex.Message); }

            codecRows.Add(new Dictionary<string, object>
            {
                { "events", eventCount },
                { "payloadBytes", payloadBytes },
                { "payloadBytesVerified", payloadCheck },
                { "fileBytes", fileBytes },
                { "compressedBytes", compressedBytes },
                { "bytesPerEvent", fileBytes / (double)eventCount },
                { "compressionRatio", fileBytes > 0 ? payloadBytes / (double)fileBytes : 0.0 },
                { "serializeMs", ser.ToArray() },
                { "serializeMedianMs", Median(ser) },
                { "deserializeMs", deser.ToArray() },
                { "deserializeMedianMs", Median(deser) },
                { "serializeMBps", eventCount / (Median(ser) / 1000.0) / 1e6 },
                { "deserializeMBps", eventCount / (Median(deser) / 1000.0) / 1e6 },
                { "roundtripOk", roundtripOk },
                { "deterministic", deterministic },
                { "deserializedEvents", actualEvents },
                { "metadataOnlyOk", metadataOk },
                { "metadataDecodeOk", metadataDecodeOk },
                { "metadataOnlyMs", metadataMs },
                { "durationMs", rec.DurationMs },
            });
            Console.WriteLine($"   file={fileBytes} B payload={payloadBytes} B compressed={compressedBytes} B " +
                               $"ser={Median(ser):F2} ms deser={Median(deser):F2} ms metadata={metadataMs:F3} ms ok={metadataOk}/{metadataDecodeOk}");
        }
        root["codec"] = codecRows;

        // ------------------------------------------------------------------
        // 2) RecordingStore 保存/加载 10 万条
        // ------------------------------------------------------------------
        var store = new Dictionary<string, object>();
        {
            var rec = DataBridge.MakeRecording(100000, 777);
            string path = Path.Combine(TmpDir, "bench-store-100k.kbmr");
            if (File.Exists(path)) File.Delete(path);
            var saveMs = new List<double>();
            var loadMs = new List<double>();
            bool saveOk = true;
            bool loadOk = true;
            long fileBytes = 0;
            // 预热
            DataBridge.SaveRecording(rec, path + ".warm");
            DataBridge.LoadFile(path + ".warm");
            File.Delete(path + ".warm");
            for (int rep = 0; rep < 3; rep++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                bool ok = true;
                double tSave = TimeMs(() => { ok = DataBridge.SaveRecording(rec, path); });
                saveMs.Add(tSave); if (!ok) saveOk = false;
                fileBytes = new FileInfo(path).Length;

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                KbmrRecording back = null;
                double tLoad = TimeMs(() => { back = DataBridge.LoadFile(path); });
                loadMs.Add(tLoad); if (back == null || back.Events.Count != 100000) loadOk = false;
            }
            store["path"] = path;
            store["saveMs"] = saveMs.ToArray();
            store["saveMedianMs"] = Median(saveMs);
            store["loadMs"] = loadMs.ToArray();
            store["loadMedianMs"] = Median(loadMs);
            store["fileBytes"] = fileBytes;
            store["saveOk"] = saveOk;
            store["loadOk"] = loadOk;
            store["note"] = "Save 调用真实 RecordingStore.Save（预置 FilePath 指向 .perf\\bench\\tmp）；Load 测的是 LoadAll 内部单文件的 File.ReadAllBytes+KbmrCodec.Deserialize 组合（LoadAll 目录固定为 %LOCALAPPDATA%，基准不写用户录制目录）。";
            Console.WriteLine($"   save={Median(saveMs):F2} ms load={Median(loadMs):F2} ms file={fileBytes} B");
        }
        root["recordingStore"] = store;

        // ------------------------------------------------------------------
        // 3) KeyNameMapper 1000 个键名批量转换
        // ------------------------------------------------------------------
        var knm = new Dictionary<string, object>();
        {
            string[] pool = { "A", "b", "CTRL", "control", "SHIFT", "ALT", "WIN", "ENTER", "ESC", "SPACE",
                              "F1", "F12", "NUMPAD5", "PAGEUP", "VK_0x41", "VK_0x7B", ";", "=", "[", "\\",
                              "'", "LEFT", "RALT", "PRTSC", "unknown_key", "", "  Tab  ", "VK_0xGG", "return", "Delete" };
            var names = new string[1000];
            var rng = new Random(4242);
            for (int i = 0; i < 1000; i++) names[i] = pool[rng.Next(pool.Length)];

            ushort[] vks = null;
            var parseMs = new List<double>();
            var formatMs = new List<double>();
            var emptyMs = new List<double>();
            string[] formatted = null;
            // 预热
            for (int i = 0; i < 1000; i++) { DataBridge.ParseKeyName(names[i]); DataBridge.FormatKeyName(65); }
            for (int rep = 0; rep < 3; rep++)
            {
                emptyMs.Add(TimeMs(() => { int x = 0; for (int i = 0; i < 1000; i++) x += i; if (x < 0) Console.Write(""); }));
                parseMs.Add(TimeMs(() => { vks = new ushort[1000]; for (int i = 0; i < 1000; i++) vks[i] = DataBridge.ParseKeyName(names[i]); }));
                formatMs.Add(TimeMs(() => { formatted = new string[1000]; for (int i = 0; i < 1000; i++) formatted[i] = DataBridge.FormatKeyName(vks[i]); }));
            }
            int resolved = vks.Count(v => v != 0);
            long sumVk = vks.Sum(v => (long)v);
            knm["count"] = 1000;
            knm["emptyLoopMs"] = emptyMs.ToArray();
            knm["emptyLoopMedianMs"] = Median(emptyMs);
            knm["parseMs"] = parseMs.ToArray();
            knm["parseMedianMs"] = Median(parseMs);
            knm["formatMs"] = formatMs.ToArray();
            knm["formatMedianMs"] = Median(formatMs);
            knm["resolvedNonZero"] = resolved;
            knm["sumVk"] = sumVk;
            knm["sampleParse"] = names.Zip(vks, (n, v) => new Dictionary<string, object> { { "name", n }, { "vk", v } }).Take(12).ToArray();
            knm["sampleFormat"] = formatted.Take(12).ToArray();
            Console.WriteLine($"   parse={Median(parseMs):F3} ms format={Median(formatMs):F3} ms empty={Median(emptyMs):F3} ms resolved={resolved}/1000");
        }
        root["keyNameMapper"] = knm;

        // ------------------------------------------------------------------
        // 4) SettingsModule 注册表读写 1000 次
        // ------------------------------------------------------------------
        var settings = new Dictionary<string, object>();
        {
            const int N = 1000;
            string prefix = "PerfBench_" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture) + "_";
            string[] keys = new string[N];
            for (int i = 0; i < N; i++) keys[i] = prefix + i.ToString(CultureInfo.InvariantCulture);

            var writeMs = new List<double>();
            var readMs = new List<double>();
            var missMs = new List<double>();
            bool cleanupOk = false;
            int shimErrBefore = DataBridge.ShimErrorCount();
            var readValues = new object[N];
            try
            {
                // 预热（单个键）
                DataBridge.WriteSetting(prefix + "warm", 1);
                DataBridge.ReadSetting(prefix + "warm", null);
                DataBridge.DeleteSetting(prefix + "warm");

                for (int rep = 0; rep < 3; rep++)
                {
                    writeMs.Add(TimeMs(() => { for (int i = 0; i < N; i++) DataBridge.WriteSetting(keys[i], 1000 + i); }));
                    readMs.Add(TimeMs(() => { for (int i = 0; i < N; i++) readValues[i] = DataBridge.ReadSetting(keys[i], null); }));
                    missMs.Add(TimeMs(() => { for (int i = 0; i < N; i++) DataBridge.ReadSetting(prefix + "missing_" + i, null); }));
                }
            }
            finally
            {
                for (int i = 0; i < N; i++) DataBridge.DeleteSetting(keys[i]);
                // 兜底：直接查注册表确认无遗留
                try
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DataBridge.RegistryPath))
                    {
                        cleanupOk = k != null && k.GetValueNames().Count(n => n.StartsWith("PerfBench_", StringComparison.OrdinalIgnoreCase)) == 0;
                        if (!cleanupOk)
                        {
                            foreach (string n in k.GetValueNames().Where(n => n.StartsWith("PerfBench_", StringComparison.OrdinalIgnoreCase)))
                            {
                                k.DeleteValue(n, false);
                            }
                            cleanupOk = k.GetValueNames().Count(n => n.StartsWith("PerfBench_", StringComparison.OrdinalIgnoreCase)) == 0;
                        }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("cleanup check failed: " + ex.Message); }
            }
            int shimErrAfter = DataBridge.ShimErrorCount();
            int readMatched = 0;
            for (int i = 0; i < N; i++)
            {
                if (readValues[i] != null && Convert.ToInt32(readValues[i], CultureInfo.InvariantCulture) == 1000 + i) readMatched++;
            }
            settings["registryPath"] = "HKCU\\" + DataBridge.RegistryPath;
            settings["count"] = N;
            settings["writeMs"] = writeMs.ToArray();
            settings["writeMedianMs"] = Median(writeMs);
            settings["readMs"] = readMs.ToArray();
            settings["readMedianMs"] = Median(readMs);
            settings["readMissingMs"] = missMs.ToArray();
            settings["readMissingMedianMs"] = Median(missMs);
            settings["writePerOpUs"] = Median(writeMs) * 1000.0 / N;
            settings["readPerOpUs"] = Median(readMs) * 1000.0 / N;
            settings["readMatched"] = readMatched;
            settings["shimErrors"] = shimErrAfter - shimErrBefore;
            settings["cleanupOk"] = cleanupOk;
            Console.WriteLine($"   write={Median(writeMs):F2} ms read={Median(readMs):F2} ms miss={Median(missMs):F2} ms " +
                              $"matched={readMatched}/1000 errors={shimErrAfter - shimErrBefore} cleanupOk={cleanupOk}");
        }
        root["settings"] = settings;

        string json = Json.Serialize(root, true);
        File.WriteAllText(outPath, json, new UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine("raw data -> " + outPath + " (" + json.Length + " chars)");
        return 0;
    }

    // ---- helpers ----

    static double TimeMs(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    static int ReadCompressedLength(byte[] file)
    {
        // 文件头：Magic(4) + Version(2) + SampleInterval(4) + W(4) + H(4) + [Dpi(4)，版本 2 起] + Duration(8) + Count(4) + Ticks(8) + CompressedLen(4) + SHA256(32)
        ushort version = BitConverter.ToUInt16(file, 4);
        int offset = 4 + 2 + 4 + 4 + 4 + (version >= 2 ? 4 : 0) + 8 + 4 + 8;
        return BitConverter.ToInt32(file, offset);
    }

    static long ComputePayloadSize(KbmrRecording rec)
    {
        long lastOffset = 0, size = 0;
        int lastX = 0, lastY = 0;
        foreach (var e in rec.Events)
        {
            long offset = Math.Max(lastOffset, Math.Max(0, e.OffsetMs));
            size += VarIntSize((ulong)(offset - lastOffset));
            lastOffset = offset;
            size += 1;
            switch (e.Kind)
            {
                case KbmrEventKind.KeyDown:
                case KbmrEventKind.KeyUp:
                    size += VarIntSize(e.Vk);
                    break;
                case KbmrEventKind.MouseDown:
                case KbmrEventKind.MouseUp:
                    size += 1;                 // 按钮
                    size += 1;                 // HasPosition 标记
                    if (e.HasPosition)
                    {
                        size += VarIntSize(ZigZag(e.X - lastX));
                        size += VarIntSize(ZigZag(e.Y - lastY));
                        lastX = e.X; lastY = e.Y;
                    }
                    break;
                case KbmrEventKind.MouseMove:
                    size += VarIntSize(ZigZag(e.X - lastX));
                    size += VarIntSize(ZigZag(e.Y - lastY));
                    lastX = e.X; lastY = e.Y;
                    break;
                case KbmrEventKind.Wheel:
                    size += VarIntSize(ZigZag(e.Delta));
                    break;
            }
        }
        return size;
    }

    static byte[] BuildPayloadMirror(KbmrRecording rec)
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            long lastOffset = 0;
            int lastX = 0, lastY = 0;
            foreach (var e in rec.Events)
            {
                long offset = Math.Max(lastOffset, Math.Max(0, e.OffsetMs));
                WriteVarInt(bw, (ulong)(offset - lastOffset));
                lastOffset = offset;
                bw.Write((byte)e.Kind);
                switch (e.Kind)
                {
                    case KbmrEventKind.KeyDown:
                    case KbmrEventKind.KeyUp:
                        WriteVarInt(bw, e.Vk);
                        break;
                    case KbmrEventKind.MouseDown:
                    case KbmrEventKind.MouseUp:
                        bw.Write(e.Button);
                        bw.Write((byte)(e.HasPosition ? 1 : 0));
                        if (e.HasPosition)
                        {
                            WriteVarInt(bw, ZigZag(e.X - lastX));
                            WriteVarInt(bw, ZigZag(e.Y - lastY));
                            lastX = e.X; lastY = e.Y;
                        }
                        break;
                    case KbmrEventKind.MouseMove:
                        WriteVarInt(bw, ZigZag(e.X - lastX));
                        WriteVarInt(bw, ZigZag(e.Y - lastY));
                        lastX = e.X; lastY = e.Y;
                        break;
                    case KbmrEventKind.Wheel:
                        WriteVarInt(bw, ZigZag(e.Delta));
                        break;
                }
            }
            bw.Flush();
            return ms.ToArray();
        }
    }

    static void WriteVarInt(BinaryWriter w, ulong value)
    {
        do
        {
            byte current = (byte)(value & 0x7FUL);
            value >>= 7;
            if (value != 0) current |= 0x80;
            w.Write(current);
        } while (value != 0);
    }

    static int VarIntSize(ulong value)
    {
        int n = 1;
        while ((value >>= 7) != 0) n++;
        return n;
    }

    static ulong ZigZag(int value)
    {
        return (ulong)(((long)value << 1) ^ ((long)value >> 31));
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
}

// ---- 极简 JSON 序列化（Dictionary<string,object> / IList / 基元） ----
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
        var enumerable = value as IEnumerable;
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
