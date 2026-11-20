"""2026-11-19 additions: mdp.spin_drift (Event 12 spot-hold term) and mdp.with_spin (spin command cfg copy)."""
import types

import torch

from poolympic.tasks import mdp


def _env(pos, cmd):
    return types.SimpleNamespace(
        scene={"robot": types.SimpleNamespace(data=types.SimpleNamespace(root_link_pos_w=pos))},
        command_manager=types.SimpleNamespace(get_command=lambda n: cmd),
        common_step_counter=0, episode_length_buf=torch.tensor([5, 5, 5]))


def test_spin_drift_measures_distance_from_where_the_spin_began():
    pos = torch.zeros(3, 3)
    cmd = torch.tensor([[0.0, 0.0, 3.0], [0.0, 0.0, 0.5], [0.05, 0.0, -2.5]])   # spin, slow turn, spin
    env = _env(pos, cmd)
    assert torch.equal(mdp.spin_drift(env), torch.zeros(3))
    pos[:, 0] += 0.3
    env.common_step_counter = 1
    assert torch.allclose(mdp.spin_drift(env), torch.tensor([0.3, 0.0, 0.3]))
    cmd[2, 2] = -2.0                                    # a new command re-anchors at the current spot
    env.common_step_counter = 2
    assert torch.allclose(mdp.spin_drift(env), torch.tensor([0.3, 0.0, 0.0]))
    pos[:, 1] += 2.0                                    # capped at 1 m
    env.common_step_counter = 3
    assert torch.allclose(mdp.spin_drift(env), torch.tensor([1.0, 0.0, 1.0]))


def test_with_spin_copies_every_field_and_leaves_the_source_alone():
    src = mdp.AthleteCommandCfg(resampling_time_range=(3.0, 8.0), sprint_fraction=0.45, bands=((0.1, (1, 2), (0, 0), 0.5),),
                                ranges=mdp.AthleteCommandCfg.Ranges(lin_vel_x=(-1.5, 4.0), lin_vel_y=(-1.2, 1.2),
                                                                    ang_vel_z=(-3.0, 3.0), heading=None),
                                entity_name="robot")
    out = mdp.with_spin(src, 0.15)
    assert isinstance(out, mdp.SpinAthleteCommandCfg) and out.spin_fraction == 0.15
    assert out.sprint_fraction == 0.45 and out.bands == src.bands and out.ranges.lin_vel_x == (-1.5, 4.0)
    assert not hasattr(src, "spin_fraction")
