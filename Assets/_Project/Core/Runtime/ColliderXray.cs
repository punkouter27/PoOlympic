using System;
using Mujoco;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Collision-shape view (player option GameSettings.ColliderView): the MuJoCo collision primitives of every moving body
    /// (athletes, pool cubes), drawn through the skinned meshes straight from mjData (geom_xpos / geom_xmat and the
    /// model's geom sizes — the shapes the physics actually uses, not a copy of them). A geom turns from `rest` to
    /// `loaded` with the contact force on it; every contact above arrowMinN gets an arrow along its normal, as long as
    /// the force. Read only, drawn with Graphics.DrawMesh: no scene objects. During an instant replay the shapes
    /// follow the replayed pose, without forces (the replay records qpos, not contacts).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public unsafe class ColliderXray : MonoBehaviour
    {
        [Header("Unity primitives (any size: scaled by their bounds)")]
        public Mesh sphere;
        public Mesh cylinder, cube;
        [Tooltip("PoOlympic/Xray: drawn over everything.")]
        public Material material;
        public Color rest = new(0.35f, 0.9f, 1f, 0.16f), loaded = new(1f, 0.25f, 0.15f, 0.75f), arrow = new(1f, 0.85f, 0.2f, 0.9f);
        [Tooltip("Contact force (N) at which a geom is fully `loaded`.")]
        public float fullForceN = 1500f;
        public float arrowMinN = 30f, arrowMetresPerKn = 0.5f, arrowMaxM = 1.5f;
        [Header("AR Telemetry Overlays (Features 1, 3, 6, 7)")]
        public bool showPhotoFinishLaser = true;
        public Color intentColor = new(0.2f, 0.95f, 0.55f, 0.85f), laserColor = new(1f, 0.15f, 0.2f, 0.32f);

        /// <summary>Shapes drawn last frame (tests, perf overlay).</summary>
        public int Drawn { get; private set; }
        public int TelemetryDrawn { get; private set; }

        const int Sphere = 2, Capsule = 3, Ellipsoid = 4, Cylinder = 5, Box = 6;       // mjtGeom
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        float[] _force;
        MaterialPropertyBlock _block;
        TrackRaceEvent _race;
        bool _raceSearched;

        void LateUpdate()
        {
            Drawn = 0;
            TelemetryDrawn = 0;
            if (material == null || !MjScene.InstanceExists || MjScene.Instance.Model == null) return;
            _block ??= new MaterialPropertyBlock();
            if (showPhotoFinishLaser) DrawPhotoFinish();
            if (!GameSettings.ColliderView) return;
            var m = MjScene.Instance.Model;
            var d = MjScene.Instance.Data;
            int n = (int)m->ngeom;
            if (_force == null || _force.Length != n) _force = new float[n];
            else Array.Clear(_force, 0, n);

            if (Time.timeScale > 0.01f)             // frozen (replay, hit-stop): the contacts are not those of the pose shown
            {
                var f = stackalloc double[6];
                for (int i = 0; i < d->ncon; i++)
                {
                    var c = d->contact[i];
                    MujocoLib.mj_contactForce(m, d, i, f);
                    float fn = (float)Math.Abs(f[0]);
                    _force[c.geom1] += fn;
                    _force[c.geom2] += fn;
                    if (fn < arrowMinN) continue;
                    // the normal runs geom1 → geom2: point it at the moving body
                    var dir = new Vector3((float)c.frame[0], (float)c.frame[2], (float)c.frame[1]);
                    if (m->body_weldid[m->geom_bodyid[c.geom2]] == 0) dir = -dir;
                    float len = Mathf.Min(arrowMaxM, fn * 0.001f * arrowMetresPerKn);
                    var at = new Vector3((float)c.pos[0], (float)c.pos[2], (float)c.pos[1]);
                    Draw(cylinder, at + dir * (len * 0.5f), Quaternion.FromToRotation(Vector3.up, dir), new Vector3(0.012f, len * 0.5f, 0.012f), arrow);
                }
            }

            // ponytail: one draw call per shape (~500 with 8 athletes); instance them if this view ever has to run on a phone
            for (int g = 0; g < n; g++)
            {
                if (m->body_weldid[m->geom_bodyid[g]] == 0) continue;           // static: the scene shows it already
                double* s = m->geom_size + 3 * g, x = d->geom_xpos + 3 * g, r = d->geom_xmat + 9 * g;
                var pos = new Vector3((float)x[0], (float)x[2], (float)x[1]);
                // MuJoCo y ↔ Unity z, and a geom's long axis is its z (Unity primitives: y)
                var up = new Vector3((float)r[2], (float)r[8], (float)r[5]);
                var rot = Quaternion.LookRotation(new Vector3((float)r[1], (float)r[7], (float)r[4]), up);
                var color = Color.Lerp(rest, loaded, Mathf.Clamp01(_force[g] / fullForceN));
                float rad = (float)s[0], half = (float)s[1];
                switch (m->geom_type[g])
                {
                    case Sphere: Draw(sphere, pos, rot, Vector3.one * rad, color); break;
                    case Capsule:
                        Draw(cylinder, pos, rot, new Vector3(rad, half, rad), color);
                        Draw(sphere, pos + up * half, rot, Vector3.one * rad, color);
                        Draw(sphere, pos - up * half, rot, Vector3.one * rad, color);
                        break;
                    case Cylinder: Draw(cylinder, pos, rot, new Vector3(rad, half, rad), color); break;
                    case Ellipsoid: Draw(sphere, pos, rot, new Vector3(rad, (float)s[2], half), color); break;
                    case Box: Draw(cube, pos, rot, new Vector3(rad, (float)s[2], half), color); break;
                }
            }
            foreach (var t in AthleteTelemetry.All)
                if (t != null && t.Ready && t.isActiveAndEnabled) DrawAthleteTelemetry(t);
        }

        /// <summary>Feature 6: Vertical translucent laser plane across the finish line when the leader approaches.</summary>
        void DrawPhotoFinish()
        {
            if (!_raceSearched) { _raceSearched = true; _race = FindAnyObjectByType<TrackRaceEvent>(); }
            if (_race == null || !_race.PhotoFinishActive || _race.runners.Count == 0 || cube == null) return;
            float sign = _race.commandSpeed < 0f ? -1f : 1f;
            float finishX = (float)_race.runners[0].runner.laneOriginX + sign * _race.distance;
            DrawOverlay(cube, new Vector3(finishX, 1.1f, 0f), Quaternion.identity, new Vector3(0.015f, 1.1f, 6.2f), laserColor);
        }

        /// <summary>Features 1, 3, 7: Predictive intent ribbon, ZMP + support polygon ring, and wind/disturbance vector.</summary>
        void DrawAthleteTelemetry(AthleteTelemetry t)
        {
            var groundAt = new Vector3(t.PelvisPosition.x, t.SupportHeight + 0.02f, t.PelvisPosition.z);
            // Feature 1: Intent vector ribbon vs actual velocity
            if (t.IntentWorld.sqrMagnitude > 0.04f)
            {
                float div = Mathf.Clamp01(Vector3.Distance(t.IntentWorld, t.VelocityWorld) / 2.5f);
                var col = Color.Lerp(intentColor, loaded, div);
                float len = Mathf.Clamp(t.IntentWorld.magnitude * 0.35f, 0.2f, 1.4f);
                var dir = t.IntentWorld.normalized;
                DrawOverlay(cube, groundAt + dir * (len * 0.5f), Quaternion.LookRotation(dir, Vector3.up), new Vector3(0.035f, 0.008f, len * 0.5f), col);
            }
            // Feature 3: Support polygon hull & ZMP dot
            if (t.SupportHull.Count >= 2 && !float.IsNaN(t.BalanceMargin))
            {
                var ringCol = Color.Lerp(loaded, rest, Mathf.InverseLerp(-0.04f, 0.08f, t.BalanceMargin));
                for (int i = 0; i < t.SupportHull.Count; i++)
                {
                    var a = t.SupportHull[i] + Vector3.up * 0.015f;
                    var b = t.SupportHull[(i + 1) % t.SupportHull.Count] + Vector3.up * 0.015f;
                    var seg = b - a;
                    float slen = seg.magnitude;
                    if (slen > 0.01f)
                        DrawOverlay(cylinder, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, seg / slen), new Vector3(0.01f, slen * 0.5f, 0.01f), ringCol);
                }
                DrawOverlay(sphere, t.Zmp + Vector3.up * 0.02f, Quaternion.identity, Vector3.one * 0.035f, ringCol);
            }
            // Feature 7: Wind / external disturbance vector on torso
            var ext = t.Runner != null ? t.Runner.AppliedForce : Vector3.zero;
            if (ext.sqrMagnitude > 0.25f)
            {
                float flen = Mathf.Clamp(ext.magnitude * 0.04f, 0.15f, 1.2f);
                var fdir = ext.normalized;
                var torsoAt = t.PelvisPosition + Vector3.up * 0.35f;
                DrawOverlay(cylinder, torsoAt + fdir * (flen * 0.5f), Quaternion.FromToRotation(Vector3.up, fdir), new Vector3(0.015f, flen * 0.5f, 0.015f), arrow);
            }
        }

        void DrawOverlay(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 half, Color color)
        {
            if (mesh == null) return;
            var e = mesh.bounds.extents;
            var scale = new Vector3(half.x / e.x, half.y / e.y, half.z / e.z);
            _block.SetColor(BaseColor, color);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(pos, rot, scale), material, 0, null, 0, _block, false, false);
            TelemetryDrawn++;
        }

        /// <param name="half">half extents wanted along the mesh's own axes (the mesh may be any size: the built-in
        /// sphere and cylinder come at radius 1 here, not Unity's usual 0.5)</param>
        void Draw(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 half, Color color)
        {
            if (mesh == null) return;
            var e = mesh.bounds.extents;
            var scale = new Vector3(half.x / e.x, half.y / e.y, half.z / e.z);
            _block.SetColor(BaseColor, color);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(pos, rot, scale), material, 0, null, 0, _block, false, false);
            Drawn++;
        }
    }
}
