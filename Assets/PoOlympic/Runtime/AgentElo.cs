using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// Persistent ELO rating table for every agent (PlayerPrefs JSON, survives restarts).
    /// Standard logistic expectation with K = 32 and initial rating = 1200; BYE slots are never rated.
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
            string.IsNullOrWhiteSpace(agent) || string.Equals(agent.Trim(), MeetLineup.ByeName, StringComparison.OrdinalIgnoreCase);

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
