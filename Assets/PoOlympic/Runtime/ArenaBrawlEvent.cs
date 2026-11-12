using System;
using System.Collections.Generic;
using System.Linq;
using Mujoco;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Controller for five high-collision arena events sharing a planar homing rule (Python: poolympic/events/arena_brawl.py):
    ///   Sumo   (#2  Sumo Ring):              8 on a r=1.45 m raised ring; push rivals out or down; last standing wins.
    ///   Bridge (#3  Narrow Bridge Crossing): 4v4 head-on crossing on a 14 m x 1.44 m bridge; fastest safe cross wins.
    ///   Pads   (#7  Shrinking Safe-Zone):    4 rounds x 9 s with 7 -> 5 -> 3 -> 1 pads; outside or fallen at buzzer = out.
    ///   Beacon (#10 Center Beacon Scramble): 30 s King-of-the-Hill inside r=1.15 m with fall recovery.
    ///   Hill   (#25 Hilltop Scramble):       4 converging 1.5 deg ramps onto a 2.6x2.6 m summit with fall recovery.
    /// </summary>
    public class ArenaBrawlEvent : MonoBehaviour, IBroadcastBoard, ILaneRoster
    {
        public enum Phase { Ready, Live, Result }
        public enum Mode { Sumo, Bridge, Pads, Beacon, Hill }

        [Serializable]
        public class Runner
        {
            public string name = "L1";
            public PolicyRunner runner;
            [NonSerialized] public string status = "";
            [NonSerialized] public float scoreS, outAt, finishS = -1f, progressM;
            [NonSerialized] public int place, rounds, falls;
            [NonSerialized] public bool down;
            [NonSerialized] public float upT;
            [NonSerialized] public AthleteJudge judge;
        }

        public Mode mode = Mode.Sumo;
        public List<Runner> runners = new();

        public void DropInactiveLanes() => runners.RemoveAll(x => x.runner == null || !x.runner.gameObject.activeInHierarchy);

        [Header("Rules (= arena_brawl.py)")]
        public float maxSeconds = 30f;
        public float countdownSeconds = 3f, resultHoldSeconds = 6f;
        public float sumoRadius = 1.45f;
        public float bridgeLen = 12f, bridgeHalfW = 0.80f;
        public float padRoundSeconds = 9f, padRadius = 1.15f;
        public float beaconRadius = 1.15f;
        public float hillSummitHalf = 1.32f, hillRampAxisHalf = 0.85f;
        public bool recoverFromFalls = true;
        public bool autoRestart = true, randomTraits = true;
        public int seed = 1;

        [Header("Editor-created Pad Visuals (Pads mode only, zero runtime Instantiate/Destroy)")]
        public Transform[] padVisuals = Array.Empty<Transform>();

        static readonly int[] PadCounts = { 7, 5, 3, 1 };

        public Phase Current { get; private set; } = Phase.Ready;
        public int Attempt { get; private set; }
        public float PhaseTime { get; private set; }
        public float LiveTime { get; private set; }

        System.Random _rng;
        int _liveStartTick, _prevTick, _nextRoundIdx;
        bool _traitsPending = true;
        readonly List<Vector2[]> _padSets = new();
        readonly int[] _padRoundShow = { -1 };

        public static (float maxS, bool recover) Defaults(Mode m) => m switch
        {
            Mode.Sumo => (30f, false),
            Mode.Bridge => (30f, false),
            Mode.Pads => (36f, true),
            Mode.Beacon => (30f, true),
            Mode.Hill => (35f, true),
            _ => (30f, false),
        };

        void Start() => BeginAttempt(first: true);
        public void Restart() => BeginAttempt(first: false);

        void BeginAttempt(bool first)
        {
            if (!first) Attempt++;
            int activeSeed = seed + Attempt;
            _rng = new System.Random(activeSeed);
            _nextRoundIdx = 0;
            _padRoundShow[0] = -1;
            _padSets.Clear();
            if (mode == Mode.Pads)
                for (int r = 0; r < PadCounts.Length; r++)
                    _padSets.Add(RoundPads(activeSeed, r));

            foreach (var r in runners)
            {
                r.status = ""; r.scoreS = 0f; r.outAt = 0f; r.finishS = -1f; r.progressM = 0f;
                r.place = 0; r.rounds = 0; r.falls = 0; r.down = false; r.upT = 0f;
                r.runner.SetRecovering(false);
                r.runner.lieStill = false;
                r.runner.command = Vector3.zero;
                r.runner.laneKeeping = false;
                r.runner.steer = null;
                if (!first) r.runner.RequestReset();
            }
            UpdatePadVisuals(0, visible: mode == Mode.Pads);
            Current = Phase.Ready;
            PhaseTime = LiveTime = 0f;
            _traitsPending = true;
        }

        void DrawTraits()
        {
            var tr = runners[0].runner.Contract.trait_ranges;
            for (int i = 0; i < runners.Count; i++)
                runners[i].runner.DrawTraits(tr, _rng, randomTraits, seed * 1000 + Attempt * 16 + i);
        }

        string[] SupportGeoms() => mode switch
        {
            Mode.Sumo => new[] { "ground", "sumo_ring" },
            Mode.Bridge => new[] { "ground", "bridge" },
            Mode.Hill => new[]
            {
                "ground", "hill_summit",
                "hill_ramp_e", "hill_ramp_w", "hill_ramp_n", "hill_ramp_s",
                "hill_pad_e", "hill_pad_w", "hill_pad_n", "hill_pad_s"
            },
            _ => new[] { "ground" },
        };

        unsafe void Update()
        {
            if (runners.Count == 0 || runners.Any(r => r.runner == null || !r.runner.Initialized)) return;
            if (!MjScene.InstanceExists || MjScene.Instance.Data == null) return;
            var m = MjScene.Instance.Model;
            var d = MjScene.Instance.Data;
            var supports = SupportGeoms();
            foreach (var r in runners)
                r.judge ??= new AthleteJudge(m, r.runner, supports);
            if (_traitsPending) { DrawTraits(); _traitsPending = false; }

            PhaseTime += Time.deltaTime;
            var lead = runners[0].runner;
            float tick = (float)(lead.Contract.timestep * lead.Contract.decimation);

            switch (Current)
            {
                case Phase.Ready:
                    if (HoldStart) { PhaseTime = 0f; break; }
                    if (PhaseTime >= countdownSeconds)
                    {
                        Current = Phase.Live;
                        PhaseTime = 0f;
                        _liveStartTick = _prevTick = lead.ControlTick;
                        for (int i = 0; i < runners.Count; i++) AttachSteer(i, runners[i]);
                    }
                    break;

                case Phase.Live:
                    LiveTime = (lead.ControlTick - _liveStartTick) * tick;
                    float elapsed = (lead.ControlTick - _prevTick) * tick;
                    _prevTick = lead.ControlTick;

                    int rndIdx = mode == Mode.Pads ? Mathf.Min((int)(LiveTime / padRoundSeconds), PadCounts.Length - 1) : 0;
                    if (mode == Mode.Pads && rndIdx != _padRoundShow[0]) UpdatePadVisuals(rndIdx, visible: true);

                    for (int i = 0; i < runners.Count; i++)
                    {
                        var r = runners[i];
                        if (!string.IsNullOrEmpty(r.status)) continue;

                        if (r.down)
                        {
                            r.upT = r.runner.Upright(d) ? r.upT + elapsed : 0f;
                            if (r.upT >= PolicyRunner.UpHoldSeconds)
                            {
                                r.down = false;
                                r.runner.SetRecovering(false);
                                AttachSteer(i, r);
                            }
                            continue;
                        }

                        int ra = r.runner.Binding.RootQposAdr;
                        float px = (float)d->qpos[ra], py = (float)d->qpos[ra + 1];
                        string why = r.judge.Eliminated(m, d);

                        if (mode == Mode.Sumo)
                        {
                            if (why != null || Mathf.Sqrt(px * px + py * py) > sumoRadius + 0.08f)
                            {
                                r.status = "OUT"; r.outAt = LiveTime; r.progressM = LiveTime; Stand(r);
                            }
                            continue;
                        }
                        if (mode == Mode.Bridge)
                        {
                            float prog = i < 4 ? px : (bridgeLen - px);
                            r.progressM = Mathf.Max(r.progressM, prog);
                            if (why != null || Mathf.Abs(py) > bridgeHalfW + 0.12f)
                            {
                                r.status = "FELL"; r.outAt = LiveTime; Stand(r); continue;
                            }
                            if (prog >= bridgeLen)
                            {
                                r.status = "FINISHED"; r.finishS = LiveTime; Stand(r); continue;
                            }
                            continue;
                        }

                        if (recoverFromFalls && why == "FELL" && r.runner.CanRecover)
                        {
                            r.down = true; r.upT = 0f; r.falls++;
                            Stand(r);
                            r.runner.SetRecovering(true);
                            continue;
                        }

                        if (mode == Mode.Beacon && Mathf.Sqrt(px * px + py * py) <= beaconRadius)
                            r.scoreS += elapsed;
                        else if (mode == Mode.Hill && Mathf.Abs(px) <= hillSummitHalf && Mathf.Abs(py) <= hillSummitHalf)
                            r.scoreS += elapsed;
                    }

                    if (mode == Mode.Pads && LiveTime >= (_nextRoundIdx + 1) * padRoundSeconds)
                    {
                        var pads = _padSets[_nextRoundIdx];
                        var active = new List<(Runner r, Vector2 pos, float nearD)>();
                        foreach (var r in runners.Where(x => string.IsNullOrEmpty(x.status)))
                        {
                            int ra = r.runner.Binding.RootQposAdr;
                            var pos = new Vector2((float)d->qpos[ra], (float)d->qpos[ra + 1]);
                            float nearD = pads.Min(p => Vector2.Distance(pos, p));
                            r.progressM = -nearD;
                            active.Add((r, pos, nearD));
                        }
                        var safe = new HashSet<Runner>();
                        for (int pi = 0; pi < pads.Length; pi++)
                        {
                            Runner best = null; float bestD = float.MaxValue;
                            foreach (var a in active)
                            {
                                if (a.r.down || safe.Contains(a.r)) continue;
                                float dPad = Vector2.Distance(a.pos, pads[pi]);
                                if (dPad <= padRadius && dPad < bestD) { bestD = dPad; best = a.r; }
                            }
                            if (best != null) safe.Add(best);
                        }
                        foreach (var a in active)
                        {
                            if (!safe.Contains(a.r)) { a.r.status = "OUT"; a.r.outAt = LiveTime; Stand(a.r); }
                            else a.r.rounds++;
                        }
                        _nextRoundIdx++;
                        if (_nextRoundIdx >= PadCounts.Length || runners.Count(x => string.IsNullOrEmpty(x.status)) <= 1)
                            Finish();
                    }

                    if (Current == Phase.Live)
                    {
                        bool done = (mode == Mode.Sumo && LiveTime > 1f && runners.Count(x => string.IsNullOrEmpty(x.status)) <= 1)
                                 || (mode == Mode.Bridge && runners.All(x => !string.IsNullOrEmpty(x.status)))
                                 || LiveTime >= maxSeconds;
                        if (done) Finish();
                    }
                    break;

                case Phase.Result:
                    if (autoRestart && PhaseTime >= resultHoldSeconds) Restart();
                    break;
            }
        }

        void AttachSteer(int index, Runner r)
        {
            r.runner.laneKeeping = false;
            r.runner.steer = (lx, ly, yaw, c) => ComputeCommand(index, r, yaw);
        }

        void Stand(Runner r) { r.runner.command = Vector3.zero; r.runner.laneKeeping = false; r.runner.steer = null; }

        unsafe Vector3 ComputeCommand(int i, Runner me, double yaw)
        {
            var d = MjScene.Instance.Data;
            int ra = me.runner.Binding.RootQposAdr;
            double px = d->qpos[ra], py = d->qpos[ra + 1];
            double tx = 0, ty = 0;
            float maxVx = 3.4f, slowR = 0.5f, stopR = 0f;

            switch (mode)
            {
                case Mode.Sumo:
                    double distC = Math.Sqrt(px * px + py * py);
                    if (distC > 0.95) { tx = 0; ty = 0; maxVx = 2.6f; slowR = 0.2f; }
                    else
                    {
                        double bestD = double.MaxValue;
                        for (int j = 0; j < runners.Count; j++)
                        {
                            if (j == i || !string.IsNullOrEmpty(runners[j].status)) continue;
                            int rja = runners[j].runner.Binding.RootQposAdr;
                            double ox = d->qpos[rja], oy = d->qpos[rja + 1];
                            double dd = Math.Sqrt((ox - px) * (ox - px) + (oy - py) * (oy - py));
                            if (dd < bestD) { bestD = dd; tx = ox * 0.7; ty = oy * 0.7; }
                        }
                        maxVx = 2.1f; slowR = 0.15f;
                    }
                    break;

                case Mode.Bridge:
                    tx = i < 4 ? bridgeLen : 0.0;
                    ty = Math.Clamp(me.runner.laneOriginY * 0.65, -0.42, 0.42);
                    maxVx = 3.4f; slowR = 0.3f;
                    break;

                case Mode.Pads:
                    int rndIdx = _padSets.Count == 0 ? 0 : Mathf.Min((int)(LiveTime / padRoundSeconds), _padSets.Count - 1);
                    var pads = _padSets[rndIdx];
                    double bestP = double.MaxValue;
                    foreach (var p in pads)
                    {
                        double dd = (p.x - px) * (p.x - px) + (p.y - py) * (p.y - py);
                        if (dd < bestP) { bestP = dd; tx = p.x; ty = p.y; }
                    }
                    maxVx = 3.2f; slowR = 1.2f; stopR = 0.08f;
                    break;

                case Mode.Beacon:
                    double ang = 0.9 * LiveTime + 2.0 * Math.PI * i / Math.Max(1, runners.Count);
                    tx = 0.35 * Math.Cos(ang); ty = 0.35 * Math.Sin(ang);
                    maxVx = 3.5f; slowR = 0.6f;
                    break;

                case Mode.Hill:
                    (tx, ty, maxVx, slowR) = HillWaypoint(i, px, py, me.runner.laneOriginX, me.runner.laneOriginY);
                    break;
            }

            return SteerToPoint(yaw, px, py, tx, ty, maxVx, slowR, stopR, me.runner.Contract);
        }

        (double tx, double ty, float maxVx, float slowR) HillWaypoint(int i, double px, double py, double ox, double oy)
        {
            if (Math.Abs(px) <= hillSummitHalf && Math.Abs(py) <= hillSummitHalf)
            {
                double ang = 2.0 * Math.PI * i / Math.Max(1, runners.Count);
                return (0.25 * Math.Cos(ang), 0.25 * Math.Sin(ang), 2.2f, 0.4f);
            }
            if (Math.Abs(ox) >= Math.Abs(oy))
            {
                if (Math.Abs(py) > hillRampAxisHalf)
                    return (Math.Sign(ox) * Math.Min(Math.Abs(px), 2.5), 0.0, 2.6f, 0.2f);
                return (0.0, 0.0, 2.8f, 0.3f);
            }
            if (Math.Abs(px) > hillRampAxisHalf)
                return (0.0, Math.Sign(oy) * Math.Min(Math.Abs(py), 2.5), 2.6f, 0.2f);
            return (0.0, 0.0, 2.8f, 0.3f);
        }

        static Vector3 SteerToPoint(double yaw, double px, double py, double tx, double ty, float maxVx, float slowR, float stopR, Contract ct)
        {
            double dx = tx - px, dy = ty - py;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= stopR) return Vector3.zero;
            double err = Math.Atan2(dy, dx) - yaw + Math.PI;
            err = err - 2.0 * Math.PI * Math.Floor(err / (2.0 * Math.PI)) - Math.PI;
            float wz = (float)Math.Clamp(ct.steering.heading_gain * err, -ct.steering.wz_limit, ct.steering.wz_limit);
            float turnScale = Math.Abs(err) < 0.5 * Math.PI ? (float)Math.Max(0.08, Math.Cos(err)) : 0.08f;
            float vx = (slowR > stopR && dist < slowR)
                ? maxVx * (float)(0.20 + 0.80 * (dist - stopR) / (slowR - stopR))
                : maxVx;
            return new Vector3(vx * turnScale, 0f, wz);
        }

        static Vector2[] RoundPads(int seedValue, int rnd)
        {
            int count = PadCounts[Mathf.Clamp(rnd, 0, PadCounts.Length - 1)];
            var rng = new System.Random(unchecked(seedValue * 7919 + rnd * 131 + 4100));
            if (count == 1)
                return new[] { new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) };
            var pts = new Vector2[count];
            double baseAng = rng.NextDouble() * 2.0 * Math.PI;
            for (int k = 0; k < count; k++)
            {
                double ang = baseAng + 2.0 * Math.PI * k / count + (rng.NextDouble() - 0.5) * 0.36;
                double rad = 1.65 + rng.NextDouble() * 1.10;
                pts[k] = new Vector2((float)(rad * Math.Cos(ang)), (float)(rad * Math.Sin(ang)));
            }
            return pts;
        }

        void UpdatePadVisuals(int rndIdx, bool visible)
        {
            _padRoundShow[0] = rndIdx;
            if (padVisuals == null || padVisuals.Length == 0) return;
            var pads = visible && rndIdx < _padSets.Count ? _padSets[rndIdx] : Array.Empty<Vector2>();
            for (int k = 0; k < padVisuals.Length; k++)
            {
                if (padVisuals[k] == null) continue;
                bool active = visible && k < pads.Length;
                padVisuals[k].gameObject.SetActive(active);
                if (active)
                {
                    // MuJoCo world (px, py) -> Unity world (px, y, py)
                    var pos = padVisuals[k].localPosition;
                    padVisuals[k].localPosition = new Vector3(pads[k].x, pos.y, pads[k].y);
                }
            }
        }

        unsafe void Finish()
        {
            var d = MjScene.Instance.Data;
            foreach (var r in runners)
            {
                int ra = r.runner.Binding.RootQposAdr;
                float px = (float)d->qpos[ra], py = (float)d->qpos[ra + 1];
                if (mode == Mode.Sumo && string.IsNullOrEmpty(r.status))
                {
                    r.status = "SURVIVED"; r.outAt = LiveTime + 10f - Mathf.Sqrt(px * px + py * py);
                }
                else if (mode == Mode.Bridge && string.IsNullOrEmpty(r.status)) r.status = "DNF";
                else if (mode == Mode.Pads && string.IsNullOrEmpty(r.status))
                {
                    r.status = "SURVIVED"; r.outAt = LiveTime + 10f;
                    var pads = _padSets[_padSets.Count - 1];
                    r.progressM = -pads.Min(p => Vector2.Distance(new Vector2(px, py), p));
                }
                else if (mode is Mode.Beacon or Mode.Hill)
                {
                    r.status = "FINISHED"; r.progressM = -Mathf.Sqrt(px * px + py * py);
                }
                Stand(r);
            }

            IEnumerable<Runner> order = mode switch
            {
                Mode.Sumo => runners.OrderBy(r => r.status == "SURVIVED" ? 0 : 1).ThenByDescending(r => r.outAt),
                Mode.Bridge => runners.OrderBy(r => r.status == "FINISHED" ? 0 : r.status == "DNF" ? 1 : 2)
                                      .ThenBy(r => r.status == "FINISHED" ? r.finishS : -r.progressM),
                Mode.Pads => runners.OrderBy(r => r.status == "SURVIVED" ? 0 : 1).ThenByDescending(r => r.rounds)
                                    .ThenByDescending(r => r.outAt).ThenByDescending(r => r.progressM),
                _ => runners.OrderByDescending(r => Mathf.Round(r.scoreS * 100f)).ThenByDescending(r => r.progressM),
            };
            int place = 1;
            foreach (var r in order) r.place = place++;
            Current = Phase.Result;
            PhaseTime = 0f;
        }

        // IBroadcastBoard / IStandingsBoard
        public BoardPhase BoardState => (BoardPhase)(int)Current;
        public int Heat => Attempt;
        public bool HoldStart { get; set; }
        public string SubtitleExtra => mode switch
        {
            Mode.Sumo => $"r = {sumoRadius:0.00} m · last standing",
            Mode.Bridge => $"{bridgeLen:0} m × {bridgeHalfW * 2:0.00} m head-on bridge",
            Mode.Pads => $"{PadCounts.Length} rounds · {padRoundSeconds:0} s each",
            Mode.Beacon => $"{maxSeconds:0} s · center r = {beaconRadius:0.00} m",
            Mode.Hill => $"{maxSeconds:0} s · climb to 2.6 m summit",
            _ => "",
        };
        public string ClockLine => $"{LiveTime:0.00} s";
        public string InfoLine => mode switch
        {
            Mode.Sumo or Mode.Pads => $"{runners.Count(r => string.IsNullOrEmpty(r.status))}/{runners.Count} standing",
            Mode.Bridge => $"{runners.Count(r => r.status == "FINISHED")} crossed",
            Mode.Beacon or Mode.Hill => $"leader {runners.Max(r => r.scoreS):0.0} s",
            _ => "",
        };

        public IEnumerable<Runner> Standings => Current == Phase.Result
            ? runners.OrderBy(r => r.place)
            : mode switch
            {
                Mode.Sumo => runners.OrderBy(r => string.IsNullOrEmpty(r.status) ? 0 : 1).ThenByDescending(r => r.outAt),
                Mode.Bridge => runners.OrderBy(r => r.status == "FINISHED" ? 0 : string.IsNullOrEmpty(r.status) ? 1 : 2)
                                      .ThenBy(r => r.status == "FINISHED" ? r.finishS : -r.progressM),
                Mode.Pads => runners.OrderBy(r => string.IsNullOrEmpty(r.status) ? 0 : 1).ThenByDescending(r => r.rounds).ThenByDescending(r => r.outAt),
                _ => runners.OrderByDescending(r => r.scoreS).ThenByDescending(r => r.progressM),
            };

        public IEnumerable<(int place, string name, string result, bool bad, PolicyRunner runner)> Rows
        {
            get
            {
                int liveP = 1;
                return Standings.Select(r => (Current == Phase.Result ? r.place : liveP++, r.name, Describe(r),
                    r.status is "OUT" or "FELL", r.runner));
            }
        }

        public string Banner => Current switch
        {
            Phase.Ready => Mathf.CeilToInt(countdownSeconds - PhaseTime).ToString(),
            Phase.Result => TryWinningMark(out _, out _, out _) ? $"{runners.First(x => x.place == 1).name} WINS\n{Describe(runners.First(x => x.place == 1))}" : "HEAT OVER",
            _ => LiveTime < 0.8f ? "GO!" : "",
        };

        public bool TryWinningMark(out double value, out bool lowerIsBetter, out string text)
        {
            var w = runners.FirstOrDefault(r => r.place == 1);
            value = 0; text = ""; lowerIsBetter = mode == Mode.Bridge;
            if (Current != Phase.Result || w == null) return false;
            switch (mode)
            {
                case Mode.Sumo: value = w.outAt; text = Describe(w); return true;
                case Mode.Bridge: if (w.status != "FINISHED") return false; value = w.finishS; text = $"{w.finishS:0.00} s"; return true;
                case Mode.Pads: value = w.rounds; text = $"round {w.rounds}"; return true;
                default: value = w.scoreS; text = $"{w.scoreS:0.00} s"; return true;
            }
        }

        public string Describe(Runner r) => mode switch
        {
            Mode.Sumo => r.status == "SURVIVED" ? "SURVIVED" : string.IsNullOrEmpty(r.status) ? "in ring" : $"{r.outAt:0.00} s",
            Mode.Bridge => r.status == "FINISHED" ? $"{r.finishS:0.00} s" : $"{r.progressM:0.0} m {r.status}",
            Mode.Pads => r.status == "SURVIVED" ? $"WIN · {r.rounds} rds" : string.IsNullOrEmpty(r.status) ? $"round {_nextRoundIdx + 1}" : $"out r{r.rounds + 1} · {r.outAt:0.0} s",
            _ => $"{r.scoreS:0.00} s" + (r.down ? " · DOWN" : r.falls > 0 ? $" · {r.falls}f" : ""),
        };
    }
}
