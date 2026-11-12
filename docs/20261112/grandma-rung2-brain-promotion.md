# Grandma Rung 2 brain promotion

## Policy scope

This deployment touches only Grandma's Rung 2 locomotion policy. The candidate uses the existing v3 actor interface
(84 observations, 23 actions) and physics fingerprint
`de971d498513174843349626535396adf747e55c09794bbabf4005e6b2dcfc38`; body physics, task rules, observations, and
action mapping are unchanged. Other character policies are not changed.

The candidate ramp critic has 92 inputs and cannot be paired with the 84-input actor. The deployed Grandma Rung 2
critic and confidence calibration are from the previous actor, so the Grandma Rung 2 confidence calibration must be
disabled until a compatible calibration is produced. This changes confidence telemetry only; it does not change policy
selection or control.

## Promotion evidence

- Candidate: `g2ramp_v2_best100_it300.onnx`, checkpoint `model_300.pt`.
- G1 Rung 2 graduation gate: 100/100, zero falls; realism probes passed.
- Training logs contain no NaN/Inf markers. A dedicated gradient-norm metric was not recorded.
- Contract v3 and the existing physics fingerprint are unchanged.

## Deployment and scene validation

- Promoted the candidate actor in `parity/brains/grandma_rung2.onnx`; its checksum matches the candidate sidecar.
- Regenerated `parity/brains/manifest.json`; `brain_manifest.py --check` reports 30 deployed brains and no problems.
- Synced the actor and sidecar into Unity through `PoOlympic/Parity/Sync Contract + Brains`; the Unity asset checksum
  matches the parity brain.
- Removed the previous actor's Grandma Rung 2 confidence calibration. Until a compatible calibration is produced, the
  HUD correctly has no confidence value for that brain; control and policy selection are unchanged.
- Re-ran the deployed brain's G1 Rung 2 gate: 100/100 seeds passed, zero falls. Realism probes had no falls; average
  speeds were 1.23 m/s walk, 2.94 m/s run, and 3.58 m/s sprint. The probe recorded small ankle-inversion cap
  exceedances in up to 5.1% of sprint frames (and 1.2% ankle-dorsiflexion cap exceedance); it reported no body-clipping
  frames or high angular velocity.
- MainMenu entered Play Mode and its demo loaded `Event_HilltopScramble` through the game flow. Runtime inspection
  confirmed Grandma runners G0/G3/G4 loaded `grandma_rung2`; Unity reported zero runtime errors.
- The broad EditMode suite reported 849 tests. It was interrupted during its long crowd-contact checks after memory
  dropped below 4 GB, so the suite is not counted as a pass. The completed `Event_30mAllFours` crowd-contact check
  reported zero failures.

Regression artifacts: `parity/watch/g1_rung2_grandma_rung2_postpromotion.json` and
`parity/watch/bio_grandma_rung2_postpromotion.json`.
