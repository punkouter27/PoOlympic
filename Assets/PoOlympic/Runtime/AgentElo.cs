using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Persistent ELO rating table for every agent (PlayerPrefs JSON, survives restarts).
    /// Standard logistic expectation with K = 32 and initial rating = 1200. Ratings move after every completed
    /// 16-entrant event (RecordEvent): each character against each other character, on their best placing.
    /// </summary>
    public static class AgentElo
    {
        public const string Key = "PoOlympic.AgentElo";
        public const float DefaultElo = 1200f;
        public const float KFactor = 32f;

        [Serializable]
        public class Entry
        {
            public string agent = "";
            public float elo = DefaultElo;
            public int wins;
            public int losses;
            public int tournamentsWon;
        }

        [Serializable]
        class Table
        {
            public List<Entry> entries = new();
        }

        static Table Load()
        {
            var raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return new Table();
            try { return JsonUtility.FromJson<Table>(raw) ?? new Table(); }
            catch (ArgumentException) { return new Table(); }
        }

        static void Save(Table t)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(t));
            PlayerPrefs.Save();
        }

        public static bool IsBye(string agent) =>
            string.IsNullOrWhiteSpace(agent) || string.Equals(agent.Trim(), "BYE", StringComparison.OrdinalIgnoreCase);

        static Entry EnsureEntry(Table t, string agent)
        {
            string id = agent.Trim().ToUpperInvariant();
            var e = t.entries.Find(x => string.Equals(x.agent, id, StringComparison.OrdinalIgnoreCase));
            if (e == null)
            {
                e = new Entry { agent = id, elo = DefaultElo };
                t.entries.Add(e);
            }
            return e;
        }

        /// <summary>Current ELO rating for an agent (1200 default before any rated match).</summary>
        public static float GetElo(string agent)
        {
            if (IsBye(agent)) return 0f;
            string id = agent.Trim().ToUpperInvariant();
            var e = Load().entries.Find(x => string.Equals(x.agent, id, StringComparison.OrdinalIgnoreCase));
            return e != null ? e.elo : DefaultElo;
        }

        public static int GetRoundedElo(string agent) => IsBye(agent) ? 0 : Mathf.RoundToInt(GetElo(agent));

        public static Entry GetEntry(string agent)
        {
            if (IsBye(agent)) return null;
            var t = Load();
            return EnsureEntry(t, agent);
        }

        /// <summary>All known agents (roster + any recorded agents), ordered by ELO descending.</summary>
        public static List<Entry> Leaderboard(IEnumerable<string> roster = null)
        {
            var t = Load();
            if (roster != null)
                foreach (var a in roster)
                    if (!IsBye(a)) EnsureEntry(t, a);
            return t.entries
                .Where(e => !IsBye(e.agent))
                .OrderByDescending(e => e.elo)
                .ThenByDescending(e => e.tournamentsWon)
                .ThenByDescending(e => e.wins)
                .ThenBy(e => e.agent, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Update ELOs after a decisive match (winner vs loser). Returns (winnerElo, loserElo, delta).</summary>
        public static (float winnerElo, float loserElo, float delta) RecordMatch(string winner, string loser)
        {
            if (IsBye(winner) || IsBye(loser) || string.Equals(winner, loser, StringComparison.OrdinalIgnoreCase))
                return (GetElo(winner), GetElo(loser), 0f);

            var t = Load();
            var w = EnsureEntry(t, winner);
            var l = EnsureEntry(t, loser);

            float expectedW = 1f / (1f + Mathf.Pow(10f, (l.elo - w.elo) / 400f));
            float delta = KFactor * (1f - expectedW);
            w.elo += delta;
            l.elo = Mathf.Max(100f, l.elo - delta);
            w.wins++;
            l.losses++;

            Save(t);
            return (w.elo, l.elo, delta);
        }

        /// <summary>
        /// Rate one finished event. entrants[i] scored points[i] (repeats of a character allowed); a character's mark
        /// is its best entrant. Every pair of characters is one game (win 1, tie ½, loss 0) at K / (characters − 1),
        /// so a whole event moves a rating about as much as one head-to-head match. Expectations use the ratings
        /// from before the event. Returns the rating change per character (empty with fewer than two characters).
        /// </summary>
        public static Dictionary<string, float> RecordEvent(IReadOnlyList<string> entrants, IReadOnlyList<int> points)
        {
            var best = new Dictionary<string, int>();
            for (int i = 0; i < entrants.Count && i < points.Count; i++)
            {
                if (IsBye(entrants[i])) continue;
                string id = entrants[i].Trim().ToUpperInvariant();
                if (!best.TryGetValue(id, out int b) || points[i] > b) best[id] = points[i];
            }
            var deltas = new Dictionary<string, float>();
            if (best.Count < 2) return deltas;

            var t = Load();
            var ids = best.Keys.ToList();
            var rows = ids.Select(id => EnsureEntry(t, id)).ToList();
            var change = new float[ids.Count];
            float k = KFactor / (ids.Count - 1);
            for (int a = 0; a < ids.Count; a++)
                for (int b = a + 1; b < ids.Count; b++)
                {
                    float expectedA = 1f / (1f + Mathf.Pow(10f, (rows[b].elo - rows[a].elo) / 400f));
                    int pa = best[ids[a]], pb = best[ids[b]];
                    float scoreA = pa > pb ? 1f : pa == pb ? 0.5f : 0f;
                    change[a] += k * (scoreA - expectedA);
                    change[b] -= k * (scoreA - expectedA);
                    if (pa > pb) { rows[a].wins++; rows[b].losses++; }
                    else if (pb > pa) { rows[b].wins++; rows[a].losses++; }
                }
            for (int a = 0; a < ids.Count; a++)
            {
                float before = rows[a].elo;
                rows[a].elo = Mathf.Max(100f, before + change[a]);
                deltas[ids[a]] = rows[a].elo - before;
            }
            Save(t);
            return deltas;
        }

        /// <summary>The character won a whole series (MeetSeries): counted in <see cref="Entry.tournamentsWon"/>.</summary>
        public static void RecordTournamentWin(string champion)
        {
            if (IsBye(champion)) return;
            var t = Load();
            EnsureEntry(t, champion).tournamentsWon++;
            Save(t);
        }

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
