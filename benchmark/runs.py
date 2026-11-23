"""Training runs grid: Training/runs/runs.html, one row per real training run under Training/runs/, newest first.

    python benchmark/runs.py                                   rebuild Training/runs/runs.html from the run folders
    python benchmark/runs.py --record <run dir> --ended "<how>" [--launcher <script>] [--task <id>] [--body <body>]
                                                               note this machine and how the run ended, then rebuild

A run is one directory mjlab's `train` writes: Training/runs/<experiment>/<YYYY-MM-DD_HH-MM-SS>_<run name>/ with
params/agent.yaml + params/env.yaml (horizon, iterations, warm start, env count), model_<iteration>.pt checkpoints,
git/<repo>.diff (the commit) and events.out.tfevents.<start epoch>.<host>.<pid>.<n> (start time, machine). The grid is
rebuilt from those plain files, so a run started by hand, or one whose launcher was killed, still gets a row. The task id
and body come from runs/queue.log (tools/train_queue.ps1), the GPU and any crash from the run's own
runs/<name>.log / .err.log. What the folder cannot tell for sure (GPU, how the run ended, task of a hand-started run)
comes from <run dir>/run_meta.json, which --record writes; tools/train_queue.ps1 and tools/detached_run.ps1 call it when
a run ends, crash or not. Without it those cells read "-" or are inferred from the files.

Steps are env steps (all parallel envs): (iteration + 1) x num_steps_per_env x num_envs at the newest checkpoint.
Speed is the run's own env steps per second, measured between the first and last checkpoint of this run directory, so a
resumed part is not credited with the steps of an earlier part. Benchmark is the newest benchmark/history.json result on
the same machine from before the run started (ms per step + env steps/s), for comparison. TensorBoard event files are
not parsed (stdlib only); core.py next to this file supplies the machine description.
"""

from __future__ import annotations

import argparse
import datetime
import html
import json
import os
import re
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
RESULTS = ROOT / "Training" / "runs"  # mjlab --log-root runs (git-ignored)
OUT = RESULTS / "runs.html"
BENCH = HERE / "history.json"
META = "run_meta.json"
TITLE = "PoOlympic"
SKIP = {"_benchmark"}  # the Benchmark's own throwaway log root (benchmark/workload.py LOG_ROOT)
RUNNING_S = 180  # a run dir written to within this many seconds is still running


# ---------------------------------------------------------------- collect


def _json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def _text(path: Path, limit: int | None = None) -> str:
    try:
        with path.open(encoding="utf-8", errors="replace") as f:
            return f.read(limit) if limit else f.read()
    except OSError:
        return ""


def _yaml(text: str, key: str, indent: str = "") -> str | None:
    """Value of a scalar `key:` line in mjlab's dumped yaml (top level unless indent is given)."""
    m = re.search(rf"^{indent}{key}:[ \t]*(\S[^\n]*)?$", text, re.M)
    v = m.group(1).strip() if m and m.group(1) else None
    return None if v in (None, "null", "~") else v


def _int(v: str | None) -> int | None:
    try:
        return int(v) if v is not None else None
    except ValueError:
        return None


def _bench_for(host: str | None, before: float) -> dict | None:
    runs = [r for r in _json(BENCH).get("runs", []) if r.get("ok") and r.get("result")]
    if host:
        runs = [r for r in runs if (r.get("machine", {}).get("hostname") or "").lower() == host.lower()]
    if not runs:
        return None
    stamp = lambda r: datetime.datetime.fromisoformat(r["when"]).timestamp()  # noqa: E731
    earlier = [r for r in runs if stamp(r) <= before]
    return (earlier or runs)[-1]


def _training_logs() -> dict[str, Path]:
    """run dir name -> runs/<name>[.rN].log, from mjlab's 'Logging experiment in directory' line."""
    logs = {}
    for p in RESULTS.glob("*.log"):
        if p.name.endswith((".err.log", "_watch.log", "_prep.log")) or p.name.startswith("queue"):
            continue
        m = re.search(r"Logging experiment in directory: (.+)", _text(p, 4000))
        if m:
            logs[re.split(r"[\\/]", m.group(1).strip())[-1]] = p
    return logs


def _queue_starts() -> list[tuple[str, str, str, str]]:
    """(MM-dd HH:mm:ss, run name, task id, body) for every START line of tools/train_queue.ps1's runs/queue.log."""
    pat = r"^\[(\d\d-\d\d \d\d:\d\d:\d\d)\] START ([^:]+): (\S+) \(body (\S+?),"
    return re.findall(pat, _text(RESULTS / "queue.log"), re.M)


def _queue_time(mm_dd_hms: str, year: int) -> float:
    return datetime.datetime.strptime(f"{year}-{mm_dd_hms}", "%Y-%m-%d %H:%M:%S").timestamp()


def _ended_from_logs(log: Path | None) -> str | None:
    if not log:
        return None
    err = _text(log.with_name(log.name[: -len(".log")] + ".err.log"))
    if "Traceback" not in err:
        return None
    tail = err[err.rindex("Traceback") :]
    exc = re.findall(r"^((?:\w+\.)*\w*(?:Error|Exception|Interrupt|Exit)\b.*)$", tail, re.M)
    if exc and exc[-1].startswith("KeyboardInterrupt"):
        return "interrupted (Ctrl+C)"
    return f"crashed: {exc[-1][:160]}" if exc else "crashed (traceback in .err.log)"


def collect() -> list[dict]:
    rows = []
    if not RESULTS.is_dir():
        return rows
    logs, starts = _training_logs(), _queue_starts()
    for agent_yaml in RESULTS.glob("*/*/params/agent.yaml"):
        d = agent_yaml.parent.parent
        if d.parent.name in SKIP or d.parent.name.startswith("_"):
            continue
        agent, env = _text(agent_yaml), _text(d / "params" / "env.yaml")
        meta = _json(d / META)
        events = sorted(d.glob("events.out.tfevents.*"))
        ev = re.match(r"events\.out\.tfevents\.(\d+)\.(.+)\.\d+\.\d+$", events[0].name) if events else None
        stamp = re.match(r"(\d{4}-\d\d-\d\d_\d\d-\d\d-\d\d)_?(.*)$", d.name)
        if ev:
            start = float(ev.group(1))
        elif stamp:
            start = datetime.datetime.strptime(stamp.group(1), "%Y-%m-%d_%H-%M-%S").timestamp()
        else:
            start = agent_yaml.stat().st_mtime
        cps = sorted(
            (int(m.group(1)), p.stat().st_mtime)
            for p in d.glob("model_*.pt")
            if (m := re.fullmatch(r"model_(\d+)\.pt", p.name))
        )
        written = [t for _, t in cps] + [p.stat().st_mtime for p in events] + [agent_yaml.stat().st_mtime]
        end = max(written)
        horizon = _int(_yaml(agent, "num_steps_per_env")) or 0
        envs = _int(_yaml(env, "num_envs", r"\s*"))
        per_it = horizon * (envs or 0)
        resume = (_yaml(agent, "resume") or "").lower() == "true"
        load_run, load_cp = _yaml(agent, "load_run"), _yaml(agent, "load_checkpoint")
        start_it = _int(m.group(1)) if resume and load_cp and (m := re.search(r"(\d+)", load_cp)) else 0
        max_its = _int(_yaml(agent, "max_iterations"))
        last_it = cps[-1][0] if cps else None
        steps = (last_it + 1) * per_it if last_it is not None and per_it else None
        rate = None
        if len(cps) >= 2 and cps[-1][1] > cps[0][1] and per_it:
            rate = (cps[-1][0] - cps[0][0]) * per_it / (cps[-1][1] - cps[0][1])
        name = _yaml(agent, "run_name") or (stamp.group(2) if stamp else d.name)
        log = logs.get(d.name)
        gpu = re.search(r'"cuda:0"\s*:\s*"([^"]+)"', _text(log, 6000)) if log else None
        year = datetime.datetime.fromtimestamp(start).year
        queued = [q for q in starts if q[1] == name and _queue_time(q[0], year) <= start + 5]
        task = meta.get("task") or (queued[-1][2] if queued else None)
        body = meta.get("body") or (queued[-1][3] if queued else None)
        finished = last_it is not None and max_its is not None and last_it >= start_it + max_its - 1
        ended = (
            meta.get("ended")
            or _ended_from_logs(log)
            or ("finished" if finished else None)
            or ("running" if time.time() - end < RUNNING_S else "still running or killed")
        )
        diff = next(iter(sorted((d / "git").glob("*.diff"))), None) if (d / "git").is_dir() else None
        sha = re.search(r"--- git commit ---\s+([0-9a-f]{7,40})", _text(diff, 400)) if diff else None
        lr, ent = _yaml(agent, "learning_rate", r"\s+"), _yaml(agent, "entropy_coef", r"\s+")
        rows.append(
            dict(
                run=name,
                exp=d.parent.name,
                start=start,
                wall=max(0.0, end - start),
                ended=ended,
                resumed=start_it > 0,
                iteration=last_it,
                steps=steps,
                rate=rate,
                bench=_bench_for(meta.get("host") or (ev.group(2) if ev else None), start),
                config=task,
                args=" · ".join(
                    filter(
                        None,
                        (
                            body and f"body {body}",
                            max_its and f"{max_its} its",
                            lr and f"lr {lr}",
                            ent and f"ent {ent}",
                        ),
                    )
                ),
                envs=envs,
                horizon=horizon,
                warm=[f"{load_run}/{load_cp}"] if resume and load_run else [],
                commit=(sha.group(1)[:7] if sha else None) or meta.get("commit"),  # the diff mjlab saved at start
                host=meta.get("host") or (ev.group(2) if ev else None),
                gpu=meta.get("gpu") or (gpu.group(1) if gpu else None),
                device=meta.get("device"),
                launcher=meta.get("launcher"),
            )
        )
    rows.sort(key=lambda r: r["start"], reverse=True)
    return rows


# ---------------------------------------------------------------- render


def _dur(s: float) -> str:
    h, m = divmod(int(s) // 60, 60)
    return f"{h} h {m:02d} m" if h else f"{m} m"


def _num(n: float | None, digits: int = 0) -> str:
    return "-" if n is None else f"{n:,.{digits}f}"


def render(rows: list[dict]) -> str:
    e = html.escape
    body = []
    for r in rows:
        b = r["bench"]
        if b:
            notes = [f"{b['result']['env_steps_per_s']:,} env steps/s", e(b["when"][:10])]
            if datetime.datetime.fromisoformat(b["when"]).timestamp() > r["start"]:
                notes.append("taken after the run")
            if not r["host"]:
                notes.append("machine unknown")
            bench = f"{b['result']['step_time_ms']} ms<small>{' · '.join(notes)}</small>"
        else:
            bench = "-<small>no benchmark on this machine</small>"
        ended = e(r["ended"])
        cls = "bad" if re.search(r"crash|died|killed|HALT|error|exception", r["ended"], re.I) else ""
        sub = e(r["exp"]) + (" · resumed" if r["resumed"] else "")
        it = f"iteration {r['iteration']}" if r["iteration"] is not None else ""
        body.append(
            "<tr>"
            f"<td><b>{e(r['run'])}</b><small>{sub}</small></td>"
            f"<td>{datetime.datetime.fromtimestamp(r['start']).strftime('%Y-%m-%d %H:%M')}</td>"
            f'<td class="n">{_dur(r["wall"])}</td>'
            f'<td class="{cls}">{ended}</td>'
            f'<td class="n">{_num(r["steps"])}<small>{it}</small></td>'
            f'<td class="n"><b>{_num(r["rate"])}</b><small>env steps/s</small></td>'
            f'<td class="n">{bench}</td>'
            f"<td>{e(r['config'] or '-')}<small>{e(r['args'])}</small></td>"
            f'<td class="n">{_num(r["envs"])}<small>{r["horizon"] or "-"} steps/iteration</small></td>'
            f"<td>{e(', '.join(r['warm']) or 'cold')}</td>"
            f'<td class="mono">{e(r["commit"] or "-")}</td>'
            f"<td>{e(r['host'] or '-')}<small>{e(r['gpu'] or '')}{' · ' + e(r['device']) if r['device'] else ''}"
            f"</small></td>"
            "</tr>"
        )
    latest = rows[0] if rows else None
    prev = next((r for r in rows[1:] if r["rate"]), None) if rows else None
    delta = ""
    if latest and latest["rate"] and prev:
        pct = 100 * (latest["rate"] / prev["rate"] - 1)
        delta = f'<span class="{"up" if pct >= 0 else "down"}">{pct:+.0f}% vs {e(prev["run"])}</span>'
    summary = (
        f'<div class="sum"><div><span>Latest run</span><b>{e(latest["run"])}</b></div>'
        f"<div><span>Speed</span><b>{_num(latest['rate'])} env steps/s</b>{delta}</div>"
        f"<div><span>Wall-clock</span><b>{_dur(latest['wall'])}</b></div>"
        f"<div><span>Runs listed</span><b>{len(rows)}</b></div></div>"
        if latest
        else "<p>No training runs under Training/runs/ yet.</p>"
    )
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    return f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>{e(TITLE)} training runs</title>
<style>
:root{{--bg:#f5f7f6;--card:#fff;--ink:#18211f;--muted:#5d6b67;--line:#d8e0dd;--accent:#0f6b5c;--bad:#b42318;--up:#1f7a43}}
@media (prefers-color-scheme:dark){{:root{{--bg:#0f1514;--card:#161e1c;--ink:#e2eae7;--muted:#91a19c;--line:#27332f;
--accent:#5cc9b0;--bad:#ff8a7a;--up:#5fcf8a;color-scheme:dark}}}}
body{{margin:0;background:var(--bg);color:var(--ink);font:14px/1.5 "Segoe UI",system-ui,sans-serif;padding:24px 16px}}
h1{{font-size:20px;margin:0 0 4px}} p{{margin:0;color:var(--muted)}} .wrap{{max-width:1500px;margin:auto;display:grid;gap:16px}}
.sum{{display:flex;flex-wrap:wrap;gap:12px 40px;background:var(--card);border:1px solid var(--line);border-radius:8px;padding:14px 18px}}
.sum span{{display:block;color:var(--muted);font-size:12px}} .sum b{{font-size:20px}} .sum .up,.sum .down{{display:block;font-size:12px}}
.up{{color:var(--up)}} .down{{color:var(--bad)}}
.t{{overflow-x:auto;background:var(--card);border:1px solid var(--line);border-radius:8px}}
table{{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}}
th,td{{padding:8px 12px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;white-space:nowrap}}
th{{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);position:sticky;top:0;background:var(--card)}}
tr:last-child td{{border-bottom:0}} td.n{{text-align:right}} td small{{display:block;color:var(--muted);font-size:11px}}
td.bad{{color:var(--bad)}} .mono{{font-family:Consolas,monospace}}
</style></head><body><div class="wrap">
<div><h1>{e(TITLE)} training runs</h1><p>Every real training run under Training/runs/, newest first. Rebuilt {now} by
benchmark/runs.py. Steps = env steps (all parallel envs) at the newest checkpoint; Speed = env steps/s of this run
directory, first to last checkpoint; Benchmark = the newest benchmark.ps1 result on the same machine before the run
(ms per step, env steps/s).</p></div>
{summary}
<div class="t"><table><thead><tr><th>Run</th><th>Started</th><th>Wall-clock</th><th>Ended</th><th>Steps</th>
<th>Speed</th><th>Benchmark</th><th>Config</th><th>Envs</th><th>Warm start</th><th>Commit</th><th>Machine</th></tr></thead>
<tbody>{"".join(body)}</tbody></table></div></div></body></html>
"""


def build() -> Path:
    RESULTS.mkdir(parents=True, exist_ok=True)
    tmp = OUT.with_suffix(".tmp")
    tmp.write_text(render(collect()), encoding="utf-8")
    os.replace(tmp, OUT)
    return OUT


def record(run_id: str, ended: str, launcher: str | None, task: str | None = None, body: str | None = None) -> None:
    """Write <run dir>/run_meta.json: this machine, the commit, the task and how the run ended.

    run_id is the run directory: absolute, or relative to Training/runs/ (e.g. zombie_rung2/2026-11-13_04-22-39_z2_v1).
    A directory that does not exist (training died before mjlab created it) is not created: nothing to annotate."""
    d = Path(run_id)
    d = d if d.is_absolute() else RESULTS / d
    if not (d / "params").is_dir():
        print(f"no run directory {d}: nothing recorded", file=sys.stderr)
        return
    sys.path.insert(0, str(HERE))
    import core

    m = core.machine()
    meta = dict(
        host=m["hostname"],
        gpu=m["gpu"],
        cpu=m["cpu"],
        device="CPU" if os.environ.get("CUDA_VISIBLE_DEVICES") == "-1" else None,
        commit=core.git_commit(),
        ended=ended,
        launcher=launcher,
        task=task,
        body=body,
        recorded=datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
    )
    (d / META).write_text(json.dumps(meta, indent=1), encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description="Rebuild Training/runs/runs.html (the training runs grid).")
    p.add_argument("--record", metavar="RUN_DIR", help="note this machine and how the run ended first")
    p.add_argument("--ended", default="exited", help="how the run ended, in words")
    p.add_argument("--launcher", help="the script that ran it")
    p.add_argument("--task", help="the mjlab task id it trained")
    p.add_argument("--body", help="POOLYMPIC_BODY it trained")
    a = p.parse_args()
    if a.record:
        record(a.record, a.ended, a.launcher, a.task, a.body)
    print(f"Training runs grid: {build()}")


if __name__ == "__main__":
    main()
