# Kim Rung 2 brain promotion

## Policy scope

- **Body:** `kim`
- **Touched policy:** `parity/brains/kim_rung2.onnx` and `Assets/PoOlympic/Models/Brains/kim_rung2.onnx` (plus `.onnx.json` sidecars)
- **Director & physics:** Unchanged (`0` physics XML changes, `0` contract dimension changes; Contract v3, `84` actor obs / `23` actions)
- **Candidate promoted:** `parity/brains/k2ramp_ss_v1_it225.onnx` trained with `PoOlympic-Kim-Rung2-Ramp-SprintSharp` (`4,096` envs, initialized from `k2ramp_lin4_v1/model_399.pt`), replacing `k2_v3_it600` (`29/30` G1 baseline)
- **Source checkpoint:** `training/runs/kim_rung2/2026-11-13_03-58-15_k2ramp_ss_v1/model_225.pt`
- **ONNX SHA-256:** `ef490ae984950ae66a5cab2a250f66d53ffabcdaeaea4cf1cf746735ef09fba2`
- **Physics fingerprint SHA-256:** `b1fa14afabbae823a66389734493e82b668d639f98105ff0f406aa6a59469d07`

## Promotion evidence

- **Selection note (`it 225` vs `it 299`):** Both `it 225` and `it 299` passed `100/100` G1 Rung 2 with `0` falls, but `it 299` failed `tests/test_events.py::test_fallen_runner_with_a_getup_brain_gets_up_and_finishes` (lane `7` `DNF` after knockdown recovery). On `it 225`, both knocked-down Kim lanes (`3` and `7`) recovered via `getup_kim.onnx` and `FINISHED` (`12.1 s` and `13.68 s`, faster than `k2_v3_it600` baseline at `13.72 s` and `14.54 s`).
- **G1 Rung 2 (`100` deterministic seeds, post-promotion):** `100/100` pass (`PASS = True`), `0` falls — verified in [g1_rung2_kim_postpromotion.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/watch/g1_rung2_kim_postpromotion.json) (`eval_100_kim_ss_it225.json` also `100/100`, `0` falls).
- **Biomechanics (`bio_probe.py`, post-promotion):** `0` falls, `0.0%` `>18 rad/s` across all 5 probes (`stand 0.00 m/s`, `walk_1.2 1.21 m/s` `270 W` CoT `0.42` arm-clip `0.8%`, `run_3.0 2.99 m/s` `670 W` CoT `0.42` clip `0.0%`, `sprint_3.8 3.72 m/s` `903 W` CoT `0.46` clip `0.0%`, `spin_2.5 0.01 m/s` `192 W`; `hill <= 0.3%`, `sat <= 0.31%`, `ankle_dorsi <= 0.7%`) — [bio_kim_rung2_postpromotion.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/watch/bio_kim_rung2_postpromotion.json).
- **6-event comparison vs baseline (`k2_v3_it600`, 4 heats/event):**
  - `E05` Gust Gauntlet: `7/8 ok, 1 FELL` (mean place `4.62`, mark `10.81`) vs `6/8 ok, 2 STEPPED OFF` (`5.00`, `11.55`)
  - `E09` Inverted Sprint: `8/8 FINISHED` (mark `15.93`, peak `1.34 m/s`, place `3.50`) vs `16.80` (peak `1.26 m/s`, place `5.50`)
  - `E11` Slalom Sprint: `8/8 FINISHED` (`28.06`, place `5.12`) vs `28.95` (`5.50`)
  - `E12` 360 Turntable: `8/8 DONE` (`7.82`, place `6.12`) vs `8/8 DONE` (`7.60`, `5.25`)
  - `E19` Terminal Velocity Sprint: `8/8 FINISHED` (`24.68`, peak `3.71 m/s`, place `3.50`) vs `25.16` (peak `3.63 m/s`, `3.50`)
  - `E22` Emergency Brake: `8/8 STOPPED` (`0.43`, peak `2.85 m/s`, place `4.25`) vs `0.41` (peak `2.80 m/s`, `4.62`)
  - Recorded in [events_kim_postpromotion.json](file:///c:/Users/punko/Downloads/_CODEMATT/PoOlympic/parity/watch/events_kim_postpromotion.json).

## Deployment and scene validation

- **Confidence calibration (`confidence_model.json`):** Removed the stale `kim_rung2` entry calibrated for the old `k2_v3_it600` brain because Ramp-trained critics take `92` observations (`84` actor + `8` critic privilege block) and cannot be exported via `tools/export_critic.py` for the `84`-obs Unity actor input, matching the pattern used in commit `5256d841` (`grandma-rung2-brain-promotion.md`). Existing `kim_rung2.critic.onnx` remains untouched on disk. Note: `BroadcastFxTests.EveryEventBrainHasACalibratedCritic` in Unity expects `"kim_rung2"` and `"grandma_rung2"` entries and will remain incomplete until 84-obs critics are distilled and re-calibrated.
- **Unity & manifest sync:** `Assets/PoOlympic/Models/Brains/kim_rung2.onnx(.json)` match `parity/brains/kim_rung2.onnx(.json)` (`Get-FileHash` verified); `tools/brain_manifest.py --check` reports `30 deployed brains, 0 problem(s)`.
