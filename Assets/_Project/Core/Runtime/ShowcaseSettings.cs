using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Editor-tunable settings of the attract (demo) mode, the broadcast camera, the athletes' gaze and the Tab overlay's
    /// training-speed benchmark, as one asset: Assets/_Project/Data/Resources/ShowcaseSettings.asset (read at run time
    /// through Resources; the defaults below are used when the asset is missing). Reward weights have their own asset
    /// (RewardPenalties); UI styling is in the UI folder (PanelSettings / USS tokens).
    /// </summary>
    [CreateAssetMenu(menuName = "PoOlympic/Showcase Settings", fileName = "ShowcaseSettings")]
    public sealed class ShowcaseSettings : ScriptableObject
    {
        [Header("Attract (demo) mode")]
        [Tooltip("Seconds without a tap, click, key or gamepad button on the main menu before demo mode starts (0 = never).")]
        public float idleDemoSeconds = 30f;
        [Tooltip("Seconds a demo result stays up before the next event.")]
        public float demoResultSeconds = 6f;

        [Header("Broadcast camera (BroadcastDirector, applied when a scene starts)")]
        public float shotSeconds = 5f;
        public float followSharpness = 3f;
        [Tooltip("rad/s")] public float orbitSpeed = 0.12f;
        public float closeupSeconds = 8f;
        public float contextSeconds = 5f;
        public float shotBlendSeconds = 2.5f;
        public float pacingScale = 1.5f;
        public float handheldAmplitude = 0.15f;

        [Header("Gaze (render-only head look, BoneBinder)")]
        [Tooltip("Largest head yaw off the chest's forward, degrees (a neck's reach).")]
        public float gazeMaxYaw = 70f;
        [Tooltip("Largest head pitch, degrees.")]
        public float gazeMaxPitch = 35f;
        [Tooltip("How fast the head turns toward its target (1/s).")]
        public float gazeSharpness = 6f;
        [Tooltip("Metres ahead of the athlete along its running direction it looks at while moving.")]
        public float gazeLookAhead = 4f;

        [Header("Tab overlay benchmark (SimBenchmark)")]
        [Tooltip("Seconds of MuJoCo stepping a benchmark run measures.")]
        public float benchmarkSeconds = 3f;
        [Tooltip("Milliseconds of stepping per frame, so the page keeps drawing during the run.")]
        public float benchmarkSliceMs = 25f;

        static ShowcaseSettings _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _current = null;

        /// <summary>The project asset (Resources/ShowcaseSettings), else the defaults above.</summary>
        public static ShowcaseSettings Current
        {
            get
            {
                if (_current == null) _current = Resources.Load<ShowcaseSettings>("ShowcaseSettings");
                if (_current == null) _current = CreateInstance<ShowcaseSettings>();
                return _current;
            }
        }

        /// <summary>Tests: use this instance instead of the asset (null = back to the asset).</summary>
        public static void Override(ShowcaseSettings s) => _current = s;
    }
}
