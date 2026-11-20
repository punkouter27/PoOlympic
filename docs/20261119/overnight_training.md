# Overnight training — 2026-11-19 21:40 → 2026-11-20 09:00

User: "train until 9am as needed". Machine: RTX 5070 Ti Laptop (12 GB), 24 threads; first training on this PC — no
`training/runs`, so every warm start is rebuilt from a deployed / candidate ONNX (`tools/onnx_to_checkpoint.py`, template
= a 1-iteration run of the same architecture).

## Policies touched (AGENTS §3)

| Policy | Why | Change |
|---|---|---|
| MATT Rung 2 (`rung2.onnx`, locomotion) | candidate `m2ramp_ss_v1_it200` (G1 100/100, E19 crowd falls −33 %) was held back by the Event 12 heat | fine-tune only; deployed only if it passes G1, the turntable heat **and** the event heats |
| ZOMBIE Rung 2 (`zombie_rung2.onnx`, locomotion) | 23/30 on the 30-seed G1 (4 sprints, 3 slow creeps); six 2026-11-13 runs fell to 0/30 | fine-tune only, same gating |

No change to `training/assets/*.xml`, `contract.py`, the observation / action layout or any existing task
(`tools/dump_task_cfgs.py --diff`: 2 new IDs, 0 existing changed). Physics version unchanged; interface = contract v3.

## Code added

- `mdp.SpinAthleteCommandCfg` / `mdp.with_spin`: a share of command resamples that spin in place (|wz| 2.0–3.3, |v| ≤ 0.1).
  A subclass, so no existing command cfg changes.
- `symmetry.critic_warmup_runner(n)`: the actor (MLP + std) is frozen for the first n iterations so a rebuilt critic fits
  the returns before PPO moves the policy (the 2026-11-13 zombie runs lost the gait in their first iterations after the
  critic / Adam reset). Verified: action std stays 0.10 over the frozen iterations.
- Tasks `PoOlympic-Matt-Rung2-Ramp-Spin` (SprintSharp + 15 % spins, lr 3e-4, warm-up 50) and
  `PoOlympic-Zombie-Rung2-Wobble-Warm` (deployed Wobble recipe + fast-turn / backward / side-step bands, lr 3e-4, warm-up 50).
- `tools/turntable_gate.py` + `watch_gate.py --gates turntable`: the heat of `test_turntable_heat` on seeds 0–5.

## Baselines on this machine

- pytest 136/136 (14 min).
- Turntable heat (seeds 0–5, DONE of 8): `rung2` (r2_v8) **7,7,8,7,8,7 = 7.33**; `m2ramp_ss_v1_it200` **3,4,5,2,4,3 = 3.50**
  (identical to the 2026-11-13 numbers).
- Throughput: 2.0–2.4 s/it at 4096 envs (RTX 2060: 4.3–4.8). 400 its ≈ 15 min + ~5 min start-up.

## Plan and estimate

1. `m2spin_v1` — MATT warm start = `m2ramp_ss_v1_it200` actor + r2_v8 critic (+8 zero privileged columns), std 0.10;
   400 its; gates G1 Rung 2 (30 seeds) + turntable at 75…399. ~20 min + gates.
2. `z2warm_v1` — ZOMBIE warm start = `zombie_rung2.onnx` + its critic, std 0.10; 400 its; G1 30 seeds. ~20 min + gates.
3. Then by results: a second round on whichever recipe moves, 100-seed G1 on the best checkpoints, event heats
   (`compare_brain_events.py`), crowd heats, pytest. Promote only on G1 100/100, 0 falls, 0 NaN, no event regression.

## Progress

- **22:00** queue started (`m2spin_v1`), keep-awake + TensorBoard (`http://127.0.0.1:6006/`).
- **22:22 — first attempt broken, cause found: the observation normaliser, not the critic.** `m2spin_v1` it 75 (actor
  weights frozen until it 50, max weight change 0.002) scored G1 **0/30, 30 falls**, turntable 0/8; training episode
  length fell to ~50 steps by it 25. The exported brain's `obs_mean` had moved by up to 1.28 and `obs_std` by 0.31: the
  rebuilt checkpoint's normaliser count was 1e6 (`expand_obs.py` default) — ten iterations of 4096-env data — and
  `onnx_to_checkpoint.py` keeps the *template's* count (1,536 for the zombie's 1-iteration template). The statistics
  re-fit to the first rollouts (reset poses), which moved every input of the policy. This is very likely also what
  sank the six 2026-11-13 zombie runs (log blamed the critic / Adam reset). Fix: `onnx_to_checkpoint.py --norm-count`
  (default 1e12 = pinned) and `expand_obs.py --count 1e12`; both warm starts rebuilt, failed run deleted, queue restarted.
- **22:24 — second attempt broken at the unfreeze: the empty Adam state.** With the normaliser pinned, episodes held
  ~1000 steps through it 50 (actor frozen) and fell to 30 within three iterations of the unfreeze; every weight had
  moved ~0.03 (Adam's bias-corrected first steps are ±lr per weight whatever the gradient's signal-to-noise). Fix:
  `critic_warmup_runner` now runs PPO's update as usual for the first 50 iterations (Adam's moments fill for the actor
  too) and puts the actor's weights back after each update.
- **22:25 third attempt healthy.** Episode length 1000 through it 50 → 160; G1 Rung 2 **30/30, 0 falls** at it 75 and
  it 100. Turntable heat 4.3 / 4.0 of 8 (candidate 3.5, r2_v8 7.3): every miss is drift past the 0.75 m ring (DQ);
  r2_v8 drifts 0.2–0.6 m on the same heats, these 0.6–0.76 m. No falls.
- **22:50 next MATT recipe queued: `PoOlympic-Matt-Rung2-Ramp-SpinHold`** = Spin + `mdp.spin_drift` (distance of the
  pelvis from where the current in-place-spin command began, capped 1 m, w −3; re-anchored on a new command / episode;
  checked on a fake env) and 20 % spins. Warm start `m2spin_v1` it 399 via `plain_init.py` (real Adam state).
  (Ops note: a Monitor `tail -F` on `runs/queue.log` locks the file against the queue's `Add-Content`; poll instead.)
- **`m2spin_v1` result:** G1 (30 seeds) 30/30 at it 75–250 and 350, **28/30 at it 300, 29/30 at it 399**; 0 falls at
  every gate. Turntable (6 seeds) 4.3 / 4.0 / 4.2 / 5.2 / 3.8 / 5.0 / 5.5 / 4.5. `m2hold_v1` it 50 / 100 / 150: 4.7 /
  4.5 / 4.0 (G1 30/30 to it 300, 29/30 at 350 and 399). `m2bump_v1` G1 30 / 28 / 27 / 26 at it 50–200 (bumps erode it).
  (Corrected 00:40: earlier lines in this log said "30/30 throughout" — that read only the early gates.)
- **23:10 — diagnosis: the turntable gap is contact, not spinning.** Ablation (6 seeds; `scratchpad/tt_ablate.py`
  patches athlete geoms to collide with the world only):

  | brain | default | nominal traits | no athlete contact | both |
  |---|---|---|---|---|
  | r2_v8 (`rung2`) | 7.33 / 0.415 m | 4.67 / 0.585 | **8.00 / 0.071** | 8.00 / 0.053 |
  | m2spin_v1 it 350 | 5.50 / 0.604 | 4.67 / 0.655 | **8.00 / 0.079** | 8.00 / 0.070 |
  | m2hold_v1 it 100 | 4.50 / 0.617 | 4.00 / 0.620 | **8.00 / 0.090** | 8.00 / 0.074 |

  (DONE of 8 / mean max drift.) Alone, every brain spins 3 turns within 9 cm of its spot; all DQs come from
  neighbours knocking each other out of the 0.75 m ring. 6 seeds is noisy, so 24 seeds: r2_v8 **7.12** (min 6),
  m2ramp_ss_v1 it 200 4.00, m2spin_v1 it 200 4.46, it 350 **5.04**, m2hold_v1 it 100 4.75 (min 2 for all four).
  The SprintSharp line is less robust to (or more violent in) spinning contact. SpinHold's drift term has nothing to act
  on solo → **`PoOlympic-Matt-Rung2-Ramp-SpinBump`** = SpinHold + `add_crowd_bumps` (±300 N forearms / chest), from
  m2spin_v1 it 350, queued after m2hold_v1.
- **`m2hold_v1` (SpinHold) result:** G1 30/30 to it 300, 29/30 at 350 / 399; turntable 4.7 → 3.5–4.0 by it 399: no gain, as the
  diagnosis predicted.
- **23:35 — spin posture probe** (`scratchpad/tt_reach.py`, seeds 0–5): reach radius while spinning (max geom distance
  from the athlete's own vertical axis, contact off) r2_v8 mean 0.221 m / p95 0.508 m; ss_v1 it 200 0.232 / 0.518;
  m2spin_v1 it 350 0.238 / 0.548; m2bump_v1 it 100 0.236 / 0.541. Athlete-athlete contact impulse per heat (contact
  on): r2_v8 **470 N s**, ss_v1 it 200 817, m2spin_v1 it 350 660, m2bump_v1 it 100 768. The turntable spots are
  **0.75 m apart** (`turntable8_layout.json`) with a 0.75 m ring and ~0.5 m spin reach: contact is continuous by design
  (2026-09-30 crowd layout). A solo-trained brain only approximates it; the 2026-10-13 log already concluded "true
  multi-athlete envs, not a third bump recipe". `m2bump_v1` it 50 / 100 / 150: 5.00 / 4.92 / 4.75 (12 seeds), G1 30 / 28 / 27.
- **23:25 `m2bump_v1` stopped by hand** after it 200 = 4.00 / 8 (G1 26/30, 0 falls); no MATT turntable
  training after this. (The `queue.log` note says 23:45 — wrong clock in my note; the stop was at 23:25.) Ops: killing
  the `uv` launcher leaves the `python … train` child running on the GPU — kill trainers by PID (process tree).
- **Deployed zombie baseline on this machine:** G1 Rung 2 **23/30** (same as 2026-11-12).
- **23:25 `z2warm_v1` started** (zombie, pinned normaliser + Adam warm-up runner). Episodes held 970–983 steps
  through the unfreeze (the 2026-11-13 runs: 0/30 by it 50). G1 30 seeds: it 75 **22/30**, it 100 **25/30**, it 150
  10/30, it 200 7/30, 0 falls everywhere. Left at it 100: 4 sprints (2.75–2.95 m/s with a turn) + 1 slow crab. The later
  drop: G1 turntable 360° in 2.36 s vs the 2.358 s bar on every even seed (one tick, one direction), then the `turn`
  segments' linear tracking. Round 2 `PoOlympic-Zombie-Rung2-Wobble-Warm2` (forward band to 3.8 × SS = 2.99 m/s, + 10 %
  slow diagonal side-steps, + 10 % in-place spins at 2.4–3.0 × WS) from **it 100**.
- **00:05 MATT event heats, mixed lineups** (`compare_brain_events.py`, 6 heats per event, MATT lanes only, events 5 9 11
  12 13 19 — event 10 skipped: the tool still maps it to the removed Center Beacon Scramble; `parity/events_matt_candidates_20261119.json`):

  | event | r2_v8 (`rung2`) | m2ramp_ss_v1 it 200 | m2spin_v1 it 350 |
  |---|---|---|---|
  | E05 Gust Gauntlet | place 6.61, **4 ok / 11 stepped / 3 fell** | 6.83, **12 ok / 6 stepped / 0 fell** | 6.83, 10 ok / 8 stepped |
  | E09 Inverted Sprint | 14.93 s | 14.88 s | 15.01 s |
  | E11 Slalom | place **3.44**, 25.63 s | 4.06, 26.90 s | 4.56, 26.90 s |
  | E12 Turntable | 18/18 DONE, mark 7.12 | **18/18 DONE**, 8.17 | 16/18 DONE, 7.91 |
  | E13 Steeplechase | 9.09 (flight brain) | 9.09 | 9.09 |
  | E19 Sprint | 24.24 s | **22.85 s** | 23.22 s |

  In mixed lineups (how the 16-entrant game draws heats) the candidate finishes every turntable; the failing test is
  the all-MATT heat, the worst case for MATT-on-MATT contact. The candidate's real costs: slalom ~1.3 s slower, turntable
  ~1 s slower; gains: gauntlet, sprint, G1 100/100.
- **Learning rate finding:** every run tonight trained at lr **1e-5** (TensorBoard `Loss/learning_rate`), not the
  configured 3e-4. `onnx_to_checkpoint.py` keeps the template's optimiser `param_groups`; the 1-iteration template
  (random policy) had cut its adaptive lr to the 1e-5 floor, rsl_rl restores lr from the optimiser state, and KL then
  sat between desired/2 and 2 × desired (no change). Not TF32: measured TF32-vs-IEEE KL at std 0.1 = 0.0004.
  Correction 00:20: `plain_init.py --lr 1e-4` (new option; `onnx_to_checkpoint.py --lr`, default 3e-4, now resets it too)
  set lr 1e-4 for `z2warm_v3` and the adaptive schedule cut it back to 1e-5 inside the first iteration. So 1e-5 is the
  KL schedule's equilibrium at action std 0.10 with desired KL 0.003 (KL ∝ 1/σ²), not only the inherited value. Fine
  for polishing; a fine-tune that must move further needs std ≥ 0.15 or a larger desired KL.
