"""Physics rules for every athlete body (AGENTS.md 1.2 Physics & Realism), checked on the MJCF the brains train with:
gravity -9.81 m/s^2, every hinge limited and held at its limit, PD position servos (kp, kv) with a torque cap, no
contact between adjacent (parent / child) bodies in any pose, and friction on every body geom. Fast tier (CPU, seconds).
"""

from __future__ import annotations

import mujoco
import numpy as np
import pytest

from poolympic.bodies import BODIES

SCENES = {name: body.scene_xml for name, body in BODIES.items()}


@pytest.fixture(scope="module", params=sorted(SCENES))
def model(request):
    return request.param, mujoco.MjModel.from_xml_path(str(SCENES[request.param]))


def _hinges(m):
    return [j for j in range(m.njnt) if m.jnt_type[j] == mujoco.mjtJoint.mjJNT_HINGE]


def _body_geoms(m):
    return [g for g in range(m.ngeom) if m.geom_bodyid[g] != 0]


def test_gravity_is_earth(model):
    name, m = model
    np.testing.assert_allclose(m.opt.gravity, [0.0, 0.0, -9.81], err_msg=name)
    # and it acts: a body released in the air falls g t^2 / 2 (no ground contact in 0.1 s from the default pose + 1 m)
    d = mujoco.MjData(m)
    d.qpos[2] += 1.0
    z0 = d.qpos[2]
    steps = int(round(0.1 / m.opt.timestep))
    for _ in range(steps):
        mujoco.mj_step(m, d)
    assert z0 - d.qpos[2] == pytest.approx(0.5 * 9.81 * (steps * m.opt.timestep) ** 2, rel=0.05), name


def test_every_hinge_has_an_anatomical_limit(model):
    name, m = model
    hinges = _hinges(m)
    assert len(hinges) >= 20, name
    for j in hinges:
        lo, hi = np.degrees(m.jnt_range[j])
        jn = m.joint(j).name
        assert m.jnt_limited[j], f"{name}: {jn} has no range"
        assert -180.0 < lo < hi < 180.0, f"{name}: {jn} range {lo:.0f}..{hi:.0f} deg"


@pytest.mark.parametrize("side", [0, 1], ids=["lower", "upper"])
def test_limits_hold_when_driven_to_them(model, side):
    """Every actuated joint commanded to its limit (the brains' ONNX graph clips ctrl to the joint range,
    policy_export.py), root pinned, 1 s of gravity and swing: no joint ends more than 3 deg past its range."""
    name, m = model
    d = mujoco.MjData(m)
    root_q = d.qpos[:7].copy()
    for i in range(m.nu):
        d.ctrl[i] = m.jnt_range[int(m.actuator_trnid[i, 0]), side]
    for _ in range(int(round(1.0 / m.opt.timestep))):
        d.qpos[:7] = root_q
        d.qvel[:6] = 0.0
        mujoco.mj_step(m, d)
    worst, joint = 0.0, ""
    for i in range(m.nu):
        j = int(m.actuator_trnid[i, 0])
        q = d.qpos[m.jnt_qposadr[j]]
        past = q - m.jnt_range[j, 1] if side else m.jnt_range[j, 0] - q
        if past > worst:
            worst, joint = past, m.joint(j).name
    assert np.degrees(worst) < 3.0, f"{name}: {joint} ended {np.degrees(worst):.1f} deg past its limit"


def test_actuators_are_pd_servos_with_a_torque_cap(model):
    name, m = model
    assert m.nu > 0, name
    d = mujoco.MjData(m)
    rng = np.random.default_rng(0)
    for i in range(m.nu):
        kp, kv = m.actuator_gainprm[i, 0], -m.actuator_biasprm[i, 2]
        assert m.actuator_trntype[i] == mujoco.mjtTrn.mjTRN_JOINT, name
        assert m.actuator_gaintype[i] == mujoco.mjtGain.mjGAIN_FIXED, name
        assert m.actuator_biastype[i] == mujoco.mjtBias.mjBIAS_AFFINE, name
        assert kp > 0 and kv > 0 and m.actuator_biasprm[i, 1] == pytest.approx(-kp), f"{name}: actuator {i} is not PD"
        assert m.actuator_forcelimited[i] and m.actuator_forcerange[i, 1] > 0, f"{name}: actuator {i} has no torque cap"
    # the force MuJoCo applies is kp (target - q) - kv qdot, clipped to the cap
    d.qpos[7:] += rng.uniform(-0.1, 0.1, m.nq - 7)
    d.qvel[6:] = rng.uniform(-1.0, 1.0, m.nv - 6)
    d.ctrl[:] = rng.uniform(-0.3, 0.3, m.nu)
    mujoco.mj_forward(m, d)
    for i in range(m.nu):
        j = int(m.actuator_trnid[i, 0])
        q, qd = d.qpos[m.jnt_qposadr[j]], d.qvel[m.jnt_dofadr[j]]
        want = m.actuator_gainprm[i, 0] * (d.ctrl[i] - q) + m.actuator_biasprm[i, 2] * qd
        want = np.clip(want, *m.actuator_forcerange[i])
        assert d.actuator_force[i] == pytest.approx(want, rel=1e-6, abs=1e-6), f"{name}: actuator {i}"


def test_adjacent_bodies_never_touch(model):
    """Parent / child capsules overlap at every joint; MuJoCo's parent filter must keep them out of the contact list
    in any pose (300 random poses inside the joint ranges)."""
    name, m = model
    assert not (m.opt.disableflags & mujoco.mjtDisableBit.mjDSBL_FILTERPARENT), f"{name}: parent filter disabled"
    d = mujoco.MjData(m)
    rng = np.random.default_rng(1)
    hinges = _hinges(m)
    pairs = 0
    for _ in range(300):
        mujoco.mj_resetData(m, d)
        for j in hinges:
            d.qpos[m.jnt_qposadr[j]] = rng.uniform(*m.jnt_range[j])
        d.qpos[2] += 2.0  # off the ground
        mujoco.mj_forward(m, d)
        for c in d.contact[: d.ncon]:
            b1, b2 = m.geom_bodyid[c.geom1], m.geom_bodyid[c.geom2]
            if 0 in (b1, b2):
                continue
            pairs += 1
            assert m.body_parentid[b1] != b2 and m.body_parentid[b2] != b1, (
                f"{name}: adjacent bodies {m.body(b1).name} / {m.body(b2).name} in contact"
            )
    # self-collision is on: non-adjacent parts do meet in some of those poses (otherwise the check proves nothing)
    assert pairs > 0, f"{name}: no self-contact at all in 300 random poses"


def test_every_body_geom_has_friction(model):
    name, m = model
    for g in _body_geoms(m):
        if m.geom_contype[g] == 0 and m.geom_conaffinity[g] == 0:
            continue  # visual-only geom
        assert m.geom_friction[g, 0] >= 0.5, f"{name}: {m.geom(g).name} sliding friction {m.geom_friction[g, 0]}"
        assert m.geom_condim[g] >= 3, f"{name}: {m.geom(g).name} condim {m.geom_condim[g]} (no tangential friction)"
    ground = m.geom("ground").id
    assert m.geom_friction[ground, 0] >= 0.5, f"{name}: ground friction"
