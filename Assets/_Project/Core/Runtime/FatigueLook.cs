using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Tiredness you can see (AthleteTelemetry.Fatigue, display only): the athlete's mesh flushes and picks up a
    /// sweat sheen as the fatigue fills, through a MaterialPropertyBlock (no material is instantiated or written).
    /// A rested athlete carries no block, so its renderers keep batching. Added per athlete by the FX upgrade.
    /// </summary>
    [RequireComponent(typeof(AthleteTelemetry))]
    public class FatigueLook : MonoBehaviour
    {
        public Renderer[] skins;
        [Tooltip("Colour the base colour is multiplied towards at full fatigue (epidermal subsurface flush).")]
        public Color flush = new(1f, 0.8f, 0.76f, 1f);
        [Tooltip("Feature 2: high-load joint strain thermal tint when actuators approach torque/speed limits.")]
        public Color strainFlush = new(1f, 0.62f, 0.45f, 1f);
        [Range(0f, 1f)] public float sheen = 0.35f;
        [Tooltip("Fatigue below this shows nothing.")]
        public float from = 0.25f;

        // glTFast materials (the athletes) and URP Lit name the same things differently: set whichever exists
        static readonly int[] ColorIds = { Shader.PropertyToID("baseColorFactor"), Shader.PropertyToID("_BaseColor") };
        static readonly int Roughness = Shader.PropertyToID("roughnessFactor"), Smoothness = Shader.PropertyToID("_Smoothness");

        AthleteTelemetry _telemetry;
        MaterialPropertyBlock _block;
        float _shown, _strainShown;

        /// <summary>0..1 as shown (tests).</summary>
        public float Shown => _shown;

        void Awake()
        {
            _telemetry = GetComponent<AthleteTelemetry>();
            _block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            float f = Mathf.InverseLerp(from, 1f, _telemetry.Fatigue);
            float th = GameSettings.ColliderView ? _telemetry.ThermalLoad : Mathf.InverseLerp(0.82f, 1f, _telemetry.StressMax) * 0.5f;
            if (Mathf.Abs(f - _shown) < 0.02f && Mathf.Abs(th - _strainShown) < 0.03f && (f > 0f || th > 0f || (_shown == 0f && _strainShown == 0f))) return;
            _shown = f;
            _strainShown = th;
            if (skins == null) return;
            var tint = Color.Lerp(Color.Lerp(Color.white, flush, f), strainFlush, th * 0.65f);
            float wet = Mathf.Clamp01(f + th * 0.35f);
            foreach (var r in skins)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if ((f <= 0f && th <= 0f) || m == null) { r.SetPropertyBlock(null, i); continue; }
                    _block.Clear();
                    foreach (int id in ColorIds)
                        if (m.HasProperty(id)) _block.SetColor(id, m.GetColor(id) * tint);
                    if (m.HasProperty(Roughness)) _block.SetFloat(Roughness, m.GetFloat(Roughness) * (1f - sheen * wet));
                    if (m.HasProperty(Smoothness)) _block.SetFloat(Smoothness, Mathf.Lerp(m.GetFloat(Smoothness), 1f, sheen * wet));
                    r.SetPropertyBlock(_block, i);
                }
            }
        }
    }
}
