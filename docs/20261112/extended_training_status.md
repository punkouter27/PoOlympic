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
