# Extended 8-Hour Training Log — 2026-11-12 / 2026-11-13

**Window:** `2026-11-12 21:53` – `2026-11-13 ~05:50` (~8 hours sequential GPU queue on RTX 2060 6 GiB)  
**Branch:** `master` (`8500aec`)  
**TensorBoard:** `http://127.0.0.1:6006/` (`training/runs`)

---

## 1. Pre-Flight Hardware & Baseline Audit

- **Hardware / Memory:** Closed orphaned Unity batch test (`PID 22984`, `Event_30mAllFours`, 608.6 s CPU, `crowd_spot.xml` absent). Free RAM recovered from `2.42–3.53 GB` to `7.74 GB` (> 4 GB `AGENTS.md` limit). GPU RTX 2060 `53 °C` idle, `4912 MiB` free VRAM.
- **30-Seed G1 Rung 2 Baselines (`tools/eval_cpu.py --rung 2 --seeds 30`):**

| Athlete | Deployed Brain | 30-Seed G1 | Primary Failure Mode | Best Existing Checkpoint |
|---|---|---:|---|---|
| **Grandma** | `grandma_rung2.onnx` | **100/100 PASS** | Graduated (`5256d84`) | Deployed |
| **Zombie** | `zombie_rung2.onnx` (`z2_v11`) | **23/30 FAIL** | `tracking_lin: 7`, `tracking_yaw: 1` (slow creep `0.19–0.30 m/s` & fast sprint `2.75–2.95 m/s`) | Deployed (`zombie_rung2.onnx`); `z2ramp_v2_4096` scored `0/30` due to `entropy_coef=0.005` std blowup |
| **Matt** | `rung2.onnx` | **29/30 FAIL** | `tracking_yaw: 1` (deployed); candidate `m2ramp_v2_4096` = 29/30 (`seed 1009` backward sprint) | `matt_rung2/2026-11-12_05-57-02_m2ramp_v2_4096/model_599.pt` |
| **Kim** | `kim_rung2.onnx` | **29/30 FAIL** | `tracking_lin: 1` on `seed 1021` (`3.45 m/s` sprint); `k2ramp_v2` = 89/100 (11 fast sprints) | `kim_rung2/2026-11-12_02-16-41_k2ramp_v2/model_599.pt` |

- **10-Seed Get-up Baselines (`tools/getup_probe.py --seeds 10`):**
  - `getup_grandma.onnx`: `10/10` supine (mean `0.75 s`), `10/10` prone (mean `0.57 s`), `0` fell after up.
  - `getup_zombie.onnx`: `10/10` supine (mean `0.69 s`), `10/10` prone (mean `0.30 s`), `0` fell after up.

---

## 2. Policies Touched & Invariant Scope (`AGENTS.md` §3)

Zero changes to existing MJCF assets (`training/assets/*.xml`), `contract.py` observation/action dimensions, or any existing registered task configuration hashes (`tools/dump_task_cfgs.py --diff`). Added 4 opt-in continuation tasks in `training/poolympic/tasks/__init__.py`:

| Task ID | Body | Touches Policy | Key Design Change |
|---|---|---|---|
| `PoOlympic-Matt-Rung2-Ramp-Polish` | `matt` | `rung2` (Matt locomotion) | 4096 envs, `entropy_coef=0.001`, `desired_kl=0.004`, `save_interval=50` |
| `PoOlympic-Kim-Rung2-Ramp-Bands3` | `kim` | `kim_rung2` (Kim locomotion) | 4096 envs, `Bands3` (30% 3.0–4.0×SS sprints) + `add_ramp_recipe`, `entropy_coef=0.001`, `desired_kl=0.004` |
| `PoOlympic-Zombie-Rung2-WobbleSprint-4096` | `zombie` | `zombie_rung2` (Zombie locomotion) | 4096 envs, `50%` 2.5–4.0×SS sprint band, `entropy_coef=0.001`, `desired_kl=0.004` |
| `PoOlympic-Zombie-Rung2-Ramp-LowEnt` | `zombie` | `zombie_rung2` (Zombie locomotion) | 4096 envs, `zombie_rung2_ramp_env_cfg` with `entropy_coef=0.001`, `desired_kl=0.004` |

*(Plus existing `PoOlympic-Zombie-Rung2-Omni-Wobble-Polish`: 4096 envs, 84-obs critic, `entropy_coef=0.001`, `desired_kl=0.004`.)*

---

## 3. Startup Training Time Estimate & Plan

- **Throughput estimate:** `~4.3–4.8 s/it` at 4096 envs (`~28–32 min` per 400-it stage, `~36–40 min` per 500-it stage + CPU gates).
- **Wave 1 (~2.0 h):**
  1. `zombie` | `PoOlympic-Zombie-Rung2-Omni-Wobble-Polish` (`z2_polish_v1`, from `zombie_rung2.onnx` @ `std=0.12`, `400` its, gate every `50` its + `--bio`)
  2. `matt` | `PoOlympic-Matt-Rung2-Ramp-Polish` (`m2ramp_polish_v1`, from `m2ramp_v2_4096/model_599.pt` @ `max_std=0.18`, `500` its, gate every `50` its + `--bio`)
  3. `kim` | `PoOlympic-Kim-Rung2-Ramp-Bands3` (`k2ramp_b3_v1`, from `k2ramp_v2/model_599.pt` @ `max_std=0.18`, `500` its, gate every `50` its + `--bio`)
- **Waves 2–4 (~6.0 h):** Adapt based on Wave 1 convergence (100-seed G1 validation, `Zombie-Rung2-WobbleSprint-4096` or `Zombie-Rung2-Ramp-LowEnt`, further polish / Event heat checks / Getup / Rung0-Full), promoting only brains passing **100/100 G1** with zero falls, zero NaNs, and improved/passing regression heats.

---

## 4. Progress Updates
- **[21:53 EST] Startup:** Pre-flight checks completed; task definitions added; verifying task diff + pytest before launching Wave 1.

### [22:36 EST] Wave 1 Checkpoint & Wave 2 Adaptation
- **`z2_polish_v1` completed (`399/400`, 32.6 min, 0 NaNs):**
  - `Train/mean_reward`: `1.86 -> 150.23` (`max 157.88`); `ep_len`: `13.3 -> 973.7` (`max 995.0`); `Loss/surrogate`: `0.322 -> 0.075` (initial critic-reset spike `4.678`); `Loss/symmetry`: `0.104 -> 0.025`; `Policy/mean_std`: `0.123 -> 0.132`.
  - G1 Rung 2 stayed `0/30` due to initial `onnx_to_checkpoint` critic shock (`it50` fell to `0/30` with `10` falls), then monotonically recovered across `it50 -> it399`: falls `10 -> 0`, turn fails `35/45 -> 10/45`, sprint fails `32/43 -> 21/43`, stop fails `19/19 -> 0/19`, crab fails `43/43 -> 26/43`, turntable drift `0.38m -> 0.24–0.27m`.
  - Built Stage-2 warm start `zombie_rung2/z2_polish_v2_init` (`plain_init.py --max-std 0.14` from `z2_polish_v1/model_399.pt`, preserving the converged Zombie critic & obs normalizer) and queued **`z2_polish_v2` (`600` its)** right after `k2ramp_b3_v1`.
- **`m2ramp_polish_v1` active (`step 70/500`, GPU `61% / 78°C`, `5.65 GB` free RAM):**
  - `Curriculum/shove_ramp/gust_mps` reached max `1.414` cleanly (`ep_len = 991.4–1000.0`, `mean_reward = 99.47`, `Loss/surrogate = 0.026`, `Policy/mean_std = 0.178`, `0` NaNs).
  - **`it 50` G1 Rung 2:** **`29/30`** (`0` falls, `0` yaw/stop/turn/crab/turntable/back fails; only `1` boundary segment `[3.75, 0.0, -0.39]` at `0.20` m/s RMS).

### [23:18 EST] Hourly Status Update #1
- **`m2ramp_polish_v1` completed (`499/500`, 36.6 min, 0 NaNs):**
  - `Train/mean_reward`: `1.12 -> 103.14` (`max 105.32`); `ep_len = 1000.00 / 1000.00`; `gust_mps = 1.414`; `Loss/surrogate`: `0.072 -> 0.025`; `Policy/mean_std`: `0.179 -> 0.178`.
  - **G1 Rung 2 (30 seeds):** Hit **`30/30`** (`0` falls, `clip = 0.0`, sprint `3.77–3.78 m/s`) at **`it 150`**, **`it 250`**, **`it 300`**, and **`it 350`** (`it 50/100/200/400/450 = 29/30`, `it 499 = 28/30`).
  - Launched 100-seed G1 Rung 2 graduation sweep (`eval_cpu.py --seeds 100`) across `it 300, 350, 250, 150`.
- **`k2ramp_b3_v1` active (`step 147/500`, GPU `63% / 76°C`, `4.81 GB` free RAM):**
  - `Train/mean_reward`: `1.26 -> 109.77` (`max 112.03`); `ep_len = 993.82`; `gust_mps = 1.299`; `Loss/surrogate = 0.087`; `Policy/mean_std = 0.151`; `0` NaNs.
  - **`it 50 / 100` G1 Rung 2:** `28/30` (`0` falls; `0` turn/stop/crab/turntable/yaw fails; `2` fast-sprint misses at `3.45 m/s` while `sprint_3.8` climbed to `3.63 m/s`).
- **`z2_polish_v2` queued next** behind `k2ramp_b3_v1` (`600` its from `z2_polish_v1/model_399.pt`).

### [00:32 EST] Hourly Status Update #2
- **Matt (`m2ramp_polish_v1`) 100-seed G1 Rung 2 Audit:**
  - `it350`: **`99/100`** (`0` falls, `turn 0/124`, `stop 0/78`, `crab 0/133`, turntable `2.56–2.62 s` drift `0.06 m`; single miss: `sprint [3.78, 0.0, -0.41]` at `0.22` m/s vs `0.200` threshold).
  - `it150 / it250 / it300`: all `98/100` (`0` falls, `2` misses).
  - Queued **`m2ramp_sharp_v1`** (`PoOlympic-Matt-Rung2-Ramp-Sharp`, `300` its from `model_350.pt` at `--max-std 0.12`, `entropy_coef=0.0002`, `desired_kl=0.003`, `save_interval=25`).
- **Kim (`k2ramp_b3_v1`) completed (`499/500`, 42.4 min, 0 NaNs):**
  - Held **`28/30`** across `it50..it499` (`0` falls, turntable drift `0.11–0.13 m`, `0` turn/stop/crab/yaw fails; `2` fast sprints at `3.45 m/s`). Since `k2ramp_v2/model_599.pt` already passes `30/30` (`89/100`), queued **`k2ramp_sharp_v1`** (`PoOlympic-Kim-Rung2-Ramp-Sharp`, `300` its from `k2ramp_v2/model_599.pt` at `--max-std 0.11`, `entropy_coef=0.0002`, `desired_kl=0.003`).
- **Zombie (`z2_polish_v2`) active (`step 523/600`, GPU `60% / 79°C`, `4.90 GB` free RAM):**
  - `Train/mean_reward`: `1.84 -> 159.43` (`max 167.56`); `ep_len = 980.3`; `Loss/surrogate = 0.083`; `Loss/symmetry = 0.027`; `Policy/mean_std = 0.140`; `0` NaNs.
  - At `it500`: `brake` **`0/30` fails** (down from `30/30`), `tracking_yaw` **`0/30` fails** (down from `18/30`), `turntable` cut in half (`30/30 -> 15/30`, drift `0.136–0.175 m`), `turn` `3/45`, `crab` `8/43`, `sprint` `17/43` (`max 0.54`), `backward` `18.59–19.24 m` (need `20.0 m`). Queued **`z2_polish_sharp_v1`** (`300` its from `z2_polish_v2/model_599.pt` at `--max-std 0.10`, `entropy_coef=0.0002`).


### Hourly Progress Update — `2026-11-13 01:30 EST` (Wave 2 Done, Wave 3 Queued)
- **Zombie (`z2_polish_v2`) completed (`599/600`, 48.4 min, `0` NaNs):**
  - `Train/mean_reward`: `1.839 -> 163.863` (`max 167.562`); `ep_len`: `13.27 -> 981.25` (`max 997.58`); `Loss/surrogate`: `0.075`; `Policy/mean_std`: `0.142`.
  - `it599` G1 breakdown: `brake` `0/30` fails, `tracking_yaw` `0/30` fails, `turntable` `0/30` fails (`2.18–2.34 s`, drift `0.15 m`), `stop` `0/19`, `turn` `1/45`, `crab` `5/43`, `sprint` `15/43` (`max 0.50`), `backward` `30/30` fails. Root cause: `zombie_rung2_polish_env_cfg` excludes backward commands from `bands` (`20%` forward + `20%` crab only), so `backward` regressed during the initial critic warmup in `z2_polish_v1`.
- **Matt (`m2ramp_sharp_v1`) completed (`299/300`, 22.5 min, `0` NaNs):**
  - `Train/mean_reward`: `1.208 -> 106.375` (`max 109.371`); `ep_len = 1000.0`; `Loss/surrogate = 0.044`; `Policy/mean_std = 0.123`.
  - `30-seed` G1 hit **`30/30`** at `it100` & `it225` (`0` falls, `clip 0.0`), and **`100-seed` G1** scored `96/100` (`it100`) and **`97/100`** (`it225`: `turn 0/124`, `stop 0/78`, `crab 0/133`, `turntable 2.50–2.52 s` drift `0.06 m`, `sprint 3/165` hairline misses at `0.20–0.22 m/s`). **`m2ramp_polish_v1_it350` (`99/100`)** stays the leading Matt candidate so far.
- **Kim (`k2ramp_sharp_v1`) completed (`299/300`, 23.8 min, `0` NaNs):**
  - `Train/mean_reward`: `1.276 -> 110.052` (`max 114.496`); `ep_len = 994.46`; `Loss/surrogate = 0.080`; `Policy/mean_std = 0.101`.
  - Held `28/30` (`0` falls, `walk 1.19, run 2.94, sprint 3.63 m/s`), still missing top sprint-turn `[3.45, 0.0, -0.42]` (`0.29 m/s` vs `0.184` bar).
- **Get-Up Baselines Audited (`tools/getup_probe.py --seeds 10`):**
  - `getup_matt.onnx`: `10/10` supine (`0.65 s`), `10/10` prone (`0.89 s`), `0` falls after up.
  - `getup_kim.onnx`: `10/10` supine (`0.77 s`), `10/10` prone (`1.03 s`), `0` falls after up. (All 4 get-up policies `10/10` on both sides.)
- **Active & Wave 3 Queue (`check_bodies.ps1` clean across all 5 bodies; `4.19 GB` free RAM, GPU `82% / 79°C`):**
  - Active now (`01:24 → ~01:46 EST`): `z2_polish_sharp_v1` (step `51/300`, `mean_reward 167.935`, `ep_len 993.37`, `0` NaNs).
  - Queued Wave 3 (`queue.txt`):
    1. `z2_allbands_v1` (`PoOlympic-Zombie-Rung2-Wobble-AllBands`, `400` its from `z2_polish_sharp_v1/model_299.pt --max-std 0.14`, adds `20%` backward band `-1.8..-0.7*SS`, `25%` sprint up to `3.8*SS`, `15%` crab, `track_lin=3.5`).
    2. `m2ramp_ss_v1` (`PoOlympic-Matt-Rung2-Ramp-SprintSharp`, `300` its from `99/100` `m2ramp_polish_v1/model_350.pt --max-std 0.11`, `sprint_fraction=0.45`, `track_lin=3.0`, `track_ang=2.5`).
    3. `k2ramp_lin4_v1` (`PoOlympic-Kim-Rung2-Ramp-Bands3-Lin4`, `400` its from `k2ramp_sharp_v1/model_299.pt --max-std 0.12`, `track_lin=3.5`, `track_lin_coarse=1.5`).


### Hourly Progress Update — `2026-11-13 02:22 EST` (Zombie Root-Cause Isolation, `m2ramp_ss_v1` Active)
- **Zombie (`z2_polish_sharp_v1` 299/300 & `z2_allbands_v1` 399/400, `0` NaNs):**
  - `z2_polish_sharp_v1`: `Train/mean_reward` `1.883 -> 166.560` (`max 172.777`), `ep_len 987.21`, `Loss/surrogate 0.073`, `Policy/mean_std 0.098`; `0/30` (`tracking_lin: 27, turntable: 15, backward: 30`).
  - `z2_allbands_v1`: `Train/mean_reward` `1.900 -> 168.337` (`max 179.757`, step 377), `ep_len 964.35`, `Loss/surrogate 0.073`, `0` falls; `backward` recovered slightly (`18.59 -> 19.08 m` mean, `max 19.30 m`), `it350` at `0/30` (`tracking_lin: 26, turntable: 15, backward: 30`).
  - **Root cause found (`onnx_to_checkpoint` + `lr=1e-3` Adam spike):** `zombie_rung2.onnx` (`23/30`) already passes `backward` (`20.01 m`), `crab` (`0/43`), `turn` (`0/45`), `stop` (`0/19`), and `turntable` (`0/30`), failing only 4 sprints and 3 slow creeps. Rebuilding with `onnx_to_checkpoint.py` resets `optimizer_state_dict["state"] = {}`; starting PPO at `lr = 1.0e-3` caused a `4.678` step-0 surrogate shock in `z2_polish_v1` that displaced the baseline gait.
  - **Fix queued (`z2_gentle_v1`, `PoOlympic-Zombie-Rung2-Wobble-Gentle`):** Added opt-in task with `learning_rate = 1.0e-4`, `clip_param = 0.08`, `desired_kl = 0.0015`, `entropy_coef = 0.0002`, `AllBands` (`25%` sprint up to `3.8*SS`, `20%` backward, `15%` crab), and `track_lin = 3.5`. Rebuilt `runs/zombie_rung2/z2_gentle_v1_init/model_0.pt` at `--std 0.08` (`ctrl vs brain 5.36e-07`).
- **Matt (`m2ramp_ss_v1`) active now (`02:21 → ~02:45 EST`):**
  - `PoOlympic-Matt-Rung2-Ramp-SprintSharp` (`300` its from `m2ramp_polish_v1/model_350.pt` [`99/100`] at `--max-std 0.11`, `sprint_fraction = 0.45`, `track_lin = 3.0`, `track_ang = 2.5`), followed by `k2ramp_lin4_v1` (`~02:45 → 03:20`) and `z2_gentle_v1` (`~03:20 → 03:52`).
- **System health:** GPU `59% / 79°C`, `4491 / 6144 MiB` VRAM, `4.61 GB` free RAM; `check_bodies.ps1` clean across all 5 bodies (`unknown superseded ids []`).


### Hourly Progress Update — `2026-11-13 03:20 EST` (Matt `30/30` x9 Checkpoints, Kim `29/30` Breakthrough, Wave 4 Queued)
- **Zombie (`z2_allbands_v1`) completed (`399/400`, 32.3 min, `0` NaNs):**
  - `Train/mean_reward` `1.900 -> 174.931` (`max 179.820`), `ep_len 990.51`, `Loss/surrogate 0.074`, `Policy/mean_std 0.100`; stayed `0/30` (`backward 19.08 m`, `tracking_lin: 26`).
- **Matt (`m2ramp_ss_v1`, `PoOlympic-Matt-Rung2-Ramp-SprintSharp`) completed (`299/300`, 22.5 min, `0` NaNs):**
  - `Train/mean_reward` `1.300 -> 121.851` (`max 125.466`), `ep_len 998.95`, `Loss/surrogate 0.043`, `Policy/mean_std 0.114`.
  - **`30/30` on 9 consecutive checkpoints (`it50` through `it275`)** with `0` falls and **`0.0` clipping across all 4 bio probes** on `it50, it100, it150, it175, it200` (`it150`: walk `1.19`, run `3.00`, sprint `3.81 m/s`, `clip 0.0`). Launched **100-seed G1 graduation check** on `it150` and `it200` (`task-580`).
- **Kim (`k2ramp_lin4_v1`, `PoOlympic-Kim-Rung2-Ramp-Bands3-Lin4`) completed (`399/400`, 31.1 min, `0` NaNs):**
  - `Train/mean_reward` `1.381 -> 133.904` (`max 137.649`), `ep_len 987.79`, `Loss/surrogate 0.085`, `Policy/mean_std 0.101`.
  - **Broke `28/30` plateau -> `29/30` (`it150..it399`, 6 straight checkpoints)** with `0` falls, `turn 0/45`, `stop 0/19`, `crab 0/43`, `turntable 2.34–2.42 s` (drift `0.13–0.14 m`), `sprint 3.71 m/s` (`clip 0.0`). Single remaining segment `[3.45, 0.0, -0.42]` shrank `0.29 -> 0.22 m/s` (bar `0.184`).
- **Active & Wave 4 Queue (`4.25 GB` free RAM, GPU `59% / 78°C`, `check_bodies.ps1` clean across all 5 bodies):**
  - Active now (`03:15 → ~03:48 EST`): `z2_gentle_v1` (`PoOlympic-Zombie-Rung2-Wobble-Gentle`, step `57/400`, `Loss/surrogate = 0.121` — shock avoided vs `4.678`).
  - Queued right next: `k2ramp_ss_v1` (`PoOlympic-Kim-Rung2-Ramp-SprintSharp`, `300` its from `k2ramp_lin4_v1/model_399.pt --max-std 0.10`, `35%` top-sprint band `(3.2..4.0)*SS`, `track_lin = 5.0`, `track_lin_coarse = 2.0`, `~03:48 → 04:12 EST`).


### Hourly Progress Update — `2026-11-13 04:15 EST` (Matt `100/100` Qualified + Crowd `-33%` Falls; Kim Hits `30/30`)
- **Matt (`m2ramp_ss_v1`) 100-Seed Graduation & Crowd Regression (`task-580`, `task-637`, `task-689`):**
  - **100-seed G1 Rung 2 (`eval_cpu --rung 2 --seeds 100`):** `it150` = **`100/100` PASS** (`0` falls), `it175` = `99/100`, **`it200` = `100/100` PASS** (`0` falls, `turn 0/124`, `sprint 0/165`, `stop 0/78`, `crab 0/133`, turntable `2.62–2.64 s` drift `0.09 m`), `it225` = **`100/100` PASS** (`0` falls), `it250` = `99/100` (`tracking_yaw: 1`, `0` falls).
  - **Biomechanics (`bio_probe.py` on `m2ramp_ss_v1_it200.onnx`):** walk `1.19 m/s` (`293 W`, CoT `0.31`), run `3.00 m/s` (`857 W`, CoT `0.36`), sprint `3.81 m/s` (`1110 W`, CoT `0.37`), spin `0.01`, **`0.0%` Hill / sat / >18 rad/s / arm-clip across all 5 probes**, `0` falls (`mattbio` caps only `ankle_dorsi 1.5–2.0%`, `ankle_inv 0.3%`).
  - **Crowd regression (`crowd_heats.py`, 4 heats/case):** E19 `all` drops from **`2.25` falls/heat** (`9` falls, `10.9 pair-s`) baseline to **`1.50` falls/heat** (`6` falls, `-33%`, `5.9 pair-s`, `8.00/8` finish, `0.0` clips) on `it200` (vs `it150` `2.50`, `it225` `1.75`). E11 `mzgkmzgk` holds `0.00` falls/heat (`8.00/8` finish), E19 `mzgkmzgk` `0.50` vs `0.25` baseline (`8.00/8`). **`m2ramp_ss_v1_it200.onnx` is the selected Matt Rung 2 promotion candidate.**
- **Kim (`k2ramp_ss_v1`, `PoOlympic-Kim-Rung2-Ramp-SprintSharp`) active (`step 178/300`, started `03:58 EST`, `0` NaNs):**
  - `Train/mean_reward` `1.472 -> 153.465` (`max 160.302`), `ep_len = 988.400`, `Loss/value = 0.492`, `Loss/surrogate = 0.081`, `Policy/mean_std = 0.093`.
  - **30-seed G1 Rung 2 breakthrough:** `it50 = 29/30`, **`it100 = 30/30` (`0` falls)**, **`it125 = 30/30` (`0` falls)**, **`it150 = 30/30` (`0` falls)** (`walk 1.23`, `run 2.99`, `sprint 3.71 m/s`, `clip 0.0`). Launched **100-seed G1 + `bio_probe` verification** (`task-713`) on `it100, it125, it150`.
- **Zombie (`z2_gentle_v1`) completed (`399/400`, `42.9 min`, `0` NaNs):**
  - `Train/mean_reward` `1.896 -> 92.357` (`max 96.907`), `ep_len = 740.090` (`max 768.090`), `Loss/value = 1.492 (max 5.001)`, `Loss/surrogate = 0.108`; stayed `0/30` (`30` falls `it50..350`, `4` falls `it399`) due to critic value mismatch against `AllBands` (`track_lin = 3.5`) at `lr = 1e-4`. **`z2_explore_v1`** (`z2_allbands_v1/model_399.pt --max-std 0.20`, `PoOlympic-Zombie-Rung2-Wobble-Explore`) is queued next (~`04:22 → 04:55 EST`).
- **System health:** Free RAM `4.38 GB`; `keep_awake`, `train_queue`, and TensorBoard active.

### Final wrap-up & post-promotion gate (`08:30–09:15 EST`)

- **Queue completion (`05:19:24 EST`):** All 13 sequential runs finished cleanly (`5,200` iterations, `511,180,800` transitions, **`0` NaNs across all logs**).
- **Kim Rung 2 (`k2ramp_ss_v1_it225.onnx`) promoted to production (`kim_rung2.onnx`):**
  - Final `k2ramp_ss_v1` (`300` iters): reward `1.472 -> 154.230 (max 160.302)`, `ep_len = 993.5`, `30/30` on 30-seed G1 (`it225` and `it299`). Initially promoted `it299`, then switched to `it225` after `tests/test_events.py::test_fallen_runner_with_a_getup_brain_gets_up_and_finishes` flagged a post-fall hand-over DNF on `it299` (lane `7`); `it225` finished both knockdown lanes cleanly (`12.1 s`, `13.68 s`, faster than `k2_v3_it600`).
  - `it225` post-promotion checks: **`100/100` G1 (`0` falls)** (`g1_rung2_kim_postpromotion.json`), clean `bio_probe` (`0.0% >18 rad/s`, `0` falls), `8/8 DONE` Turntable (`E12`), `7/8 ok` Gust Gauntlet (`E05`, vs `6/8`), faster `E09` (`15.93 vs 16.80 s`), `E11` (`28.06 vs 28.95 s`), and `E19` (`24.68 vs 25.16 s`).
  - Documented in `DOCS/20261112/kim-rung2-brain-promotion.md`; removed stale `kim_rung2` entry from `Assets/PoOlympic/Models/confidence_model.json` (Ramp `92`-obs critic cannot export via `export_critic.py` for `84`-obs Unity actor inputs).
- **Matt Rung 2 (`m2ramp_ss_v1_it200.onnx`) qualified G1 (`100/100`) & Crowd (`-33%` E19 falls), held back at `pytest` Turntable regression:**
  - During post-promotion `pytest tests -q`, `tests/test_events.py::test_turntable_heat` failed (`4/8 DONE < 6` on `seed=1`, `3.50/8` mean across `seeds 0..5` vs `7.33/8` for `HEAD` `r2_v8_it600`).
  - Rolled `parity/brains/rung2.onnx(.json)`, `Assets/PoOlympic/Models/Brains/rung2.onnx(.json)`, and Matt's `rung2` `confidence_model.json` entry back to `HEAD` (`r2_v8_it600`); retained verified `m2ramp_ss_v1_it200.onnx` and diagnostics (`DOCS/20261112/matt-rung2-candidate-hold.md`).
- **Zombie Rung 2 (`zombie_rung2.onnx`) unchanged (`23/30` baseline retained):**
  - Six runs (`z2_polish_v1`, `z2_polish_v2`, `z2_polish_sharp_v1`, `z2_allbands_v1`, `z2_gentle_v1`, `z2_explore_v1`) lifted reward to `185.523 (max 188.764)` and eliminated falls (`0` falls across 30 seeds on `it50..399` in `z2_explore_v1`), but remained `0/30` on strict `20 m` backward & sprint tracking tolerances.
- **Verification & cleanup:** Removed intermediate unpromoted `*_it*.onnx*` exports in `parity/brains/` and stray `parity/brains/runs/` while preserving all `< 24 h` `training/runs/` folders; `check_bodies.ps1` clean, `dump_task_cfgs.py --diff` shows `0` existing task cfg changes (9 new opt-in tasks only), `brain_manifest.py --check` shows `30 deployed brains, 0 problem(s)`. Full HTML report generated at `DOCS/20261112/extended_training_report.html`.
