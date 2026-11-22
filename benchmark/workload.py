"""PoOlympic Workload: the real mjlab `train` entry point (rsl_rl PPO, SymmetricRunner) on the newest queue job's task.

Run by serve.py with the training venv:  training/.venv/Scripts/python.exe benchmark/workload.py
It mirrors the last job of training/runs/queue.txt as training/tools/train_queue.ps1 starts it
(`uv run train <task> --log-root runs ...` with POOLYMPIC_BODY set), except that it trains from scratch: the queue job
warm-starts from a local checkpoint (runs/zombie_rung2/z2warm4_init, git-ignored, not on other laptops). Env count,
horizon, network, physics and rewards are the task's own, so the work per Step is the same.
rsl_rl's runner hands collect_time and learn_time to Logger.log once per iteration; the hook below feeds them to
the Meter (one iteration = num_steps_per_env Steps), which ends the process after the measured window.
"""

from __future__ import annotations

import hashlib
import os
import re
import runpy
import shutil
import sys
from importlib import metadata
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
TRAINING = ROOT / "training"
BODY, TASK = "zombie", "PoOlympic-Zombie-Rung2-Wobble-Warm2"
# Throwaway log root inside the git-ignored training/runs/; wiped before every Benchmark, never a real run's folder.
LOG_ROOT = "runs/_benchmark"
ARGS = [
    TASK,
    "--log-root",
    LOG_ROOT,
    "--agent.run-name",
    "benchmark",
    "--agent.max-iterations",
    "100000",
    "--agent.save-interval",
    "100000",
]


def version(pkg: str) -> str | None:
    try:
        return metadata.version(pkg)
    except metadata.PackageNotFoundError:
        return None


def config_hash(log_dir: str | None) -> str | None:
    """Hash of the resolved env + agent config mjlab wrote to <run>/params before creating the runner."""
    if not log_dir:
        return None
    h = hashlib.sha1()
    for name in ("env.yaml", "agent.yaml"):
        p = Path(log_dir) / "params" / name
        text = p.read_text(encoding="utf-8") if p.exists() else ""
        h.update(re.sub(r" at 0x[0-9a-fA-F]+", "", text).encode())
    return h.hexdigest()[:10]


def main() -> None:
    os.environ["POOLYMPIC_BODY"] = BODY
    from rsl_rl.runners import OnPolicyRunner
    from rsl_rl.utils.logger import Logger

    meter: core.Meter | None = None
    init = OnPolicyRunner.__init__

    def init_(self, env, train_cfg, log_dir=None, *a, **k):
        nonlocal meter
        init(self, env, train_cfg, log_dir, *a, **k)
        if meter is not None:
            return
        policy = train_cfg.get("actor") or train_cfg.get("policy") or {}
        workload = dict(
            repo="PoOlympic",
            trainer=f"mjlab {version('mjlab')} + rsl_rl {version('rsl-rl-lib')}",
            script="mjlab train (training/.venv)",
            args=" ".join(ARGS),
            body=BODY,
            init="scratch (queue job warm-starts from a local checkpoint; same per-Step work)",
            num_envs=env.num_envs,
            horizon=train_cfg["num_steps_per_env"],
            net=policy.get("hidden_dims") or policy.get("actor_hidden_dims"),
            config_hash=config_hash(log_dir),
        )
        versions = dict(
            python=sys.version.split()[0],
            torch=version("torch"),
            mujoco_warp=version("mujoco-warp"),
            warp=version("warp-lang"),
        )
        meter = core.Meter(workload, versions)

    log = Logger.log

    def log_(self, *a, collect_time: float, learn_time: float, **k):
        log(self, *a, collect_time=collect_time, learn_time=learn_time, **k)
        meter.block(collect_time, learn_time, meter.workload["horizon"])

    OnPolicyRunner.__init__, Logger.log = init_, log_
    shutil.rmtree(TRAINING / LOG_ROOT, ignore_errors=True)
    os.chdir(TRAINING)
    sys.path.insert(0, str(TRAINING))
    sys.argv = ["train", *ARGS]
    try:
        import mjlab.scripts.train as train

        runpy.run_path(train.__file__, run_name="__main__")
    except BaseException as e:  # noqa: BLE001 - every failure becomes a failed row, never a silent miss
        if meter is None:
            meter = core.Meter({"repo": "PoOlympic", "args": " ".join(ARGS)}, {"python": sys.version.split()[0]})
        meter.finish(ok=False, error=core.fail_text(e))


if __name__ == "__main__":
    main()
