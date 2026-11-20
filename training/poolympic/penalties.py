"""Smoothness / effort penalty weights shared by every task: training/config/reward_penalties.json.

torque_l2        sum of joint torque squared                (mjlab joint_torques_l2)
body_ang_vel_l2  torso angular velocity squared             (mjlab velocity body_angular_velocity_penalty)
action_rate_l2   |a_t - a_{t-1}|^2                          (mjlab action_rate_l2)
foot_slip        foot planar speed while the foot is in contact (mdp.foot_slip; gait tasks and Rung 0 full)
joint_vel_l2     sum of joint angular velocity squared      (mjlab joint_vel_l2; 0 = term not added)

The Unity player reads the same numbers from Assets/PoOlympic/Resources/RewardPenalties.asset (its episode-reward
readout); the EditMode test RewardPenaltiesTests fails when the two disagree.
"""

from __future__ import annotations

import json
from pathlib import Path

CONFIG = Path(__file__).resolve().parents[1] / "config" / "reward_penalties.json"
KEYS = ("torque_l2", "body_ang_vel_l2", "action_rate_l2", "foot_slip", "joint_vel_l2")


def load(path: Path = CONFIG) -> dict[str, float]:
    raw = json.loads(path.read_text(encoding="utf-8"))
    missing = [k for k in KEYS if k not in raw]
    if missing:
        raise KeyError(f"{path}: missing penalty weight(s) {missing}")
    return {k: float(raw[k]) for k in KEYS}


WEIGHTS = load()
