"""Phase Z7 — multi-body meets: compose an 8-lane event scene where every lane can hold a different athlete body
(MATT, zombie, …) from the generated per-body robot MJCFs (assets/<body>.xml) and their default keyframes
(assets/scene_<body>.xml). Same scene layout as build_mjcf.compose_meet (lane prefixes L<k>_, lane collision bits +
crowd bits in event scenes, lane origins from the stadium venue, pedestals / shared beam / shaker platforms or floor /
props, cube pool), so an all-MATT lineup compiles to the identical model (checked by `--verify`).

Usage:  uv run python tools/compose_mixed.py pedestal8 matt,zombie,matt,zombie,matt,zombie,matt,zombie
        uv run python tools/compose_mixed.py --verify          (all-MATT pedestal8 == scene_pedestal8.xml)
        uv run python tools/compose_mixed.py track8 roster     (roster scene: MATT L<k>_ + zombie Z<k>_ in every lane)
        uv run python tools/compose_mixed.py --roster-all      (roster scenes for every event scene)
Roster scenes back the Unity menu: every lane holds every roster body of that scene (same lane collision bits, shared pedestal / props /
cubes); Unity's LaneLineup switches off the bodies not picked before MuJoCo compiles, leaving compose(scene, lineup) up to
name prefixes (checked by --verify).
Output: assets/scene_<scene>_<tag>.xml + <scene>_<tag>_layout.json   (tag = lineup, e.g. "mz" for alternating)
"""

from __future__ import annotations

import copy
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import mujoco
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import build_mjcf as B  # noqa: E402  (MATT profile: only the scene helpers / constants are used here)

ASSETS = ROOT / "assets"
ROSTER = ["matt", "zombie", "grandma", "kim"]      # = Unity MeetLineup.Roster (lower case)


def march_props() -> list[dict]:
    """7 Cadence March: the stadium metronome tower at the centre of the 2 x 4 station grid (build_venues.py: a 0.4 x 0.4 x
    5 m steel box; its arm at 5.3 m is out of reach) as a MuJoCo box in the athlete frame of reference lane 3 — 2 m from
    every station row, so only a falling or wandering athlete can meet it, and then it is solid."""
    ev = json.loads(B.VENUES_JSON.read_text())["events"]["07"]
    c = np.mean([l["pos"] for l in ev["lanes"]], axis=0)
    return [{"name": "metronome", "pos": B.venue_to_athlete(7, 3, [c[0], c[1], 2.5]), "size": [0.2, 0.2, 2.5]}]


def _ring_origins(radius: float) -> list[tuple[float, float, float, float]]:
    """8 athletes evenly spaced around a circle of `radius`, each facing inward."""
    out = []
    for k in range(8):
        a = 2.0 * np.pi * k / 8.0
        x, y = radius * float(np.cos(a)), radius * float(np.sin(a))
        yaw = float(np.degrees(np.arctan2(-y, -x)))
        out.append((x, y, 0.0, yaw))
    return out


def _bridge_origins() -> list[tuple[float, float, float, float]]:
    """4 west-side athletes (2x2 staggered) facing +x and 4 east-side athletes facing -x on one 1.60 m wide bridge."""
    return [
        (0.0, 0.42, 0.0, 0.0), (0.0, -0.42, 0.0, 0.0), (-1.4, 0.42, 0.0, 0.0), (-1.4, -0.42, 0.0, 0.0),
        (12.0, -0.42, 0.0, 180.0), (12.0, 0.42, 0.0, 180.0), (13.4, -0.42, 0.0, 180.0), (13.4, 0.42, 0.0, 180.0),
    ]


def _crossroad_origins() -> list[tuple[float, float, float, float]]:
    """4 eastbound runners from x=0 and 4 northbound runners from y=-12 crossing at (12, 0)."""
    offsets = (1.83, 0.61, -0.61, -1.83)
    return [(0.0, y, 0.0, 0.0) for y in offsets] + [(12.0 - x, -12.0, 0.0, 90.0) for x in offsets]


def _funnel_props() -> list[dict]:
    """Two 1.8 m bottleneck wall gates at x = 12 m and x = 24 m."""
    props = []
    for i, gx in enumerate((12.0, 24.0)):
        for j, sy in enumerate((3.9, -3.9)):
            props.append({"name": f"funnel_w{2 * i + j}", "pos": [gx, sy, 0.7], "size": [0.35, 3.0, 0.7]})
    return props


def _hill_origins() -> list[tuple[float, float, float, float]]:
    """2 athletes on each of the 4 cardinal staging pads (r = 5.0 m, ±0.50 m lateral), facing the central summit."""
    out = []
    for k, (nx, ny, yaw) in enumerate(((1.0, 0.0, 180.0), (-1.0, 0.0, 0.0), (0.0, 1.0, -90.0), (0.0, -1.0, 90.0))):
        tx, ty = -ny, nx
        for s in (-0.5, 0.5):
            out.append((5.0 * nx + s * tx, 5.0 * ny + s * ty, 0.0, yaw))
    return out


def _hill_props() -> list[dict]:
    """Central 2.6 x 2.6 m summit (top z = +0.0785 m), 4 x 1.5 deg ramps (3 m long), and 4 flat staging pads (z = 0)."""
    deg = 1.5
    rad = float(np.radians(deg))
    ramp_len, half_t = 3.0, 0.1
    rise = ramp_len * float(np.sin(rad))
    r_inner = 1.3
    r_mid = r_inner + 0.5 * ramp_len * float(np.cos(rad)) - half_t * float(np.sin(rad))
    z_mid = 0.5 * rise - half_t * float(np.cos(rad))
    props = [{"name": "hill_summit", "pos": [0.0, 0.0, rise - half_t], "size": [r_inner, r_inner, half_t]}]
    # Ramp normals point inward: +x side slopes up toward -x (euler="0 +deg 0"); -x side up toward +x ("0 -deg 0"), etc.
    sides = [
        ("e", [r_mid, 0.0, z_mid], [ramp_len / 2, 1.1, half_t], [0.0, deg, 0.0], [5.3, 0.0, -half_t], [1.0, 1.1, half_t]),
        ("w", [-r_mid, 0.0, z_mid], [ramp_len / 2, 1.1, half_t], [0.0, -deg, 0.0], [-5.3, 0.0, -half_t], [1.0, 1.1, half_t]),
        ("n", [0.0, r_mid, z_mid], [1.1, ramp_len / 2, half_t], [-deg, 0.0, 0.0], [0.0, 5.3, -half_t], [1.1, 1.0, half_t]),
        ("s", [0.0, -r_mid, z_mid], [1.1, ramp_len / 2, half_t], [deg, 0.0, 0.0], [0.0, -5.3, -half_t], [1.1, 1.0, half_t]),
    ]
    for tag, rpos, rsize, reul, spos, ssize in sides:
        props.append({"name": f"hill_ramp_{tag}", "pos": rpos, "size": rsize, "euler": reul})
        props.append({"name": f"hill_pad_{tag}", "pos": spos, "size": ssize})
    return props


SCENES = {  # scene -> the build_mjcf.compose_meet arguments of that event scene (callables: read from the venues)
    "meet8": dict(),                                  # G6 testbed (Testbed_Rung1): 1.22 m lanes, no venue, lane isolation
    "pedestal8": dict(event=1, pedestal_h=B.PEDESTAL_H, beam=lambda: B.venue_box(1, "beam"), crowd=True),
    "track8": dict(event=22, park_offset=(0.0, -30.0, 0.0), crowd=True),
    "crawl8": dict(event=8, park_offset=(0.0, -30.0, 0.0), crowd=True),     # GRANDMA crawls too since 2026-10-02 (crawl_grandma.onnx)
    "turntable8": dict(event=12, crowd=True),
    "squat8": dict(event=3, park_offset=(0.0, -30.0, 0.0), crowd=True, roster=[]),   # legacy stance test scene
    "flamingo8": dict(event=6, park_offset=(0.0, -30.0, 0.0), crowd=True, roster=[]),
    "archer8": dict(event=2, park_offset=(0.0, -30.0, 0.0), crowd=True, roster=[]),
    "reach8": dict(event=4, park_offset=(0.0, -30.0, 0.0), crowd=True, roster=[]),
    "march8": dict(event=7, park_offset=(0.0, -30.0, 0.0), props=march_props, crowd=True, roster=[]),
    "crab8": dict(event=10, yaw=90.0, park_offset=(0.0, -30.0, 0.0), props=B.crab_rails, crowd=True),
    "shaker8": dict(event=5, park_offset=(0.0, -30.0, 0.0), shaker=B.shaker_floor, crowd=True),
    "slalom8": dict(event=11, park_offset=(0.0, -30.0, 0.0), props=B.slalom_poles, crowd=True),
    "trench8": dict(event=23, park_offset=(0.0, -30.0, 0.0), props=B.trench_props, crowd=True),
    # 8-athlete high-collision event scenes (2026-11-11)
    "sumo8": dict(origins=lambda: _ring_origins(1.02), drop=0.5, park_offset=(0.0, -30.0, 0.0),
                  props=lambda: [{"name": "sumo_ring", "type": "cylinder", "pos": [0.0, 0.0, -0.25], "size": [1.45, 0.25]}], crowd=True),
    "bridge8": dict(origins=_bridge_origins, drop=0.5, park_offset=(0.0, -30.0, 0.0),
                    props=lambda: [{"name": "bridge", "pos": [6.0, 0.0, -0.25], "size": [8.2, 0.80, 0.25]}], crowd=True),
    "funnel8": dict(event=22, park_offset=(0.0, -30.0, 0.0), props=_funnel_props, crowd=True),
    "crossroad8": dict(origins=_crossroad_origins, park_offset=(0.0, -30.0, 0.0), crowd=True),
    "pads8": dict(origins=lambda: _ring_origins(3.2), park_offset=(0.0, -30.0, 0.0), crowd=True),
    "beacon8": dict(origins=lambda: _ring_origins(4.2), park_offset=(0.0, -30.0, 0.0), crowd=True),
    "hill8": dict(origins=_hill_origins, drop=0.5, park_offset=(0.0, -30.0, 0.0), props=_hill_props, crowd=True),
}


def shift_bits(v: int, k: int) -> int:
    """Lane-0 collision bits -> lane k: upper-body byte (bit 0) and leg byte (bit 8) both move by k."""
    return ((v & 0xFF) << k) | (((v >> 8) & 0xFF) << (k + 8))


def body_parts(body: str):
    """(pelvis element, actuator elements, contact excludes, athlete default qpos) of a generated robot MJCF."""
    robot = ET.parse(ASSETS / f"{body}.xml").getroot()
    pelvis = robot.find("worldbody/body")
    acts = list(robot.find("actuator"))
    excl = list(robot.find("contact")) if robot.find("contact") is not None else []
    scene = mujoco.MjModel.from_xml_path(str(ASSETS / f"scene_{body}.xml"))
    root = scene.joint("root").id
    n = sum(1 for j in range(scene.njnt) if not scene.joint(j).name.startswith("cube"))
    last = [j for j in range(scene.njnt) if not scene.joint(j).name.startswith("cube")][-1]
    nq = scene.jnt_qposadr[last] + 1 - scene.jnt_qposadr[root]
    qdef = scene.key("default").qpos[scene.jnt_qposadr[root]: scene.jnt_qposadr[root] + nq].copy()
    return pelvis, acts, excl, qdef


def roster_of(scene: str) -> list[str]:
    """Bodies every lane of the scene's roster scene holds (the bodies that have a brain for its event)."""
    return list(SCENES[scene].get("roster", ROSTER))


def athlete_prefix(lane: int, body: str, roster: bool) -> str:
    """L<k>_ for MATT and for any body in a one-body-per-lane scene; <B><k>_ (Z<k>_, G<k>_ …) for the other roster
    bodies."""
    return B.lane_prefix(lane) if not roster or body == "matt" else f"{body[0].upper()}{lane}_"


def _quat_mul(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    w1, x1, y1, z1 = a
    w2, x2, y2, z2 = b
    return np.array([
        w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2,
        w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2,
        w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
        w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2,
    ])


def compose(scene: str, lineup: list) -> tuple[str, dict]:
    """lineup: one body per lane, or a list of bodies per lane (roster scene)."""
    sc = SCENES[scene]
    ped_h, crowd = sc.get("pedestal_h", 0.0), sc.get("crowd", False)
    shaker = sc["shaker"]() if callable(sc.get("shaker")) else sc.get("shaker")
    beam = sc["beam"]() if "beam" in sc else None
    props = sc["props"]() if "props" in sc else []
    park_offset = np.asarray(sc.get("park_offset", (0.0, 0.0, 0.0)), float)
    if "origins" in sc:
        raw_origins = sc["origins"]() if callable(sc["origins"]) else sc["origins"]
        origins = [np.asarray(o[:3], float) for o in raw_origins]
        yaws = [float(o[3]) if len(o) > 3 else 0.0 for o in raw_origins]
    elif "event" in sc:
        origins = B.venue_lane_origins(sc["event"], 3, sc.get("yaw", 0.0))
        yaws = [0.0] * len(lineup)
    else:
        origins = [B.lane_origin(k) for k in range(len(lineup))]
        yaws = [0.0] * len(lineup)
    root = ET.Element("mujoco", {"model": f"{scene}_mixed"})
    B.option_block(root)
    wb = ET.SubElement(root, "worldbody")
    ET.SubElement(wb, "light", {"name": "sun", "pos": "0 0 5", "dir": "0 0 -1", "directional": "true"})
    ground = {"name": "ground", "type": "plane", "size": "0 0 0.05", "contype": str(B.ALL_BITS),
              "conaffinity": str(B.ALL_BITS), "condim": "3", "friction": B.vec(B.GROUND_FRICTION)}
    drop = sc.get("drop", 0.0) or ped_h or (shaker["h"] if shaker else 0.0)
    if drop:
        ground["pos"] = B.vec([0, 0, -drop])
    ET.SubElement(wb, "geom", ground)
    for pr in props:
        attrs = {"name": pr["name"], "type": pr.get("type", "box"), "pos": B.vec(pr["pos"]), "size": B.vec(pr["size"]),
                 "contype": str(B.ALL_BITS), "conaffinity": str(B.ALL_BITS), "condim": "3", "friction": B.vec(B.GROUND_FRICTION)}
        if "euler" in pr:
            attrs["euler"] = B.vec(pr["euler"])
        ET.SubElement(wb, "geom", attrs)
    contact = ET.Element("contact")
    actuator = ET.Element("actuator")
    lanes = []
    key_qpos = B.stage_shared(wb, ped_h, shaker, beam)    # = build_mjcf.compose_meet's stage (shared beam / floor)
    for k, bodies in enumerate(lineup):
        roster = not isinstance(bodies, str)          # roster scene: every roster body in this lane
        bodies = list(bodies) if roster else [bodies]
        p, o, yaw = B.lane_prefix(k), np.asarray(origins[k], float), yaws[k]
        key_qpos += B.stage_lane(wb, p, o, ped_h, shaker, beam)
        qz = np.array([np.cos(np.radians(yaw) / 2.0), 0.0, 0.0, np.sin(np.radians(yaw) / 2.0)])
        for body in bodies:
            p = athlete_prefix(k, body, roster)
            pelvis, acts, excl, qdef = body_parts(body)
            pelvis = copy.deepcopy(pelvis)
            for el in pelvis.iter():
                if "name" in el.attrib:
                    el.set("name", p + el.get("name"))
                if el.tag == "geom":
                    ct, ca = shift_bits(int(el.get("contype")), k), shift_bits(int(el.get("conaffinity")), k)
                    if crowd:                         # roster bodies of one lane share its crowd bit: never collide
                        ct, ca = B.crowd_bits(ct, ca, k)
                    el.set("contype", str(ct))
                    el.set("conaffinity", str(ca))
            pelvis.set("pos", B.vec(np.array([float(x) for x in pelvis.get("pos").split()]) + o))
            wb.append(pelvis)
            for e in excl:
                ET.SubElement(contact, "exclude", {"body1": p + e.get("body1"), "body2": p + e.get("body2")})
            for a in acts:
                a = copy.deepcopy(a)
                a.set("name", p + a.get("name"))
                a.set("joint", p + a.get("joint"))
                actuator.append(a)
            q = qdef.copy()
            q[0:3] += o
            if abs(yaw) > 1e-6:
                q[3:7] = _quat_mul(qz, q[3:7])
            key_qpos.append(q)
            row = {"lane": k, "prefix": p, "body": body, "origin": o.tolist(), "cubes": [2 * k, 2 * k + 1]}
            if abs(yaw) > 1e-6:
                row["yaw_deg"] = yaw
            lanes.append(row)
    inertia = B.CUBE_MASS * (2 * B.CUBE_HALF) ** 2 / 6.0
    n_cubes = B.N_CUBES_MEET
    for i in range(n_cubes):
        park = B.cube_park_pos(i) - [0, 0, drop] + park_offset
        cb = ET.SubElement(wb, "body", {"name": f"cube{i}", "pos": B.vec(park)})
        ET.SubElement(cb, "inertial", {"pos": "0 0 0", "mass": B.f(B.CUBE_MASS), "diaginertia": B.vec([inertia] * 3)})
        ET.SubElement(cb, "freejoint", {"name": f"cube{i}_free"})
        ET.SubElement(cb, "geom", {"name": f"cube{i}_geom", "type": "box", "size": B.vec([B.CUBE_HALF] * 3),
                                   "contype": str(B.ALL_BITS), "conaffinity": str(B.ALL_BITS), "condim": "3",
                                   "friction": f"{B.f(B.CUBE_FRICTION)} 0.005 0.0001"})
        key_qpos.append(np.concatenate([park, [1, 0, 0, 0]]))
    root.append(contact)
    root.append(actuator)
    kf = ET.SubElement(root, "keyframe")
    ET.SubElement(kf, "key", {"name": "default", "qpos": B.vec(np.concatenate(key_qpos))})
    layout = {"n_lanes": len(lineup), "lane_width": B.LANE_WIDTH, "n_cubes": n_cubes, "lanes": lanes, "pedestal_h": ped_h,
              "crowd": crowd, "lineup": lineup,
              "note": "lane k: names prefixed L<k>_, athlete body per lane (assets/<body>.xml), pelvis shifted by origin"}
    B.stage_layout(layout, shaker, beam)
    if props:
        layout["props"] = [{"name": pr["name"], "pos": np.asarray(pr["pos"], float).tolist(), "size": list(pr["size"])}
                           for pr in props]
    return B.indent(root), layout


def tag_of(lineup: list) -> str:
    if not isinstance(lineup[0], str):
        return "roster"
    return "".join(b[0] for b in lineup) if len(set(lineup)) > 1 else lineup[0]


def verify() -> int:
    """Every all-MATT composition must compile to the model of the existing scene_<scene>.xml (per-lane fingerprints,
    world props, keyframe, sizes)."""
    from poolympic.fingerprint import canonical_bytes, fingerprint
    ok_all = True
    for scene in SCENES:
        if not (ASSETS / f"scene_{scene}.xml").exists():      # composed-only scene (flamingo8, march8)
            continue
        xml, _ = compose(scene, ["matt"] * 8)
        a = mujoco.MjModel.from_xml_string(xml)
        b = mujoco.MjModel.from_xml_path(str(ASSETS / f"scene_{scene}.xml"))
        same = all(canonical_bytes(fingerprint(a, f"L{k}_", f"cube{2 * k}")) == canonical_bytes(fingerprint(b, f"L{k}_", f"cube{2 * k}"))
                   for k in range(8))
        same &= np.array_equal(a.key("default").qpos, b.key("default").qpos) and (a.nq, a.nu, a.ngeom) == (b.nq, b.nu, b.ngeom)
        world = lambda m: sorted((m.geom(g).name, tuple(np.round(m.geom_pos[g], 9)), tuple(np.round(m.geom_size[g], 9)))
                                 for g in range(m.ngeom) if m.geom_bodyid[g] == 0)
        same &= world(a) == world(b)
        print(f"all-MATT compose == scene_{scene}.xml:", "PASS" if same else "FAIL")
        ok_all &= same
    return 0 if ok_all else 1


def verify_roster() -> int:
    """Every athlete of every roster scene == the same body in that lane of a one-body-per-lane composition
    (per-lane fingerprint incl. its cube slot): switching the other bodies off leaves exactly compose(scene, lineup)."""
    from poolympic.fingerprint import canonical_bytes, fingerprint
    ok_all = True
    for scene in SCENES:
        xml, _ = compose(scene, [roster_of(scene)] * 8)
        r = mujoco.MjModel.from_xml_string(xml)
        for body in roster_of(scene):
            m = mujoco.MjModel.from_xml_string(compose(scene, [[body]] * 8)[0])   # same prefixes, one body per lane
            same = all(canonical_bytes(fingerprint(r, athlete_prefix(k, body, True), f"cube{2 * k}"))
                       == canonical_bytes(fingerprint(m, athlete_prefix(k, body, True), f"cube{2 * k}")) for k in range(8))
            print(f"roster {scene} {body}:", "PASS" if same else "FAIL")
            ok_all &= same
    return 0 if ok_all else 1


def main(argv: list[str]) -> int:
    if argv and argv[0] == "--verify":
        return verify() | verify_roster()
    if argv and argv[0] == "--roster-all":
        return max(main([sc, "roster"]) for sc in SCENES if roster_of(sc))
    scene = argv[0]
    lineup = [roster_of(scene)] * 8 if argv[1] == "roster" else argv[1].split(",")
    assert len(lineup) == 8, "8 lanes"
    xml, layout = compose(scene, lineup)
    tag = tag_of(lineup)
    header = f"<!-- GENERATED by training/tools/compose_mixed.py ({scene}, lineup {tag_of(lineup) if tag_of(lineup) == 'roster' else ','.join(lineup)}). Do not edit. -->\n"
    out = ASSETS / f"scene_{scene}_{tag}.xml"
    out.write_text(header + xml + "\n")
    (ASSETS / f"{scene}_{tag}_layout.json").write_text(json.dumps(layout, indent=1) + "\n")
    m = mujoco.MjModel.from_xml_path(str(out))
    print(f"{out.name}: nq={m.nq} nu={m.nu} ngeom={m.ngeom}  lineup {tag}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
