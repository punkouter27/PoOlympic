using System;
using System.Collections.Generic;
using Mujoco;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Drives a skinned visual rig from MuJoCo bodies (DESIGN.md §2/§4). Only the pivot bone of each physics body is
    /// driven; bones merged into a body (Spine1, Head, fingers, …) keep their bind-local transforms and follow rigidly.
    /// Offsets are captured once at qpos = 0 (the bind/T-pose), where every MjBody frame is world-aligned:
    ///   bone.world = body.world · offset
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class BoneBinder : MonoBehaviour
    {
        [Serializable]
        public class Binding
        {
            public Transform bone;
            public MjBody body;
            public Vector3 offsetPos;
            public Quaternion offsetRot = Quaternion.identity;
        }

        [Tooltip("Parent-before-child order.")]
        public List<Binding> bindings = new();

        /// <summary>Pivot bone (MATT/Mixamo naming) → physics body name. Mirrors build_mjcf.body_specs().</summary>
        public static readonly Dictionary<string, string> PivotBones = new()
        {
            { "Hips", "pelvis" }, { "Spine", "torso" }, { "Spine2", "chest" }, { "Neck", "head" },
            { "LeftArm", "upper_arm_l" }, { "LeftForeArm", "forearm_l" }, { "RightArm", "upper_arm_r" }, { "RightForeArm", "forearm_r" },
            { "LeftUpLeg", "thigh_l" }, { "LeftLeg", "shin_l" }, { "LeftFoot", "foot_l" }, { "LeftToeBase", "toe_l" },
            { "RightUpLeg", "thigh_r" }, { "RightLeg", "shin_r" }, { "RightFoot", "foot_r" }, { "RightToeBase", "toe_r" },
        };

        /// <summary>Capture offsets. Must be called while the MjBody transforms show qpos = 0 (Edit mode after import).
        /// `prefix` selects one athlete of a meet scene (bodies named L&lt;k&gt;_pelvis, …).</summary>
        public void Capture(Transform physicsRoot, string prefix = "")
        {
            bindings.Clear();
            var bodies = new Dictionary<string, MjBody>();
            foreach (var b in physicsRoot.GetComponentsInChildren<MjBody>(true))
                if (b.name.StartsWith(prefix)) bodies[b.name.Substring(prefix.Length)] = b;
            foreach (var bone in GetComponentsInChildren<Transform>(true)) // depth-first: parents before children
            {
                if (!PivotBones.TryGetValue(bone.name, out var bodyName)) continue;
                if (!bodies.TryGetValue(bodyName, out var body)) throw new KeyNotFoundException($"MjBody '{bodyName}' for bone '{bone.name}'");
                var inv = Quaternion.Inverse(body.transform.rotation);
                bindings.Add(new Binding
                {
                    bone = bone, body = body,
                    offsetPos = inv * (bone.position - body.transform.position),
                    offsetRot = inv * bone.rotation,
                });
            }
        }

        /// <summary>Largest bind-pose position error (m) and rotation of the body frame (deg) — B5 acceptance.</summary>
        public (float posErr, float rotErr) BindError()
        {
            float p = 0, r = 0;
            foreach (var b in bindings)
            {
                p = Mathf.Max(p, (b.bone.position - b.body.transform.position).magnitude);
                r = Mathf.Max(r, Quaternion.Angle(b.body.transform.rotation, Quaternion.identity));
            }
            return (p, r);
        }

        void LateUpdate()
        {
            foreach (var b in bindings)
            {
                var t = b.body.transform;
                b.bone.SetPositionAndRotation(t.position + t.rotation * b.offsetPos, t.rotation * b.offsetRot);
            }
            Gaze();
        }

        // ---------------------------------------------------------------- gaze (render only)
        // The physics has no neck joint (the head is welded to the "head" body that drives the Neck bone), so gaze is a
        // visual look on the Head bone only: it turns toward a target within a neck's reach (ShowcaseSettings gaze*),
        // the point ahead along the athlete's running direction while it moves, else the broadcast camera. Upright
        // athletes only (crawling / fallen bodies keep the bind pose). Nothing here touches MuJoCo.
        /// <summary>Optional world-space gaze target for every athlete (an event's goal, a ball); null = default.</summary>
        public static Func<BoneBinder, Vector3?> GazeTarget;
        Transform _head, _chest, _hipL, _hipR, _pelvis;
        Quaternion _headBind, _look = Quaternion.identity;
        Vector3 _lastPelvis, _vel;
        bool _gazeReady, _gazeTried;
        /// <summary>Current head turn off the bind pose (degrees): the PlayMode gaze test reads it.</summary>
        public float GazeAngle => Quaternion.Angle(Quaternion.identity, _look);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGaze() => GazeTarget = null;

        void Gaze()
        {
            if (!_gazeTried)
            {
                _gazeTried = true;
                foreach (var b in bindings)
                {
                    if (b.bone == null) continue;
                    switch (b.bone.name)
                    {
                        case "Neck":
                            foreach (Transform c in b.bone) if (c.name.EndsWith("Head")) { _head = c; break; }
                            break;
                        case "Spine2": _chest = b.bone; break;
                        case "LeftUpLeg": _hipL = b.bone; break;
                        case "RightUpLeg": _hipR = b.bone; break;
                        case "Hips": _pelvis = b.bone; break;
                    }
                }
                _gazeReady = _head != null && _chest != null && _hipL != null && _hipR != null && _pelvis != null;
                if (_gazeReady) { _headBind = _head.localRotation; _lastPelvis = _pelvis.position; }
            }
            if (!_gazeReady || !Application.isPlaying) return;
            float dt = Time.deltaTime;
            if (dt > 0f) _vel = Vector3.Lerp(_vel, (_pelvis.position - _lastPelvis) / dt, 1f - Mathf.Exp(-4f * dt));
            _lastPelvis = _pelvis.position;
            _head.localRotation = _headBind;                       // undo last frame's look before measuring

            var s = ShowcaseSettings.Current;
            var right = _hipR.position - _hipL.position;
            var facing = Vector3.Cross(right, Vector3.up);         // Unity: forward = right × up
            facing.y = 0f;
            bool upright = Vector3.Dot((_chest.position - _pelvis.position).normalized, Vector3.up) > 0.7f && facing.sqrMagnitude > 1e-4f;
            var want = Quaternion.identity;
            if (upright)
            {
                facing.Normalize();
                var flat = new Vector3(_vel.x, 0f, _vel.z);
                Vector3? target = GazeTarget?.Invoke(this);
                if (target == null)
                {
                    if (flat.magnitude > 0.3f) target = _head.position + flat.normalized * s.gazeLookAhead;
                    else if (Camera.main != null) target = Camera.main.transform.position;
                }
                if (target != null)
                {
                    var dir = target.Value - _head.position;
                    var dirFlat = new Vector3(dir.x, 0f, dir.z);
                    if (dirFlat.sqrMagnitude > 1e-4f)
                    {
                        float yaw = Mathf.Clamp(Vector3.SignedAngle(facing, dirFlat, Vector3.up), -s.gazeMaxYaw, s.gazeMaxYaw);
                        float pitch = Mathf.Clamp(-Mathf.Atan2(dir.y, dirFlat.magnitude) * Mathf.Rad2Deg, -s.gazeMaxPitch, s.gazeMaxPitch);
                        var yawQ = Quaternion.AngleAxis(yaw, Vector3.up);
                        var side = yawQ * Vector3.Cross(Vector3.up, facing);   // the head's right after the yaw
                        want = Quaternion.AngleAxis(pitch, side) * yawQ;
                    }
                }
            }
            _look = Quaternion.Slerp(_look, want, 1f - Mathf.Exp(-s.gazeSharpness * Mathf.Max(dt, 0f)));
            _head.rotation = _look * _head.rotation;
        }
    }
}
