# Extended RL optimization log — 2026-11-11

## Scope and policy impact

This run tunes only the four existing Rung 2 policies. Each character uses its own deployed brain and its own checkpoint;
no policy or body is shared across characters.

| Priority | Body / policy | Deployed source | Contract | Physics fingerprint |
|---|---|---|---|---|
| 1 | Zombie Rung 2 | `zombie_rung2.onnx` (`zombie_r2_v11_it400`) | v3, 84 actor inputs / 23 actions | `5521551798c3942cb235d40a8daab9ef10151ef407fa1815d9c9f9cc80a2b379` |
| 2 | Grandma Rung 2 | `grandma_rung2.onnx` | v3, 84 actor inputs / 23 actions | `de971d498513174843349626535396adf747e55c09794bbabf4005e6b2dcfc38` |
| 3 | KIM Rung 2 | `kim_rung2.onnx` (`k2_v3_it600`) | v3, 84 actor inputs / 23 actions | `b1fa14afabbae823a66389734493e82b668d639f98105ff0f406aa6a59469d07` |
| 4 | MATT Rung 2 | `rung2.onnx` | v3, 84 actor inputs / 23 actions | `bfd287b0ce43147ad430d90787f724d9ef7a25a4cc281730278aa3ed0bb024e8` |

The urgent G1 gaps are Zombie and Grandma Rung 2 (both previously recorded at 8/10). The selected `Rung2-Ramp`
recipes add a success-driven contact shove curriculum, contact-solref domain randomization, and eight privileged critic
inputs. The actor layout, action map, body physics, and deployed interface remain unchanged; the critic-only inputs are
appended with zero weights. MuJoCo is 3.11.0 at 0.005 s / decimation 4.

## Startup and recovery

- Repository sync completed before training: commit `95d5825` is on `master` and pushed to `origin/master`.
- Unity was closed before the run. At preparation, free RAM was 8.07 GB, free GPU memory 5955 MiB, and GPU temperature
  61 C. A temporary high-performance power profile was activated; the original Balanced profile must be restored after
  training. NVIDIA reports fan speed as unavailable, so Cooler Boost could not be verified through its CLI.
- `training/runs/` and its source `.pt` checkpoints were absent. Each of the four deployed actor/critic pairs was
  reconstructed through `tools/onnx_to_checkpoint.py`, then expanded with `tools/expand_obs.py --critic-zeros 8`.
  Reconstructed action error was below `9e-7`; expanded actors matched the source exactly at initialization.
- The default Windows Python console encoding caused `train --help` to fail while printing a Unicode arrow. Training
  and gate processes will use `PYTHONIOENCODING=utf-8` and `PYTHONUTF8=1`.

## Run plan and gates

- Run one GPU job at a time, in priority order: Zombie, Grandma, KIM, MATT. Start with the registered standard
  `PoOlympic-*-Rung2-Ramp` task at 4096 environments and 600 iterations per body; TensorBoard and checkpoints are
  enabled by the task configuration.
- Evaluate G1 Rung 2 on 30 seeds at checkpoints 100, 200, 300, 400, 500, and 599, and run the realism probes. Review
  reward/loss curves and gate outcomes before adding continuation stages; continue only while progress is useful and
  within the eight-hour budget.
- Startup estimate: roughly 2–4 hours for the first four-body sweep and its CPU gates; there is no recent measured
  4096-environment PPO throughput for this setup, so recalibrate from the first run's iteration timings. The eight-hour
  run budget is the target, not a reason to continue a clearly regressing policy.
- Candidate ONNX exports are for evaluation only and remain separate from the deployed manifest. Do not deploy unless a
  selected candidate passes 100 consecutive G1 Rung 2 episodes and its training logs show no NaN gradients. Re-run the
  task and realism regressions before any deployment.

## Results

The detached queue started at **2026-11-11 23:25:19 -05:00**. Its first job is Zombie Rung2-Ramp (`z2ramp_v1`, 600
iterations); the remaining first-pass jobs are Grandma, KIM, then MATT. TensorBoard responded with HTTP 200 at
`http://127.0.0.1:6006/`. At the first resource check, initialization was still underway, GPU load was 0%, GPU temperature
was 50 C, and free RAM was 6.44 GB.

Checkpoint metrics and continuation decisions are recorded below. The final charted report is at
`docs/20261111/extended_training_report.html`.

## Live progress

- The Zombie first pass completed all 600 iterations with final mean reward 19.69 and no NaN/Inf indicators in its
  training log. G1 Rung 2 checkpoints at iterations 100, 200, 300, 400, 500, and 599 each scored 0/30, all 30
  falling. At iteration 599 the walk probe reached 1.32 m/s, but 34% of walk probes fell and run/sprint speeds
  remained below 0.2 m/s. Its saved config used one environment, not the planned 4096, so these are diagnostic results
  only; the corrected 4096-environment result is recorded below.
- Grandma completed all 600 iterations with final mean reward 117.03 and no NaN/Inf indicators in its training log.
  Its iteration-100/200/300/400/500/599 checkpoints scored 10/30, 26/30, 29/30, 29/30, 30/30, and 30/30 G1 Rung 2
  passes, all with zero falls. At iteration 599, walk/run/sprint probes averaged 1.22/2.89/3.57 m/s against targets
  1.2/3.0/3.8 m/s. A separate 100-seed check scored 97/100 with zero falls, short of the required 100 consecutive
  successes; further fine-tuning is warranted.
- KIM completed 600 iterations with final mean reward 109.68 and no NaN/Inf indicators. Its iteration-100/200/300/400/
  500/599 G1 checkpoints scored 18/30, 25/30, 28/30, 28/30, 28/30, and 28/30, all with zero falls. At iteration 599
  walk/run/sprint probes averaged 1.19/2.91/3.58 m/s. This candidate did not reach 30/30, and the final 100-episode
  gate is not yet warranted.
- MATT completed all 600 iterations with final mean reward -2.75 and no NaN/Inf indicators. Every tested G1 Rung 2
  checkpoint scored 0/30 with 30 falls; locomotion probes remained under 0.2 m/s, with substantial clipping at several
  checkpoints. Its saved config also used one environment, not the planned 4096, so these are diagnostic results only;
  the corrected 4096-environment result is recorded below.
- The MuJoCo attach warnings are expected for the character XML's `<option>` values: `mjlab` warns that `MjSpec.attach`
  keeps the parent defaults. The simulator then calls `MujocoCfg.apply()` on the compiled model (including the variant
  path), which explicitly sets the configured 0.005 s timestep, `implicitfast` integrator, and 20 solver iterations.
  No training configuration or deployed physics fingerprint was changed.
- The first queue completed at 01:19:59 on 2026-11-12 and restored the Balanced power plan. Subsequent runs fine-tuned
  Grandma and KIM from their own checkpoints; Zombie and MATT were initially excluded because their first-pass
  1-environment runs showed no G1 improvement. Candidates remain separate from the deployed manifest.
- Grandma continuation `g2ramp_v2` completed 600 iterations with final mean reward 117.36 and no NaN/Inf indicators.
  Its iteration-100/200/300/400/500/599 checkpoints scored 30/30, 30/30, 30/30, 29/30, 29/30, and 29/30 G1 Rung 2,
  all with zero falls. Walk/run/sprint probes stayed near target speeds; reward and gate results plateaued near the
  first pass. Its best 30/30 checkpoint, iteration 300, passed the 100-seed G1 Rung 2 gate (100/100, zero falls) and
  the realism probes; its actor was exported separately with the unchanged v3 interface and physics fingerprint.
  Production promotion was withheld because the ramp-task critic has a different observation size, as detailed below.
- KIM continuation `k2ramp_v2` completed 600 iterations with final mean reward 112.15 and no NaN/Inf indicators.
  Its iteration-100/200/300/400/500/599 checkpoints scored 28/30, 28/30, 28/30, 28/30, 28/30, and 29/30, all with
  zero falls. The iteration-599 candidate scored 89/100 at the graduation gate with zero falls. It is not eligible for
  production; a third continuation will start from its final checkpoint.
- KIM continuation `k2ramp_v3` completed 600 iterations with final mean reward 110.80 and no NaN/Inf indicators.
  Its iteration-100/200/300/400/500/599 G1 Rung 2 checkpoints scored 27/30, 29/30, 27/30, 28/30, 28/30, and
  27/30, all with zero falls. It did not improve on the v2 89/100 graduation result.
- KIM continuation `k2ramp_v4` completed 600 iterations with final mean reward 113.20 and no NaN/Inf indicators.
  Its iteration-100/200/300/400/500/599 G1 Rung 2 checkpoints scored 27/30, 28/30, 28/30, 27/30, 27/30, and 27/30,
  all with zero falls. It started from the strongest v3 checkpoint (`model_200.pt`, 29/30), but the same recipe did not
  improve its gate result; it is not eligible for promotion.
- Saved run configs revealed that Zombie and MATT Rung2-Ramp jobs used only one environment each (`num_envs: 1`),
  despite the planned 4096. Their 0/30 outcomes are under-sampled diagnostics and are not comparable to Grandma/KIM.
  Added separate `PoOlympic-Zombie-Rung2-Ramp-4096` and `PoOlympic-Matt-Rung2-Ramp-4096` task IDs; corrected runs
  started at 05:07 on 2026-11-12. Existing task IDs, physics, contracts, and rewards remain unchanged. Policy impact:
  Zombie Rung 2 and MATT Rung 2 only.
- Corrected Zombie run `z2ramp_v2_4096` completed 600 iterations with final mean reward 131.36 and no NaN/Inf
  indicators. Its iterations 100/200/300/400/500/599 all scored 0/30 G1 Rung 2 (zero G1 falls); its final walk/run/
  sprint probes were 1.17/2.82/3.27 m/s. It is not eligible for promotion. Corrected MATT run
  `m2ramp_v2_4096` started at 05:56 with 4096 environments. At iteration 100 it scored 0/30 G1 (zero falls); its
  walk/run/sprint probes were 1.32/2.81/3.52 m/s.
- At 06:17 on 2026-11-12, corrected MATT was at iteration 273/600 with mean reward 91.89; watched G1 checkpoints
  100 and 200 each scored 0/30 with zero falls. Continue the planned run and checkpoint gates; locomotion probes
  alone do not qualify a policy for promotion.
- MATT's iteration-300 checkpoint scored 8/30 G1 with zero falls. The remaining planned checkpoints are still
  required; this screening result does not qualify the policy for promotion.
- MATT's iteration-400 checkpoint improved to 24/30 G1 with zero falls; continue evaluating later checkpoints and
  retain the 100-episode gate as the promotion requirement.
- MATT's iteration-500 checkpoint scored 26/30 G1 with zero falls. It has not met the 30-seed screening threshold;
  at that stage, final iteration-599 and realism results were pending.
- Corrected MATT completed all 600 iterations with final mean reward 92.01 and no NaN/Inf markers. Its
  100/200/300/400/500/599 checkpoints scored 0/30, 0/30, 8/30, 24/30, 26/30, and 29/30 G1, all with zero falls.
  Final walk/run/sprint probes averaged 1.16/2.99/3.79 m/s. Since no checkpoint reached 30/30, the 100-episode gate
  was not warranted; this candidate was not promoted.

## Promotion follow-up — 2026-11-12

Grandma's `g2ramp_v2` iteration-300 candidate subsequently passed the 100/100 G1 Rung 2 gate with zero falls and passed
the realism probes. It is now deployed as `grandma_rung2.onnx` with the unchanged v3 contract and physics fingerprint.
The old Grandma Rung 2 confidence calibration was removed because it belongs to the previous actor; confidence
telemetry remains unavailable until a compatible calibration is produced. Zombie and MATT candidates remain
unpromoted because they did not meet the graduation gate.
