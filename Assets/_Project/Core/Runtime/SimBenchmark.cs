using System.Diagnostics;
using Mujoco;

namespace PoOlympic
{
    /// <summary>
    /// "Benchmark training speed" of the Tab overlay: steps a private copy of the loaded MuJoCo world (MjScene's model,
    /// the current state copied into a scratch mjData) as fast as this device can for a bounded time, a few milliseconds
    /// per frame so the page keeps drawing, and reports physics steps/s and the agent decisions/s that rate gives at the
    /// brains' control decimation. It measures the local simulation only — no training, no network, no GPU learner —
    /// and says so in its result line. The live heat is not touched (its mjData is only read once).
    /// Settings: ShowcaseSettings (benchmarkSeconds, benchmarkSliceMs).
    /// </summary>
    public static unsafe class SimBenchmark
    {
        static MujocoLib.mjData_* _data;
        static MujocoLib.mjModel_* _model;
        static readonly Stopwatch Clock = new();
        static long _steps;
        static float _seconds;
        static int _athletes, _decimation;
        static double _stepping;

        public static bool Running => _data != null;
        /// <summary>The last result (or the running state), for the overlay; "" before the first run.</summary>
        public static string Result { get; private set; } = "";
        /// <summary>Physics steps per second of the last finished run (0 = none).</summary>
        public static double StepsPerSecond { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _data = null;
            _model = null;
            _steps = 0;
            Result = "";
            StepsPerSecond = 0;
        }

        /// <summary>Start a run in the loaded MuJoCo world; false (with Result saying why) when there is none.</summary>
        public static bool Start()
        {
            if (Running) return true;
            if (!MjScene.InstanceExists || MjScene.Instance.Model == null || MjScene.Instance.Data == null)
            {
                Result = "Benchmark: start an event first (it steps that event's MuJoCo world).";
                return false;
            }
            var s = ShowcaseSettings.Current;
            _model = MjScene.Instance.Model;
            _data = MujocoLib.mj_makeData(_model);
            MujocoLib.mj_copyData(_data, _model, MjScene.Instance.Data);
            _seconds = UnityEngine.Mathf.Max(0.5f, s.benchmarkSeconds);
            _steps = 0;
            _stepping = 0;
            _athletes = 0;
            _decimation = 4;
            foreach (var r in PolicyRunner.Live)
                if (r != null && r.isActiveAndEnabled && r.Initialized)
                {
                    _athletes++;
                    if (r.Contract != null && r.Contract.decimation > 0) _decimation = r.Contract.decimation;
                }
            Result = $"Benchmark running ({_seconds:0.0} s)…";
            return true;
        }

        /// <summary>One slice of stepping (called every frame by the overlay); finishes the run when its time is up.</summary>
        public static void Tick()
        {
            if (!Running) return;
            if (!MjScene.InstanceExists || MjScene.Instance.Model != _model) { Abort("the scene changed"); return; }
            float sliceMs = UnityEngine.Mathf.Clamp(ShowcaseSettings.Current.benchmarkSliceMs, 2f, 100f);
            Clock.Restart();
            long until = (long)(sliceMs / 1000.0 * Stopwatch.Frequency);
            while (Clock.ElapsedTicks < until) { MujocoLib.mj_step(_model, _data); _steps++; }
            _stepping += Clock.Elapsed.TotalSeconds;
            if (_stepping < _seconds) { Result = $"Benchmark running… {_steps:N0} steps"; return; }
            Finish();
        }

        static void Finish()
        {
            // wall time of the stepping only (the slices), not the frames in between
            double wall = _stepping;
            StepsPerSecond = wall > 0 ? _steps / wall : 0;
            double decisions = StepsPerSecond / _decimation * UnityEngine.Mathf.Max(1, _athletes);
            Result = $"Benchmark ({wall:0.0} s of stepping, this device, 1 world, {_athletes} athlete{(_athletes == 1 ? "" : "s")}, CPU): " +
                     $"{StepsPerSecond:N0} physics steps/s = {decisions:N0} agent decisions/s. Local simulation speed, not a training run.";
            UnityEngine.Debug.Log("[SimBenchmark] " + Result);
            Release();
        }


        static void Abort(string why)
        {
            Result = $"Benchmark stopped: {why}.";
            Release();
        }

        static void Release()
        {
            if (_data != null) MujocoLib.mj_deleteData(_data);
            _data = null;
            _model = null;
            Clock.Stop();
        }
    }
}
