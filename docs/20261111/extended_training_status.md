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

Checkpoint metrics, continuation decisions, and the final report will be added as the run progresses. The requested
charted report is planned at `docs/20261111/extended_training_report.html`.

## Live progress

- The Zombie first pass completed all 600 iterations with final mean reward 19.69 and no NaN/Inf indicators in its
  training log. G1 Rung 2 checkpoints at iterations 100, 200, 300, 400, 500, and 599 each scored 0/30, all 30
  falling. At iteration 599 the walk probe reached 1.32 m/s, but 34% of walk probes fell and run/sprint speeds
  remained below 0.2 m/s. This candidate is not eligible for promotion; further training will depend on the other
  bodies' results and whether continuation shows useful progress.
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
  checkpoints. It is not a continuation or promotion candidate.
- The MuJoCo attach warnings are expected for the character XML's `<option>` values: `mjlab` warns that `MjSpec.attach`
  keeps the parent defaults. The simulator then calls `MujocoCfg.apply()` on the compiled model (including the variant
  path), which explicitly sets the configured 0.005 s timestep, `implicitfast` integrator, and 20 solver iterations.
  No training configuration or deployed physics fingerprint was changed.
- The first queue completed at 01:19:59 on 2026-11-12 and restored the Balanced power plan. The next pass will fine-tune
  Grandma and KIM from their iteration-599 checkpoints using `plain_init.py`; Zombie and MATT are excluded because they
  showed no G1 improvement. Candidates remain separate from the deployed manifest.
