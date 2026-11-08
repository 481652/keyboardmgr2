# -*- coding: utf-8 -*-
"""对比新旧版本性能数据，生成 markdown 表格与 compare.json。"""
import json, os, statistics as st

ROOT = r'D:\文档\Visual Studio\Projects\keyboardmgr2_'
BASE = os.path.join(ROOT, '.perf', 'baseline-v231')
DATA = os.path.join(ROOT, '.perf', 'data')


def load(path):
    with open(path, 'rb') as f:
        raw = f.read()
    for enc in ('utf-8-sig', 'utf-8', 'gbk', 'cp936'):
        try:
            return json.loads(raw.decode(enc))
        except Exception:
            continue
    raise RuntimeError('cannot parse ' + path)


def med(vals):
    vals = [v for v in vals if v is not None]
    return round(st.median(vals), 2) if vals else None


def fmt(v, nd=2):
    if v is None:
        return '—'
    return ('%.*f' % (nd, v))


out = {}
lines = []

# ---------------- 1. click ----------------
old_click = load(os.path.join(BASE, 'bench-click.json')).get('summary', {})
new_click = load(os.path.join(DATA, 'bench-click-v2.json')).get('summary', {})
out['click'] = {'old': old_click, 'new': new_click}
lines.append('### A. 连点内核（3 次有效重复中位数）\n')
lines.append('| 内核 | 间隔 | 旧版送达 | 新版送达 | 旧版均值 | 新版均值 | 旧版 σ | 新版 σ | 旧版 P95 | 新版 P95 | 旧版丢点 | 新版丢点 | 旧版 CPU | 新版 CPU |')
lines.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
name = {'native': '原生', 'managed': '托管'}
for k in ('native', 'managed'):
    old_map = {r['intervalMs']: r for r in old_click.get(k, [])}
    new_map = {r['intervalMs']: r for r in new_click.get(k, [])}
    for iv in sorted(new_map):
        o = old_map.get(iv, {})
        n = new_map.get(iv, {})
        lines.append('| {} | {} ms | {} | {} | {} | **{}** | {} | {} | {} | {} | {}% | {}% | {}% | {}% |'.format(
            name[k], iv,
            fmt(o.get('deliveredMedian'), 0), fmt(n.get('deliveredMedian'), 0),
            fmt(o.get('meanMedian')), fmt(n.get('meanMedian')),
            fmt(o.get('stdMedian')), fmt(n.get('stdMedian')),
            fmt(o.get('p95Median')), fmt(n.get('p95Median')),
            fmt((o.get('lossRateMedian') or 0) * 100, 1), fmt((n.get('lossRateMedian') or 0) * 100, 1),
            fmt(o.get('cpuPercentMedian'), 1), fmt(n.get('cpuPercentMedian'), 1)))
lines.append('')
lines.append('### B. 键盘连点周期（配置 50 ms）\n')
lines.append('| 用例 | 旧版周期 | 新版周期 | 变化 |')
lines.append('|---|---|---|---|')
old_key = {r['case']: r for r in old_click.get('key', [])}
new_key = {r['case']: r for r in new_click.get('key', [])}
for case in ['native-single-A', 'native-combo-Ctrl+A', 'managed-single-A', 'managed-combo-Ctrl+A']:
    o = old_key.get(case, {}).get('periodMedianMs')
    n = new_key.get(case, {}).get('periodMedianMs')
    delta = None if not (o and n) else round((n - o) / o * 100, 1)
    lines.append('| {} | {} ms | **{} ms** | {} |'.format(case, fmt(o), fmt(n), ('%+.1f%%' % delta) if delta is not None else '—'))
lines.append('')

# ---------------- 2. data path ----------------
old_data = load(os.path.join(BASE, 'bench-data.json'))
new_data = load(os.path.join(DATA, 'bench-data.json'))
out['data'] = {'old': old_data, 'new': new_data}


def data_row(label, o, n, unit='ms', nd=3):
    return '| {} | {} {} | {} {} |'.format(label, fmt(o, nd), unit, fmt(n, nd), unit)


codec_old = {r['events']: r for r in old_data.get('codec', [])}
codec_new = {r['events']: r for r in new_data.get('codec', [])}
lines.append('### C. 数据通路（3 次中位数）\n')
lines.append('| 项目 | 旧版 | 新版 |')
lines.append('|---|---|---|')
for ev in sorted(codec_new):
    o = codec_old.get(ev, {})
    n = codec_new.get(ev, {})
    lines.append(data_row('KbmrCodec.Serialize %d 事件' % ev, o.get('serializeMedianMs'), n.get('serializeMedianMs')))
    lines.append(data_row('KbmrCodec.Deserialize %d 事件' % ev, o.get('deserializeMedianMs'), n.get('deserializeMedianMs')))
so = old_data.get('recordingStore', {}); sn = new_data.get('recordingStore', {})
lines.append(data_row('RecordingStore.Save 10万', so.get('saveMedianMs'), sn.get('saveMedianMs')))
lines.append(data_row('RecordingStore.Load 10万', so.get('loadMedianMs'), sn.get('loadMedianMs')))
ko = old_data.get('keyNameMapper', {}); kn = new_data.get('keyNameMapper', {})
lines.append(data_row('KeyNameMapper 1000 次解析', ko.get('parseMedianMs'), kn.get('parseMedianMs'), 'ms', 4))
so = old_data.get('settings', {}); sn = new_data.get('settings', {})
lines.append(data_row('ReadSetting x1000', so.get('readMedianMs'), sn.get('readMedianMs')))
lines.append(data_row('WriteSetting x1000', so.get('writeMedianMs'), sn.get('writeMedianMs')))
lines.append('')

# ---------------- 3. startup ----------------
def load_runs(path):
    d = load(path)
    return d if isinstance(d, list) else [d]


startup_old = load_runs(os.path.join(DATA, 'startup-v231-warm.json'))
startup_new = load_runs(os.path.join(DATA, 'startup-v2c.json'))


def pick(runs, mode, key):
    return [r.get(key) for r in runs if r.get('mode') == mode]


def srow(label, mode):
    o = pick(startup_old, mode, 'tInputIdleMs') if False else []
    return None


lines.append('### D. 启动（A/B 同环境复测，中位数）\n')
lines.append('| 指标 | 旧版 正常启动 | 新版 正常启动 | 旧版 托盘启动 | 新版 托盘启动 |')
lines.append('|---|---|---|---|---|')
metric_map = [
    ('WaitForInputIdle', 'tInputIdleMs', 0),
    ('首个窗口可见', 'tVisibleMs', 0),
    ('CPU 静止（约）', 'tCpuQuietMs', 0),
    ('结算 CPU 时间', 'cpuMsAtSettle', 0),
    ('工作集 MB', 'workingSetMB', 1),
    ('私有内存 MB', 'privateMB', 1),
    ('句柄数', 'handles', 0),
    ('线程数', 'threads', 0),
    ('GDI', 'gdi', 0),
    ('USER', 'user', 0),
]
for label, key, nd in metric_map:
    vals = []
    for runs in (startup_old, startup_new):
        for mode in ('normal', 'autostart'):
            vals.append(med(pick(runs, mode, key)))
    lines.append('| {} | {} | {} | {} | {} |'.format(label, *(fmt(v, nd) for v in vals)))
lines.append('')

# ---------------- 4. idle ----------------
def idle_summary(path):
    d = load(path)
    res = {}
    for sc in d:
        res[sc['mode']] = sc
    return res


idle_old = idle_summary(os.path.join(DATA, 'idle-v231-fresh.json'))
idle_new = idle_summary(os.path.join(DATA, 'idle-v2.json'))
lines.append('### E. 空闲资源（托盘 3 分钟 / 主窗口 60 秒）\n')
lines.append('| 指标 | 旧版托盘 | 新版托盘 | 旧版主窗口 | 新版主窗口 |')
lines.append('|---|---|---|---|---|')
for label, key, nd in [
    ('CPU 平均 %', 'avgCpuPct', 3),
    ('CPU 60s 窗口 %', 'idleCpuPct60s', 3),
    ('工作集 起始→结束 MB', None, 1),
    ('私有内存 起始→结束 MB', None, 1),
    ('句柄 起始→结束', None, 0),
    ('线程 起始→结束', None, 0),
    ('GDI', 'gdiEnd', 0),
    ('USER', 'userEnd', 0),
    ('UI 探针最大 ms', 'uiPingMaxMs', 1),
]:
    vals = []
    for mode in ('tray', 'window'):
        for src in (idle_old, idle_new):
            d = src.get(mode, {})
            if key is None:
                if label.startswith('工作集'):
                    vals.append('{}→{}'.format(fmt(d.get('wsStartMB'), 1), fmt(d.get('wsEndMB'), 1)))
                elif label.startswith('私有'):
                    vals.append('{}→{}'.format(fmt(d.get('privStartMB'), 1), fmt(d.get('privEndMB'), 1)))
                elif label.startswith('句柄'):
                    vals.append('{}→{}'.format(fmt(d.get('handleStart'), 0), fmt(d.get('handleEnd'), 0)))
                elif label.startswith('线程'):
                    vals.append('{}→{}'.format(fmt(d.get('threadStart'), 0), fmt(d.get('threadEnd'), 0)))
            else:
                vals.append(fmt(d.get(key), nd))
    lines.append('| {} | {} | {} | {} | {} |'.format(label, *vals))
lines.append('')

# ---------------- 5. rapidfire ----------------
def rf(path):
    d = load(path)
    return {r['scenario']: r for r in d}


rf_old = rf(os.path.join(BASE, 'rapidfire.json'))
rf_new = rf(os.path.join(DATA, 'rapidfire-v2.json'))
lines.append('### F. 连发（列表快速发送）\n')
lines.append('| 场景 | 旧版均值 | 新版均值 | 旧版 P95 | 新版 P95 | 旧版 CPU | 新版 CPU | 旧版首条延迟 | 新版首条延迟 |')
lines.append('|---|---|---|---|---|---|---|---|---|')
for sc in ['throughput-100ms', 'throughput-50ms', 'stop-latency']:
    o = rf_old.get(sc, {}); n = rf_new.get(sc, {})
    lines.append('| {} | {} | {} | {} | {} | {}% | {}% | {} | {} |'.format(
        sc,
        fmt(o.get('achievedAvgMs')), fmt(n.get('achievedAvgMs')),
        fmt((o.get('intervalStats') or {}).get('p95')), fmt((n.get('intervalStats') or {}).get('p95')),
        fmt(o.get('cpuPctDuring'), 1), fmt(n.get('cpuPctDuring'), 1),
        fmt(o.get('firstEnterMs'), 0), fmt(n.get('firstEnterMs'), 0)))
lines.append('')

out['markdown'] = '\n'.join(lines)
out['startup_old'] = startup_old
out['startup_new'] = startup_new
out['idle_old'] = idle_old
out['idle_new'] = idle_new
out['rf_old'] = rf_old
out['rf_new'] = rf_new
with open(os.path.join(DATA, 'compare.json'), 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=1, default=str)
md = '\n'.join(lines)
with open(os.path.join(DATA, 'compare.md'), 'w', encoding='utf-8') as f:
    f.write(md)
print(md)
