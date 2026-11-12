# 2026-11-12 — Reduce the event roster to 16

## Policy scope

- **Policy interfaces, weights, physics, and training tasks changed:** None.
- The retained playable scenes use existing policies: body-specific Rung 0 and Rung 2 locomotion, crawl, flight, slope, and get-up policies as applicable. No checkpoints are retrained or promoted.
- Event 10 is retained as Crab Shuffle rather than Center Beacon Scramble. Its existing scene builder is used in the scene-readiness follow-up below.
- The Sandpit Long Jump is retained as a catalogue entry, but has no playable scene yet. Existing event scenes support eight athletes per heat; the 16-entrant format runs as two heats rather than 16 simultaneous bodies.

## Retained event IDs

`1, 5, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 27`

Only these events remain in the catalogue and stadium hub. Existing development assets for removed events are preserved. The main menu displays all 16 retained events; eleven have playable scenes, while 15, 16, 17, 20, and 21 appear as disabled “Coming Soon” tiles until their scenes are built and their policies are validated.

## Follow-up: menu numbering and 16-entry event fields

- **Policy scope:** no policy interfaces, weights, physics assets, or training tasks change. Each playable event uses the existing eight-lane scene for two sequential heats. Each heat includes every current agent at least once, then randomly fills remaining entrant slots with replacement.
- Display labels become 01–16; canonical event IDs stay unchanged for scene mappings, records, and policy lookups.
- The five events without built scenes remain unavailable to play, even though they remain in the 16-event catalogue.

## Follow-up: event-scene readiness

- **Policy scope:** Event 10 reuses the existing Rung 2 lateral-shuffle policy, `CrabShuffleEvent`, and roster MuJoCo source/layout. No policy interface, weights, or training task changes.
- Event 10 is added to the built-scene map. Its scene, the stadium hub, and the main menu have been regenerated; SceneIntegrityTests passes with 11 playable scenes.
- Events 15, 16, 17, 20, and 21 remain disabled until their terrain/obstacle environments and matching per-body policies are trained and pass their regression gates. The catalogue's rung4/rung5 labels are not evidence of deployed policies; do not substitute unrelated policies.
- The local WebGL build was attempted but stopped by Windows Application Control blocking Unity's `Bee.Toolchain.LLVM.dll` (`0x800711C7`); no app deployment occurred.

## Follow-up: WebGL retry

- **Policy scope:** no policy interfaces, weights, physics assets, or training tasks changed.
- After the user reported changing the Windows security setting, the normal WebGL builder succeeded: 12 scenes, zero errors, 21.9 minutes, 146 MB reported by Unity.
- Verified `Builds/WebGL/index.html` and the generated loader, data, framework, and WebAssembly files. The complete output is 146.77 MiB.
- SHA-256 hashes of all four athlete GLBs match their pre-build values; temporary texture reductions were restored.
- This was a local build only; no deployment occurred. The roster remains 16 entries, with 11 playable and five unavailable pending implementation and policy validation.

## Follow-up: browser smoke testing

- **Policy scope before the results-layout fix:** none. Only the combined-results label's wrapping changes; policy interfaces, physics, scoring, and training remain unchanged.
- Native Chrome completed both Iron Pedestal heats naturally and displayed the combined 16-entrant result. The combined podium summary was clipped by the single-line card style; enable wrapping specifically for that label before release.
- Native Chrome decoded 69 audio payloads without failures or JavaScript exceptions. The VS Code embedded browser lacks AAC support and cannot serve as the audio acceptance browser for this Unity build.
- SceneIntegrityTests passed 15/15 and TournamentAndVoiceTests passed 7/7. A combined pipe-separated CLI filter matched zero tests, so both classes were rerun separately.
- The normal WebGL deployment workflow is queued because its self-hosted `MSI-PoOlympic` runner is offline. No deployment has occurred during this follow-up.
- The wrapping fix was rebuilt successfully: 12 scenes, zero build errors, 7.6 minutes, 146 MB. Native Chrome completed both heats again; all three overall podium entries are readable and the final OK button returns to the 16-event menu.
- The autonomous tournament completed Alpine Ramp, Terminal Velocity Sprint, and 30m All Fours without watchdog fallbacks. KIM won with ELO 1232; the champion, all four ratings, and tournament win count survived a full browser restart and the rebuilt player.
- The rebuilt player's two-heat retest decoded 76 audio payloads without failures or JavaScript exceptions. A nonfatal Unity AudioListener `addDSP` invalid-handle diagnostic remains on event-to-event transitions; it did not prevent scoring, advancement, or decoding, and is not changed by the layout fix.
- The four original athlete GLB hashes still match their pre-build values. Before/after results captures and the persisted bracket are included in `SCREENSHOTS/event_roster_before_after.html`.
- Deployment fallback: the existing `tools/deploy_webgl.ps1` can publish the browser-tested local build while the normal CI runner is offline. Review and commit only the event-roster, entrant, scene, test, and report files; leave unrelated importer/rendering/Unity Connect changes unstaged.
