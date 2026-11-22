"""Every smoothness / effort penalty of config/reward_penalties.json is a reward term of the tasks the shipped brains
train with (rung 0 standing, rung 1 / rung 2 gait), and each one charges a negative reward for the motion it is named
after: joint torque, torso angular velocity, action jitter (rate) and foot sliding. joint_vel_l2 is 0 (no term)."""

from types import SimpleNamespace

import pytest
import torch

from poolympic import penalties as P
from poolympic.tasks import matt_env as M
from poolympic.tasks import mdp

RULE_TERMS = ("torque_l2", "body_ang_vel_l2", "action_rate_l2", "foot_slip")


def test_rule_weights_are_penalties():
    for k in RULE_TERMS:
        assert P.WEIGHTS[k] < 0, f"{k} must carry a negative weight"
    assert P.WEIGHTS["joint_vel_l2"] <= 0


@pytest.mark.parametrize(
    "cfg_fn, terms",
    [
        (
            M.matt_rung0_env_cfg,
            {"torques": "torque_l2", "torso_ang_vel": "body_ang_vel_l2", "action_rate": "action_rate_l2"},
        ),
        (
            M.matt_rung2_env_cfg,
            {
                "torques": "torque_l2",
                "torso_ang_vel": "body_ang_vel_l2",
                "action_rate": "action_rate_l2",
                "foot_slip": "foot_slip",
            },
        ),
    ],
)
def test_task_cfgs_carry_the_penalties(cfg_fn, terms):
    rewards = cfg_fn().rewards
    for name, key in terms.items():
        assert name in rewards, f"{cfg_fn.__name__}: no {name} term"
        assert rewards[name].weight != 0 and (rewards[name].weight < 0) == (P.WEIGHTS[key] < 0)


def test_foot_slip_charges_only_sliding_feet_in_contact():
    v = torch.zeros(2, 2, 3)
    v[0, 0, :2] = torch.tensor([0.3, 0.4])  # env 0: left foot slides 0.5 m/s
    v[1, 1, :2] = torch.tensor([1.0, 0.0])  # env 1: right foot moves 1 m/s but is in the air
    found = torch.tensor([[1, 1], [1, 0]])
    robot = SimpleNamespace(
        find_bodies=lambda names, preserve_order=True: ([0, 1], list(names)),
        data=SimpleNamespace(body_link_lin_vel_w=v),
    )
    env = SimpleNamespace(
        device="cpu",
        scene={"robot": robot, mdp.FOOT_SENSOR: SimpleNamespace(data=SimpleNamespace(found=found))},
    )
    out = mdp.foot_slip(env)
    assert out.tolist() == pytest.approx([0.25, 0.0])
    assert (P.WEIGHTS["foot_slip"] * out).tolist()[0] < 0
