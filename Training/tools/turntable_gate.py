"""Event 12 gate for a MATT Rung 2 candidate (2026-11-19): the heat of tests/test_events.py::test_turntable_heat on
several seeds (all-MATT turntable8 scene, 8 lanes with sampled traits, 3 turns at 3 rad/s inside the 0.75 m ring).
m2ramp_ss_v1 it 200 passed G1 100/100 and was held back by this heat (3.5 of 8 DONE over seeds 0-5; r2_v8 7.3).

Usage: uv run python tools/turntable_gate.py <brain.onnx> [--seeds 6] [--workers 6] [--out x.json]
Prints: "turntable: DONE 7,7,8,7,8,7 mean 7.33/8 min 7 (test bar 6)".
"""

from __future__ import annotations

import argparse
import json
import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))


def one(job: tuple[str, int]) -> dict:
    from poolympic.events import turntable

    brain, seed = job
    res = turntable.run_heat(Path(brain), seed=seed)
    done = [l for l in res.lanes if l.status == "DONE"]
    return {
        "seed": seed,
        "done": len(done),
        "statuses": [l.status for l in res.lanes],
        "max_drift": [round(l.max_drift_m, 3) for l in res.lanes],
        "time": [round(l.time_s, 2) for l in done],
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("brain")
    ap.add_argument("--seeds", type=int, default=6)
    ap.add_argument("--workers", type=int, default=6)
    ap.add_argument("--out", type=Path, default=None)
    a = ap.parse_args()
    with ProcessPoolExecutor(max_workers=min(a.workers, a.seeds)) as ex:
        rows = list(ex.map(one, [(a.brain, s) for s in range(a.seeds)]))
    done = [r["done"] for r in rows]
    mean = sum(done) / len(done)
    print(f"turntable: DONE {','.join(map(str, done))} mean {mean:.2f}/8 min {min(done)} (test bar 6)")
    if a.out:
        a.out.write_text(json.dumps({"brain": Path(a.brain).name, "mean_done": mean, "heats": rows}, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
