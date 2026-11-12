"""Five high-collision arena events sharing a single planar target-homing controller:

- sumo      (#2  Sumo Ring):               8 on a r=1.45 m raised ring; push rivals out or down; last standing wins.
- bridge    (#3  Narrow Bridge Crossing):  4v4 head-on crossing on a 14 m x 1.44 m raised bridge; fastest safe cross wins.
- pads      (#7  Shrinking Safe-Zone):     4 rounds x 9 s with 7 -> 5 -> 3 -> 1 seeded pads (r=0.90 m); outside/down = out.
- beacon    (#10 Center Beacon Scramble):  30 s King-of-the-Hill inside r=1.15 m at (0,0) with fall recovery; max zone s wins.
- hill      (#25 Hilltop Scramble):        4 converging 1.5 deg ramps up to a 2.6x2.6 m summit with fall recovery; max hold s wins.
"""

from __future__ import annotations

import json
import math
from dataclasses import asdict, dataclass, field
from pathlib import Path

import mujoco
import numpy as np

from .. import action_log as AL
from .. import contract as C
from .iron_pedestal import Traits, make_lanes
from .track import Recovery

MODES = {
    "sumo": {
        "scene": "scene_sumo8.xml",
        "layout": "sumo8_layout.json",
        "max_s": 30.0,
        "recover": False,
        "support": ("ground", "sumo_ring"),
    },
    "bridge": {
        "scene": "scene_bridge8.xml",
        "layout": "bridge8_layout.json",
        "max_s": 30.0,
        "recover": False,
        "support": ("ground", "bridge"),
    },
    "pads": {
        "scene": "scene_pads8.xml",
        "layout": "pads8_layout.json",
        "max_s": 36.0,
        "recover": True,
        "support": ("ground",),
    },
    "beacon": {
        "scene": "scene_beacon8.xml",
        "layout": "beacon8_layout.json",
        "max_s": 30.0,
        "recover": True,
        "support": ("ground",),
    },
    "hill": {
        "scene": "scene_hill8.xml",
        "layout": "hill8_layout.json",
        "max_s": 35.0,
        "recover": True,
        "support": (
            "ground", "hill_summit",
            "hill_ramp_e", "hill_ramp_w", "hill_ramp_n", "hill_ramp_s",
            "hill_pad_e", "hill_pad_w", "hill_pad_n", "hill_pad_s",
        ),
    },
}

SUMO_RING_R = 1.45
BRIDGE_HALF_W = 0.80
BRIDGE_LEN = 12.0
PAD_COUNTS = (7, 5, 3, 1)
PAD_ROUND_S = 9.0
PAD_RADIUS = 1.15
BEACON_RADIUS = 1.15
HILL_SUMMIT_HALF = 1.32
HILL_RAMP_AXIS_HALF = 0.85


@dataclass
class ArenaLane:
    lane: int
    traits: Traits
    body: str = "matt"
    status: str = ""                 # FINISHED / OUT / FELL / DNF
    finish_s: float | None = None
    out_at: float | None = None
    score_s: float = 0.0             # zone / summit hold seconds
    progress_m: float = 0.0          # bridge distance / -center distance tie-break
    falls: int = 0
    rounds: int = 0                  # pads: rounds survived
    place: int = 0


@dataclass
class ArenaResult:
    mode: str
    seed: int
    duration_s: float
    lanes: list[ArenaLane] = field(default_factory=list)


def wrap_pi(a: float) -> float:
    return (a + math.pi) % (2 * math.pi) - math.pi


def steer_to_point(yaw: float, px: float, py: float, tx: float, ty: float,
                   max_vx: float = 3.5, slow_r: float = 1.2, stop_r: float = 0.0) -> np.ndarray:
    """Planar homing command in MATT units towards world (tx, ty)."""
    dx, dy = tx - px, ty - py
    dist = math.hypot(dx, dy)
    if dist <= stop_r:
        return np.zeros(3)
    target_yaw = math.atan2(dy, dx)
    err = wrap_pi(target_yaw - yaw)
    wz = float(np.clip(C.HEADING_GAIN * err, -C.STEER_WZ_LIMIT, C.STEER_WZ_LIMIT))
    turn_scale = max(0.08, math.cos(err)) if abs(err) < 0.5 * math.pi else 0.08
    if slow_r > stop_r and dist < slow_r:
        vx = max_vx * (0.20 + 0.80 * (dist - stop_r) / (slow_r - stop_r))
    else:
        vx = max_vx
    return np.array([vx * turn_scale, 0.0, wz], dtype=np.float64)


def round_pads(seed: int, rnd: int) -> list[tuple[float, float]]:
    """Deterministic seeded pad centres for Shrinking Safe-Zone (matches C# ArenaBrawlEvent.RoundPads)."""
    count = PAD_COUNTS[min(rnd, len(PAD_COUNTS) - 1)]
    if count == 1:
        rng = np.random.default_rng([seed, 4100, rnd])
        return [(float(rng.uniform(-0.5, 0.5)), float(rng.uniform(-0.5, 0.5)))]
    rng = np.random.default_rng([seed, 4100, rnd])
    pts: list[tuple[float, float]] = []
    base_ang = float(rng.uniform(0.0, 2.0 * math.pi))
    for k in range(count):
        ang = base_ang + 2.0 * math.pi * k / count + float(rng.uniform(-0.18, 0.18))
        rad = 1.65 + float(rng.uniform(0.0, 1.10))
        pts.append((rad * math.cos(ang), rad * math.sin(ang)))
    return pts


def hill_waypoint(i: int, px: float, py: float, ox: float, oy: float) -> tuple[float, float, float, float]:
    """Return (tx, ty, max_vx, slow_r) that first funnels along the lane's cardinal ramp onto the summit."""
    if abs(px) <= HILL_SUMMIT_HALF and abs(py) <= HILL_SUMMIT_HALF:
        # On summit: fight near centre (small per-lane jitter prevents deadlocks)
        ang = 2.0 * math.pi * i / 8.0
        return 0.25 * math.cos(ang), 0.25 * math.sin(ang), 2.2, 0.4
    # On one of the 4 cardinal ramps: stay centered on the 2.2 m wide ramp corridor
    if abs(ox) >= abs(oy):
        # East (ox > 0) or West (ox < 0) ramp runs along X with Y = 0
        if abs(py) > HILL_RAMP_AXIS_HALF:
            return math.copysign(min(abs(px), 2.5), ox), 0.0, 2.6, 0.2
        return 0.0, 0.0, 2.8, 0.3
    # North/South ramp runs along Y with X = 0
    if abs(px) > HILL_RAMP_AXIS_HALF:
        return 0.0, math.copysign(min(abs(py), 2.5), oy), 2.6, 0.2
    return 0.0, 0.0, 2.8, 0.3


@AL.logged("event_arena_brawl")
def run_arena(onnx: Path, mode: str, seed: int, traits: list[Traits] | None = None, scene: Path | None = None,
              layout_path: Path | None = None, brains: dict | None = None,
              recover: dict | None = None) -> ArenaResult:
    cfg = MODES[mode]
    m = mujoco.MjModel.from_xml_path(str(scene or (C.ROOT / "assets" / cfg["scene"])))
    d = mujoco.MjData(m)
    layout = json.loads(Path(layout_path or (C.ROOT / "assets" / cfg["layout"])).read_text())
    rng = np.random.default_rng(seed)
    traits = traits or [Traits.sample(rng) for _ in range(len(layout["lanes"]))]
    lanes = make_lanes(m, d, layout, seed, traits, onnx, brains)

    support_ids = set()
    for name in cfg["support"]:
        gid = mujoco.mj_name2id(m, mujoco.mjtObj.mjOBJ_GEOM, name)
        if gid >= 0:
            support_ids.add(gid)

    for ln, row in zip(lanes, layout["lanes"]):
        if support_ids:
            ln.support = set(support_ids)
        yaw = math.radians(float(row.get("yaw_deg", 0.0)))
        if abs(yaw) > 1e-6:
            c, s = math.cos(0.5 * yaw), math.sin(0.5 * yaw)
            ra = ln.ath.root_qposadr
            w, x, y, z = d.qpos[ra + 3: ra + 7]
            d.qpos[ra + 3: ra + 7] = [c * w - s * z, c * x - s * y, c * y + s * x, c * z + s * w]
    mujoco.mj_forward(m, d)

    dt = m.opt.timestep * C.DECIMATION
    rec = Recovery(lanes, recover if cfg["recover"] else None, dt)
    res = [ArenaLane(ln.k, ln.traits, body=ln.body) for ln in lanes]
    pad_sets = [round_pads(seed, r) for r in range(len(PAD_COUNTS))] if mode == "pads" else []

    tick = 0
    next_round_idx = 0
    while tick * dt < cfg["max_s"]:
        t = tick * dt
        positions = [(float(d.qpos[ln.ath.root_qposadr]), float(d.qpos[ln.ath.root_qposadr + 1])) for ln in lanes]
        rnd_idx = min(int(t // PAD_ROUND_S), len(PAD_COUNTS) - 1) if mode == "pads" else 0

        for i, ln in enumerate(lanes):
            r = res[i]
            if rec.control(i, d):
                continue
            if r.status:
                ln.control(ln.sess, d, np.zeros(3))
                continue
            ra = ln.ath.root_qposadr
            px, py = positions[i]
            yaw = C.yaw_of(d.qpos[ra + 3: ra + 7])

            if mode == "sumo":
                dist_c = math.hypot(px, py)
                if dist_c > 0.95:
                    cmd = steer_to_point(yaw, px, py, 0.0, 0.0, max_vx=2.6, slow_r=0.2)
                else:
                    # Push towards closest active rival (or charge across centre)
                    best_d, tx, ty = float("inf"), 0.0, 0.0
                    for j, rj in enumerate(res):
                        if j != i and not rj.status:
                            d_ij = math.hypot(positions[j][0] - px, positions[j][1] - py)
                            if d_ij < best_d:
                                best_d, tx, ty = d_ij, positions[j][0], positions[j][1]
                    cmd = steer_to_point(yaw, px, py, tx * 0.7, ty * 0.7, max_vx=2.1, slow_r=0.15)
            elif mode == "bridge":
                goal_x = BRIDGE_LEN if i < 4 else 0.0
                target_y = float(np.clip(ln.origin[1] * 0.65, -0.42, 0.42))
                cmd = steer_to_point(yaw, px, py, goal_x, target_y, max_vx=3.4, slow_r=0.3)
            elif mode == "pads":
                pads = pad_sets[rnd_idx]
                tx, ty = min(pads, key=lambda p: (p[0] - px) ** 2 + (p[1] - py) ** 2)
                cmd = steer_to_point(yaw, px, py, tx, ty, max_vx=3.2, slow_r=1.2, stop_r=0.08)
            elif mode == "beacon":
                # Slight orbiting bias keeps all runners active & bumping inside the central beacon zone
                ang = 0.9 * t + 2.0 * math.pi * i / len(lanes)
                tx, ty = 0.35 * math.cos(ang), 0.35 * math.sin(ang)
                cmd = steer_to_point(yaw, px, py, tx, ty, max_vx=3.5, slow_r=0.6)
            else:  # hill
                tx, ty, max_vx, slow_r = hill_waypoint(i, px, py, float(ln.origin[0]), float(ln.origin[1]))
                cmd = steer_to_point(yaw, px, py, tx, ty, max_vx=max_vx, slow_r=slow_r)

            ln.control(ln.sess, d, cmd)

        for s in range(C.DECIMATION):
            for ln in lanes:
                ln.write_ctrl(d, s)
            mujoco.mj_step(m, d)
        tick += 1
        t = tick * dt

        for i, ln in enumerate(lanes):
            r = res[i]
            if r.status:
                continue
            ra = ln.ath.root_qposadr
            px, py = float(d.qpos[ra]), float(d.qpos[ra + 1])
            if rec.down[i]:
                rec.still_down(i, d)
                continue
            why = ln.eliminated(m, d)
            if mode == "sumo" and (why is not None or math.hypot(px, py) > SUMO_RING_R + 0.08):
                r.status, r.out_at = "OUT", t
                r.progress_m = t
                continue
            if mode == "bridge":
                prog = px if i < 4 else (BRIDGE_LEN - px)
                r.progress_m = max(r.progress_m, prog)
                if why is not None or abs(py) > BRIDGE_HALF_W + 0.12:
                    r.status, r.out_at = "FELL", t
                    continue
                if prog >= BRIDGE_LEN:
                    r.status, r.finish_s = "FINISHED", t
                    continue
            if cfg["recover"] and why == "FELL":
                if rec.fall(i):
                    r.falls += 1
                    continue
                r.status, r.out_at = "FELL", t
                continue
            if mode == "beacon" and math.hypot(px, py) <= BEACON_RADIUS:
                r.score_s += dt
            elif mode == "hill" and abs(px) <= HILL_SUMMIT_HALF and abs(py) <= HILL_SUMMIT_HALF:
                r.score_s += dt

        if mode == "pads" and t >= (next_round_idx + 1) * PAD_ROUND_S - 1e-9:
            pads = pad_sets[next_round_idx]
            active_i = [i for i, r in enumerate(res) if not r.status]
            dists = {}
            for i in active_i:
                ra = lanes[i].ath.root_qposadr
                px, py = float(d.qpos[ra]), float(d.qpos[ra + 1])
                dists[i] = [math.hypot(px - p[0], py - p[1]) for p in pads]
                res[i].progress_m = -min(dists[i])
            safe_set: set[int] = set()
            for pi in range(len(pads)):
                cands = [i for i in active_i if i not in safe_set and not rec.down[i] and dists[i][pi] <= PAD_RADIUS]
                if cands:
                    safe_set.add(min(cands, key=lambda i: (dists[i][pi], i)))
            for i in active_i:
                if i not in safe_set:
                    res[i].status, res[i].out_at = "OUT", t
                else:
                    res[i].rounds += 1
            next_round_idx += 1
            if next_round_idx >= len(PAD_COUNTS) or sum(1 for r in res if not r.status) <= 1:
                break

        if mode == "sumo" and t > 1.0 and sum(1 for r in res if not r.status) <= 1:
            break
        if mode == "bridge" and all(r.status for r in res):
            break

    # Final tie-break & ranking
    for i, ln in enumerate(lanes):
        r = res[i]
        ra = ln.ath.root_qposadr
        px, py = float(d.qpos[ra]), float(d.qpos[ra + 1])
        if mode == "sumo":
            if not r.status:
                r.status = "SURVIVED"
                r.out_at = t + 10.0 - math.hypot(px, py)
        elif mode == "bridge":
            r.status = r.status or "DNF"
        elif mode == "pads":
            if not r.status:
                r.status = "SURVIVED"
                r.out_at = t + 10.0
                pads = pad_sets[-1]
                r.progress_m = -min(math.hypot(px - p[0], py - p[1]) for p in pads)
        else:
            r.status = r.status or "FINISHED"
            r.progress_m = -math.hypot(px, py)

    if mode == "sumo":
        key = lambda r: (0 if r.status == "SURVIVED" else 1, -(r.out_at or 0.0))
    elif mode == "bridge":
        key = lambda r: (0 if r.status == "FINISHED" else 1 if r.status == "DNF" else 2,
                         r.finish_s if r.status == "FINISHED" else -r.progress_m)
    elif mode == "pads":
        key = lambda r: (0 if r.status == "SURVIVED" else 1, -r.rounds, -(r.out_at or 0.0), -r.progress_m)
    else:  # beacon, hill
        key = lambda r: (-round(r.score_s, 4), -r.progress_m)

    for p, r in enumerate(sorted(res, key=key), 1):
        r.place = p
    return ArenaResult(mode, seed, tick * dt, sorted(res, key=lambda r: r.lane))


def to_json(res: ArenaResult) -> dict:
    return {"mode": res.mode, "seed": res.seed, "duration_s": res.duration_s,
            "lanes": [{**asdict(l), "traits": asdict(l.traits)} for l in res.lanes]}
