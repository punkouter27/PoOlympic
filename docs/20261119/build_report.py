"""Builds DOCS/20261119/overnight_training_report.html from the gate files in parity/ (run from the repo root:
python DOCS/20261119/build_report.py). Every number in the charts comes from parity/watch_<run>.jsonl."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "parity"
OUT = Path(__file__).with_name("overnight_training_report.html")

RUNS = [  # run, body, label, warm start
    ("m2spin_v1", "matt", "Spin (15 % in-place spins)", "m2ramp_ss_v1 it 200 (ONNX rebuild)"),
    ("m2hold_v1", "matt", "SpinHold (+ spot-drift term)", "m2spin_v1 it 399"),
    ("m2bump_v1", "matt", "SpinBump (+ crowd bumps)", "m2spin_v1 it 350"),
    ("z2warm_v1", "zombie", "Warm (deployed recipe + bands)", "zombie_rung2.onnx (rebuild)"),
    ("z2warm_v2", "zombie", "Warm2 (sprint to 2.99 m/s, slow crab, spins)", "z2warm_v1 it 100"),
    ("z2warm_v3", "zombie", "Warm2, lr reset 1e-4", "z2warm_v2 it 50"),
    ("z2warm_v4", "zombie", "Warm2, action std floor 0.15", "z2warm_v2 it 50"),
]


def rows(run):
    f = P / f"watch_{run}.jsonl"
    out = []
    if f.exists():
        for line in f.read_text().splitlines():
            r = json.loads(line)
            g = re.match(r"(\d+)/(\d+)", r.get("g1_2", ""))
            t = re.match(r"([\d.]+)/8", r.get("turntable", ""))
            out.append({"it": r["it"], "g1": int(g.group(1)) if g else None, "falls": r.get("g1_2_falls"),
                        "tt": float(t.group(1)) if t else None, "tt_raw": r.get("turntable", "")})
    return out


data = {run: rows(run) for run, *_ in RUNS}
meta = {run: {"body": b, "label": l, "init": i} for run, b, l, i in RUNS}
OUT.write_text(Path(__file__).with_name("report_template.html").read_text(encoding="utf-8")
               .replace("/*DATA*/null", json.dumps({"runs": data, "meta": meta})), encoding="utf-8")
print(f"wrote {OUT} ({sum(len(v) for v in data.values())} gate rows)")
