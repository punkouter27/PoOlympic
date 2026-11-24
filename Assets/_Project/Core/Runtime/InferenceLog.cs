using System.Collections.Generic;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// One console line per brain (ONNX policy) the first time it produces an action in this session:
    /// "[PoOlympic] &lt;brain&gt;.onnx inference running (…)". It is the proof, in a player's browser console, that the
    /// trained policy really runs (AGENTS.md section 3: check it in the local WebGL build at 16:9 and 9:16).
    /// </summary>
    public static class InferenceLog
    {
        public const string Marker = "inference running";
        static readonly HashSet<string> Seen = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Seen.Clear();

        /// <summary>Logs the line for this brain once; true when it logged now.</summary>
        public static bool Note(string brain, string detail)
        {
            if (string.IsNullOrEmpty(brain) || !Seen.Add(brain)) return false;
            Debug.Log($"[PoOlympic] {brain}.onnx {Marker} ({detail})");
            return true;
        }

        /// <summary>Tests: forget which brains were logged.</summary>
        public static void Clear() => Seen.Clear();
    }
}
