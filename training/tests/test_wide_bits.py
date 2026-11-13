"""16-athlete crowd scenes (compose_mixed.wide_bits): the same contacts as the 8-lane scheme with 1 bit per athlete.

Checked on the compiled models, geom pair by geom pair, with MuJoCo's own rule
(contype_a & conaffinity_b) or (contype_b & conaffinity_a):
  - inside one athlete: exactly the pairs that can touch in its 8-lane crowd scene (leg with leg);
  - between two athletes: every pair;
  - ground and cubes: every athlete geom;
and every athlete of the 16-lane scene is the 8-lane athlete apart from those two bit fields.
"""

from __future__ import annotations

import sys
from pathlib import Path

import mujoco
import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import compose_mixed as C  # noqa: E402
from poolympic.fingerprint import fingerprint  # noqa: E402

BODIES = ["matt", "zombie", "grandma", "kim"]


def touch(m: mujoco.MjModel, a: int, b: int) -> bool:
    return bool((m.geom_contype[a] & m.geom_conaffinity[b]) or (m.geom_contype[b] & m.geom_conaffinity[a]))


def athlete_geoms(m: mujoco.MjModel, prefix: str) -> list[int]:
    return [g for g in range(m.ngeom) if m.geom(g).name.startswith(prefix)]


def lineup16() -> list[str]:
    return [BODIES[k % len(BODIES)] for k in range(16)]


@pytest.fixture(scope="module")
def models():
    wide = mujoco.MjModel.from_xml_string(C.compose("crawl16", lineup16())[0])
    narrow = mujoco.MjModel.from_xml_string(C.compose("crawl8", lineup16()[:8])[0])
    return wide, narrow


def test_sixteen_lanes_fit_the_bit_fields():
    for lane in range(16):
        for bits in ((1, 0), (256, 256)):
            ct, ca = C.wide_bits(*bits, lane)
            assert 0 < ct < 2 ** 31 and 0 <= ca < 2 ** 31   # MuJoCo's contype / conaffinity are signed 32-bit ints


def test_inside_an_athlete_the_same_pairs_touch_as_in_the_8_lane_scene(models):
    wide, narrow = models
    for lane in range(16):
        w = athlete_geoms(wide, f"L{lane}_")
        n = athlete_geoms(narrow, f"L{lane % 4}_")          # the same body (lineup repeats every 4 lanes)
        assert len(w) == len(n) > 0
        for i in range(len(w)):
            for j in range(i + 1, len(w)):
                assert touch(wide, w[i], w[j]) == touch(narrow, n[i], n[j]), (lane, wide.geom(w[i]).name, wide.geom(w[j]).name)


def test_every_part_meets_every_part_of_every_other_athlete(models):
    wide, _ = models
    geoms = [athlete_geoms(wide, f"L{lane}_") for lane in range(16)]
    for a in range(16):
        for b in range(a + 1, 16):
            assert all(touch(wide, x, y) for x in geoms[a] for y in geoms[b]), (a, b)


def test_ground_and_cubes_meet_every_athlete_geom(models):
    wide, _ = models
    world = [g for g in range(wide.ngeom) if wide.geom(g).name == "ground" or wide.geom(g).name.startswith("cube")]
    assert len(world) == 1 + 32                              # 2 cubes per lane
    for lane in range(16):
        assert all(touch(wide, x, w) for x in athlete_geoms(wide, f"L{lane}_") for w in world)


def test_each_athlete_is_the_8_lane_athlete_apart_from_the_collision_bits(models):
    wide, narrow = models

    def strip(fp: dict) -> dict:
        for g in fp["geoms"].values():
            g.pop("contype"), g.pop("conaffinity")
        fp["geoms"]["cube_geom"].pop("body", None)           # wide has cube16..cube31 beyond narrow's cube0..cube15
        fp["bodies"]["pelvis"]["pos"] = None                 # the lane's place on the track
        return fp

    for lane in range(16):
        a = strip(fingerprint(wide, f"L{lane}_", f"cube{2 * lane}"))
        b = strip(fingerprint(narrow, f"L{lane % 4}_", f"cube{2 * (lane % 4)}"))
        assert a == b, f"lane {lane}"


def test_lanes_form_two_rows_of_eight(models):
    wide, narrow = models
    pos = lambda m, k: m.body(f"L{k}_pelvis").pos
    for k in range(8):
        assert np.allclose(pos(wide, k)[:2], pos(narrow, k)[:2])
        assert np.allclose(pos(wide, k + 8)[:2], pos(narrow, k)[:2] + np.array([-2.2, 0.0]))


def test_non_overlapping_at_rest_in_two_rows_of_eight():
    for scene in ("pedestal16", "shaker16", "crawl16", "track16", "inverted16", "crab16", "slalom16", "turntable16"):
        m = mujoco.MjModel.from_xml_string(C.compose(scene, lineup16())[0])
        d = mujoco.MjData(m)
        mujoco.mj_forward(m, d)
        cross_row = []
        for i in range(d.ncon):
            c = d.contact[i]
            n1, n2 = m.geom(c.geom1).name, m.geom(c.geom2).name
            if n1.startswith("L") and n2.startswith("L"):
                l1, l2 = int(n1.split("_")[0][1:]), int(n2.split("_")[0][1:])
                if (l1 < 8) != (l2 < 8):
                    cross_row.append((n1, n2, float(c.dist)))
        assert cross_row == [], f"{scene}: row-1 / row-2 contacts at rest: {cross_row}"
