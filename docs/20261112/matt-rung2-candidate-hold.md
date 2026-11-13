# Matt Rung 2 candidate (`m2ramp_ss_v1_it200`) — held at regression gate

## Policy scope & status

- **Candidate:** `parity/brains/m2ramp_ss_v1_it200.onnx` (`PoOlympic-Matt-Rung2-Ramp-SprintSharp`, `model_200.pt`, ONNX SHA-256 `a1866137029d7760b62a5cae38cce592a0a1f0fa429a7b2775f927d116d24523`)
- **Production status:** **NOT deployed (held back)**. Deployed `parity/brains/rung2.onnx` and `Assets/PoOlympic/Models/Brains/rung2.onnx` were restored to `HEAD` (`r2_v8_it600`) after `pytest` regression testing caught a multi-turn Turntable failure. Matt's `rung2` entry in `Assets/PoOlympic/Models/confidence_model.json` was restored alongside `rung2.onnx`.

## Strengths observed

- **G1 Rung 2 (`100` deterministic seeds):** `100/100` pass (`0` falls) vs `29/30` baseline ([eval_100_matt_ss_it200.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/eval_100_matt_ss_it200.json), [g1_rung2_m2ramp_ss_v1_it200_candidate.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/watch/g1_rung2_m2ramp_ss_v1_it200_candidate.json)).
- **Biomechanics (`bio_probe.py`):** `walk 1.19 m/s` (`293 W`, CoT `0.31`), `run 3.00 m/s` (`857 W`, CoT `0.36`), `sprint 3.81 m/s` (`1110 W`, CoT `0.37`), `0.0%` Hill / sat / `>18 rad/s` / arm-clip across all 5 probes ([bio_m2ramp_ss_v1_it200_candidate.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/watch/bio_m2ramp_ss_v1_it200_candidate.json)).
- **E19 Crowd & E05 Gust Gauntlet:**
  - E19 `all` crowd falls dropped `-33%` (`2.25 -> 1.50` falls/heat, `10.90 -> 5.92` pair-s, `8.00/8` finish, `0.0` clips) in [crowd_e19_matt_it200.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/crowd_e19_matt_it200.json).
  - `E05` Gust Gauntlet improved from `2/12 ok, 3 FELL` (`HEAD`) to `7/12 ok, 0 FELL`, and `E19` sprint tightened from `24.98 s` to `22.85 s` ([events_m2ramp_ss_v1_it200_candidate.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/watch/events_m2ramp_ss_v1_it200_candidate.json)).

## Blocking regression (`tests/test_events.py::test_turntable_heat`)

- `turntable.run_heat(..., seed=1)` (8 lanes with randomized `Traits.sample(rng)`: `obs_noise` up to `~0.41`, `latency_substeps` `0..4`, 3 full circles at `WZ = 3.0 rad/s` inside `RING_R = 0.75 m`):
  - `m2ramp_ss_v1_it200`: **`4/8 DONE`** on `seed=1` (fails `assert len(done) >= 6`), with 4 lanes drifting `0.752–0.761 m`; across seeds `0..5`: `[3, 4, 5, 2, 4, 3]` (**mean `3.50/8`**).
  - Baseline `HEAD` (`r2_v8_it600`): **`7/8 DONE`** on `seed=1`; across seeds `0..5`: `[7, 7, 8, 7, 8, 7]` (**mean `7.33/8`**).
- Other `100/100` checkpoints from `m2ramp_ss_v1` also fell short on `turntable.run_heat(seed=1)` (`it150`: `1/8`, `it225`: `4/8`; `it250`: `5/8` at `99/100` G1; `it299`: `6/8` but only `29/30` G1).
- **Recommended next step:** Fine-tune from `m2ramp_ss_v1/model_200.pt` (or `model_250.pt`) with mixed long-duration in-place `wz = ±3.0` commands (`8–10 s`) under full `Traits` `obs_noise` and latency randomization while keeping `SprintSharp` speed/turn tracking and G1 `100/100` gates.
