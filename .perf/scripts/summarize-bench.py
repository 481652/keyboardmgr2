# -*- coding: utf-8 -*-
"""汇总 .perf/data 下基准 JSON，输出紧凑摘要（markdown 表格 + JSON）。"""
import json, sys, os, statistics

base = r'D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\data'

def load(name):
    p = os.path.join(base, name)
    with open(p, 'r', encoding='utf-8') as f:
        text = f.read()
    try:
        return json.loads(text)
    except Exception as e:
        print('PARSE FAIL', name, e, file=sys.stderr)
        # 尝试去掉可能的尾部垃圾
        i = text.rfind('}')
        return json.loads(text[:i+1])

out = {}
# ---------- click ----------
click = load('bench-click.json')
summary = click.get('summary', {})
meta = click.get('meta', {})
out['click'] = summary
out['click_meta'] = meta
out['click_runs'] = len(click.get('clickRuns', []))
out['key_runs'] = len(click.get('keyRuns', []))

lines = []
lines.append('### 连点基准摘要（3 次有效重复的中位数）\n')
lines.append('| 内核 | 配置间隔 | 送达次数/2s | 实际速率 | 实际间隔均值 | σ | P95 | 最大 | 丢点率 | 迟到率 | CPU(单核) |')
lines.append('|---|---|---|---|---|---|---|---|---|---|---|')
label = {'native': '原生 OpCore', 'managed': '托管回退'}
for k in ('native', 'managed'):
    for row in summary.get(k, []):
        lines.append('| {} | {} ms | {} | {:.1f} 次/s | **{:.2f} ms** | {:.2f} | {:.2f} | {:.2f} | {:.1f}% | {:.1f}% | {:.1f}% |'.format(
            label[k], row['intervalMs'], row['deliveredMedian'], row['rateMedian'], row['meanMedian'],
            row['stdMedian'], row['p95Median'], row['maxMedian'], row['lossRateMedian']*100,
            row['lateRateMedian']*100, row['cpuPercentMedian']))
lines.append('')
lines.append('### 键盘连点周期（配置间隔 50 ms，3 次有效重复中位数）\n')
lines.append('| 用例 | 周期 | 按住时长 | 释放→下一次按下间隔 | Ctrl→主键延迟 |')
lines.append('|---|---|---|---|---|')
for row in summary.get('key', []):
    lines.append('| {} | **{:.2f} ms** | {:.2f} ms | {:.2f} ms | {:.2f} ms |'.format(
        row['case'], row['periodMedianMs'], row['holdMedianMs'], row['gapMedianMs'], row['ctrlToPrimaryMedianMs']))
lines.append('')

# ---------- data path ----------
if os.path.exists(os.path.join(base, 'bench-data.json')):
    data = load('bench-data.json')
    out['data'] = data
    lines.append('### 数据通路微基准\n')
    lines.append('```json')
    lines.append(json.dumps(data, ensure_ascii=False, indent=1)[:6000])
    lines.append('```')

md = '\n'.join(lines)
out['markdown'] = md
with open(os.path.join(base, 'bench-summary.json'), 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
with open(os.path.join(base, 'bench-summary.md'), 'w', encoding='utf-8') as f:
    f.write(md)
print(md)
