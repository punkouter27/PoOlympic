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
