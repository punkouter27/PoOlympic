using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// voices.json (schema po-voice-manifest/1), next to the voice WAVs in
    /// <c>Assets/_Project/Environments/Audio/Characters/voices.json</c>: every roster athlete x 5 emotions x 5 levels,
    /// each cell listing its 5 takes by their real file names (<c>&lt;ID&gt;/&lt;EMOTION&gt;/&lt;emotion&gt;_&lt;take 01..05&gt;_L&lt;level&gt;.wav</c>)
    /// with an <c>exists</c> flag, plus the athlete's procedural voice profile. Written by the editor generator
    /// (PoOlympic › Audio › Rebuild Voice Manifest); <c>exists</c> is true only for a real recording — WAVs baked by
    /// <see cref="CharacterVoiceSynth"/> carry <see cref="CharacterVoiceSynth.PlaceholderMarker"/> and stay false.
    /// The runtime (<see cref="CharacterVoiceBank"/>) plays a WAV only where the manifest says it exists, otherwise the
    /// procedural imitation driven by the athlete's <c>voice</c> profile.
    /// </summary>
    [Serializable]
    public class CharacterVoiceManifest
    {
        public const string Schema = "po-voice-manifest/1";
        public const string Root = "Assets/_Project/Environments/Audio/Characters";
        /// <summary>Manifest emotion order (the spec's), mapped onto <see cref="CharacterEmotion"/>.</summary>
        public static readonly CharacterEmotion[] EmotionOrder =
            { CharacterEmotion.Angry, CharacterEmotion.Happy, CharacterEmotion.Hurt, CharacterEmotion.Insult, CharacterEmotion.Celebrate };

        [Serializable]
        public class Voice
        {
            public float pitchHz, formantScale, breath, rasp, vibratoHz, vibratoDepth, tempo, nasal;
            public string timbre = "";

            public static Voice From(CharacterVoiceSynth.VoiceProfile p) => new()
            {
                pitchHz = p.BaseF0, formantScale = p.FormantScale, breath = p.Breathiness, rasp = p.Grit,
                vibratoHz = p.VibratoHz, vibratoDepth = p.VibratoDepth, tempo = p.TempoScale, nasal = p.NasalBoost, timbre = p.Timbre,
            };

            public bool Valid => pitchHz > 20f && formantScale > 0.3f && tempo > 0.2f;

            public CharacterVoiceSynth.VoiceProfile ToProfile() =>
                new(pitchHz, formantScale, vibratoHz, vibratoDepth, rasp, breath, nasal, tempo, timbre);
        }

        [Serializable] public class Take { public int take; public string file = ""; public bool exists; }

        [Serializable]
        public class Cell
        {
            public int level;
            public string file = "";        // take 01: the cell's representative file
            public bool exists;             // any take recorded
            public Take[] takes = Array.Empty<Take>();
        }

        [Serializable]
        public class Clips
        {
            public Cell[] angry = Array.Empty<Cell>(), happy = Array.Empty<Cell>(), hurt = Array.Empty<Cell>(),
                          insult = Array.Empty<Cell>(), celebrate = Array.Empty<Cell>();

            public Cell[] Of(CharacterEmotion e) => e switch
            {
                CharacterEmotion.Angry => angry, CharacterEmotion.Happy => happy, CharacterEmotion.Hurt => hurt,
                CharacterEmotion.Insult => insult, _ => celebrate,
            };

            public void Set(CharacterEmotion e, Cell[] cells)
            {
                switch (e)
                {
                    case CharacterEmotion.Angry: angry = cells; break;
                    case CharacterEmotion.Happy: happy = cells; break;
                    case CharacterEmotion.Hurt: hurt = cells; break;
                    case CharacterEmotion.Insult: insult = cells; break;
                    default: celebrate = cells; break;
                }
            }
        }

        [Serializable]
        public class Character
        {
            public string id = "", name = "";
            public Voice voice = new();
            public Clips clips = new();
        }

        public string schema = Schema, game = "PoOlympic", generated = "", root = Root, fallback = "procedural";
        public string[] emotions = Array.Empty<string>();
        public int[] levels = Array.Empty<int>();
        public Character[] characters = Array.Empty<Character>();

        // ------------------------------------------------------------------------------------------ queries
        public Character Find(string characterId)
        {
            string id = CharacterVoiceSynth.NormalizeId(characterId);
            foreach (var c in characters)
                if (c != null && string.Equals(c.name, id, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>The manifest says this take (0..4) of (athlete, emotion, level 1..5) is a recorded WAV.</summary>
        public bool Exists(string characterId, CharacterEmotion emotion, int take, int level)
        {
            var cells = Find(characterId)?.clips?.Of(emotion);
            if (cells == null) return false;
            foreach (var cell in cells)
                if (cell != null && cell.level == level && cell.takes != null)
                    foreach (var t in cell.takes)
                        if (t != null && t.take == take + 1) return t.exists;
            return false;
        }

        /// <summary>Parse voices.json (JsonUtility). Null when missing, unparsable or of another schema.</summary>
        public static CharacterVoiceManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var m = JsonUtility.FromJson<CharacterVoiceManifest>(json);
                return m != null && m.schema == Schema && m.characters != null ? m : null;
            }
            catch (ArgumentException) { return null; }
        }

        // ------------------------------------------------------------------------------------------ building
        /// <summary>File of a take relative to <see cref="Root"/> (the project's established naming).</summary>
        public static string FileOf(string characterId, CharacterEmotion emotion, int take, int level) =>
            CharacterVoiceBank.RelativeWavPath(characterId, emotion, take, level).Substring("Characters/".Length);

        /// <summary>Every athlete x 5 emotions x 5 levels x 5 takes. <paramref name="recorded"/> says whether a take is a
        /// real recording; <paramref name="keepVoice"/> returns the profile already in voices.json (null = built-in).</summary>
        public static CharacterVoiceManifest Build(IEnumerable<string> roster, Func<string, CharacterEmotion, int, int, bool> recorded,
                                                   Func<string, Voice> keepVoice, string generatedUtc)
        {
            var m = new CharacterVoiceManifest { generated = generatedUtc ?? "" };
            m.emotions = new string[EmotionOrder.Length];
            for (int e = 0; e < EmotionOrder.Length; e++) m.emotions[e] = EmotionOrder[e].ToString().ToLowerInvariant();
            m.levels = new int[CharacterVoiceSynth.ExpressivenessLevels];
            for (int l = 0; l < m.levels.Length; l++) m.levels[l] = l + 1;
            var list = new List<Character>();
            foreach (var raw in roster)
            {
                if (AgentElo.IsBye(raw)) continue;                     // BYE is not a character
                string id = CharacterVoiceSynth.NormalizeId(raw);
                var kept = keepVoice?.Invoke(id);
                var c = new Character
                {
                    id = id.ToLowerInvariant(), name = id,
                    voice = kept != null && kept.Valid ? kept : Voice.From(CharacterVoiceSynth.BuiltInProfile(id)),
                };
                foreach (var emotion in EmotionOrder)
                {
                    var cells = new Cell[CharacterVoiceSynth.ExpressivenessLevels];
                    for (int lvl = 1; lvl <= cells.Length; lvl++)
                    {
                        var takes = new Take[CharacterVoiceSynth.VariantsPerEmotion];
                        bool any = false;
                        for (int t = 0; t < takes.Length; t++)
                        {
                            bool ex = recorded != null && recorded(id, emotion, t, lvl);
                            any |= ex;
                            takes[t] = new Take { take = t + 1, file = FileOf(id, emotion, t, lvl), exists = ex };
                        }
                        cells[lvl - 1] = new Cell { level = lvl, file = takes[0].file, exists = any, takes = takes };
                    }
                    c.clips.Set(emotion, cells);
                }
                list.Add(c);
            }
            m.characters = list.ToArray();
            return m;
        }

        /// <summary>Stable, readable JSON (2-space indent, invariant numbers, one take per line).</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            sb.Append("{\n");
            sb.Append($"  \"schema\": {Q(schema)},\n  \"game\": {Q(game)},\n  \"generated\": {Q(generated)},\n");
            sb.Append($"  \"root\": {Q(root)},\n");
            sb.Append("  \"emotions\": [").Append(string.Join(", ", Array.ConvertAll(emotions, Q))).Append("],\n");
            sb.Append("  \"levels\": [").Append(string.Join(", ", Array.ConvertAll(levels, l => l.ToString(CultureInfo.InvariantCulture)))).Append("],\n");
            sb.Append($"  \"fallback\": {Q(fallback)},\n");
            sb.Append("  \"characters\": [\n");
            for (int ci = 0; ci < characters.Length; ci++)
            {
                var c = characters[ci];
                var v = c.voice;
                sb.Append("    {\n");
                sb.Append($"      \"id\": {Q(c.id)},\n      \"name\": {Q(c.name)},\n");
                sb.Append($"      \"voice\": {{ \"pitchHz\": {F(v.pitchHz)}, \"formantScale\": {F(v.formantScale)}, \"breath\": {F(v.breath)}, " +
                          $"\"rasp\": {F(v.rasp)}, \"vibratoHz\": {F(v.vibratoHz)}, \"vibratoDepth\": {F(v.vibratoDepth)}, " +
                          $"\"tempo\": {F(v.tempo)}, \"nasal\": {F(v.nasal)}, \"timbre\": {Q(v.timbre)} }},\n");
                sb.Append("      \"clips\": {\n");
                for (int e = 0; e < EmotionOrder.Length; e++)
                {
                    var cells = c.clips.Of(EmotionOrder[e]);
                    sb.Append($"        {Q(emotions[e])}: [\n");
                    for (int k = 0; k < cells.Length; k++)
                    {
                        var cell = cells[k];
                        sb.Append($"          {{ \"level\": {cell.level}, \"file\": {Q(cell.file)}, \"exists\": {(cell.exists ? "true" : "false")}, \"takes\": [\n");
                        for (int t = 0; t < cell.takes.Length; t++)
                        {
                            var tk = cell.takes[t];
                            sb.Append($"            {{ \"take\": {tk.take}, \"file\": {Q(tk.file)}, \"exists\": {(tk.exists ? "true" : "false")} }}");
                            sb.Append(t + 1 < cell.takes.Length ? ",\n" : "\n");
                        }
                        sb.Append("          ] }").Append(k + 1 < cells.Length ? ",\n" : "\n");
                    }
                    sb.Append("        ]").Append(e + 1 < EmotionOrder.Length ? ",\n" : "\n");
                }
                sb.Append("      }\n    }").Append(ci + 1 < characters.Length ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        public int RecordedCount()
        {
            int n = 0;
            foreach (var c in characters)
                foreach (var e in EmotionOrder)
                    foreach (var cell in c.clips.Of(e))
                        foreach (var t in cell.takes) if (t.exists) n++;
            return n;
        }

        public int TakeCount() => characters.Length * EmotionOrder.Length * CharacterVoiceSynth.ExpressivenessLevels * CharacterVoiceSynth.VariantsPerEmotion;
    }
}
