# 键鼠管家自动化性能测试套件

一条命令完成 **构建计时 → 启动 → 空闲 → 连点内核 → 连发 → 数据通路 → 退出残留** 的全自动性能回归，自动判定 PASS/WARN/FAIL 并生成 Markdown 报告与 JSON 结果。

- 快速模式：约 **2–4 分钟**
- 完整模式：约 **8–12 分钟**
- 适合本地发版前自检，也可直接用于 CI（通过退出码判断）

---

## 快速开始

```powershell
# 快速模式（默认，推荐日常用）
powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1

# 完整模式（发版前/需要全量间隔与更长采样）
powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Full

# 只测启动/空闲/退出，不注入键鼠（可在你正常用电脑时跑）
powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Quick -NoClick

# 把当前结果设为回归基线（之后每次运行自动对比）
powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Quick -UpdateBaseline
```

运行结束后会在 `.perf\results\<时间戳>\` 生成：

```
perf-report.md        # 中文性能报告（结论 + 检查明细 + 各项指标表 + 基线对比）
perf-results.json     # 机器可读结果（指标/检查/环境/耗时）
raw\                  # 各探针原始 JSON（startup/idle/bench-click/rapidfire/bench-data）
logs\                 # 构建与各阶段日志
```

同时更新快捷方式：`.perf\results\latest-report.md`、`.perf\results\latest-results.json`。

> `.perf\results\`（结果、日志、原始数据）已在 `.gitignore` 中忽略，不会污染仓库；如需留档，请自行复制 `perf-report.md`。

---

## 参数

| 参数 | 说明 |
|---|---|
| `-Quick` / `-Full` | 模式选择；默认 `-Quick`。两者不能同时用 |
| `-SkipBuild` | 跳过构建（要求 `bin\Release\keyboardmgr2.exe` 已存在） |
| `-Rebuild` | 构建阶段用 `/t:Rebuild`（完整重建耗时，快速模式默认增量构建） |
| `-NoClick` | 跳过注入键鼠的连点/连发阶段（只做启动/空闲/数据/退出检查） |
| `-KillExisting` | 若检测到 keyboardmgr2 正在运行，自动结束它而不是报错退出 |
| `-UpdateBaseline` | 用本次结果写入 `scripts\perf-baseline.json` 作为后续回归基线 |
| `-Ci` | CI 模式：减少彩色输出，末行输出 `RESULT=... REPORT=...` |
| `-OutDir <path>` | 自定义结果目录 |
| `-ThresholdsPath <path>` | 自定义阈值文件（默认 `scripts\perf-thresholds.json`） |
| `-BaselinePath <path>` | 自定义基线文件（默认 `scripts\perf-baseline.json`） |
| `-MSBuildPath <path>` | 手动指定 MSBuild.exe（默认自动查找 VS2022/2026） |
| `-Configuration <name>` | 默认 `Release` |

---

## 套件做了什么

| 阶段 | 内容 | 关键指标 |
|---|---|---|
| 1. 构建 | 增量/全量构建并计时，校验 exe 产出 | 构建耗时、是否成功 |
| 2. 启动 | 预热 1 次后，正常启动 / 托盘启动各 N 次 | `WaitForInputIdle`、首个窗口可见、初始化完成、工作集、句柄、线程 |
| 3. 空闲 | 托盘静默 + 主窗口打开两段采样 | 平均 CPU、工作集、句柄漂移、UI 线程探针 |
| 4. 连点内核 | 原生 `OpCore64.dll` vs 托管回退内核 × 多档间隔 × 多次重复 | 实际间隔均值/σ/P95、送达数、丢点率、CPU；键盘单键/组合键周期 |
| 5. 连发 | 通过真实热键 `Ctrl+F2` 触发程序自身连发流程，向测试窗口发送 N 条 | 实际间隔均值/P95、首条延迟、UI 线程最大阻塞、CPU |
| 6. 数据通路 | 真实源码编译的编解码/录制存储/设置读写基准 | 10 万事件序列化/反序列化、保存/加载、设置读取 |
| 7. 退出 | 启动→强杀→检查残留进程 | 退出残留 |

**自动处理的环境问题**：单实例冲突（检测到已运行会报错或 `-KillExisting`）、启动时计划任务注册触发的 UAC（临时把 `DoRegisterAutoStartTask` 置 0，结束后恢复原值）、测试进程清理、基准工具按源码时间戳自动重建。

---

## 阈值与基线

### 1. 绝对阈值

编辑 `scripts\perf-thresholds.json`：

```jsonc
"limits": {
  "startup": { "visibleMs": 1800 },     // 启动窗口可见上限
  "idle": { "avgCpuPct": 3.0 },         // 空闲 CPU 上限
  "click":  { "nativeMeanTolerancePct": 15 }, // 原生内核间隔误差上限（%）
  "rapidfire": { "achievedAvgOverPct": 50 }   // 连发实际间隔允许超出配置值的比例
}
```

- `WARN` 级别不导致退出码非 0（如 CPU 占用、托管内核精度、数据通路耗时等参考项）。
- `FAIL` 级别会导致退出码 1（构建失败、启动/空闲超标、原生连点/连发/退出残留超标等）。

### 2. 回归基线（推荐）

```powershell
# 第一次：把当前（确认良好）的结果设为基线
powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Quick -UpdateBaseline

# 之后每次运行：自动与 scripts\perf-baseline.json 对比，
# 核心指标（启动/原生连点/连发）退化超过 baselineTolerancePct（默认 30%）判定为 FAIL
```

基线文件是普通 JSON，可随仓库提交，用于跟踪版本间性能变化。

---

## CI 示例

```yaml
- name: Performance regression (quick)
  shell: powershell
  run: |
    powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Quick -Ci
```

- 退出码：`0` 通过（可能带 WARN）、`1` 有 FAIL、`2` 套件自身执行失败（构建失败/环境不满足）。
- CI 机器需要交互式桌面（WPF 程序无法在无桌面会话中启动）；GitHub Actions 的 windows runner 默认桌面可用，但建议 `-Quick -NoClick` 先跑非注入项，注入项单独安排在自托管 runner。

---

## 注意事项

1. **会注入真实键鼠事件**（连点/连发阶段）：运行期间请不要同时操作键盘鼠标；测试目标为套件自建的置顶窗口，不会点击你的窗口，但会短暂抢占前台。
2. **需要交互式桌面**：锁屏/远程断开连接时 WPF 窗口无法创建，套件会直接报错退出。
3. 运行前请**保存好正在编辑的文档**（注入按键可能影响前台程序；套件会尽量把事件限制在自建窗口内）。
4. 端口/驱动类外部依赖：无。
5. 若希望结果更稳定：关闭录屏/游戏/杀软全盘扫描等高负载程序；对同一版本多次运行取报告中的中位数即可。
6. 本套件复用 `.perf\scripts` 下既有探针与 `.perf\bench` 基准工具；这些目录需要随仓库完整存在（`.perf/data` 的原始 JSON 与报告不要求）。
