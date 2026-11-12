using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// 16-player single-elimination tournament bracket:
    ///   - 8 starting slots on the LEFT (Round 0 matches 0..3 -> Round 1 matches 0..1 -> Round 2 match 0)
    ///   - 8 starting slots on the RIGHT (Round 0 matches 4..7 -> Round 1 matches 2..3 -> Round 2 match 1)
    ///   - Winner in the MIDDLE (Round 3 match 0: Left Champion vs Right Champion -> Champion)
    /// Pads missing players up to 16 with BYE and auto-advances BYE matchups cleanly.
    /// </summary>
    [Serializable]
    public class TournamentBracket
    {
        public const int BracketSize = 16;
        public const int RoundsCount = 4;
        public const string Bye = "BYE";

        // Standard 16-seed symmetric bracket mapping: seeds 0..15 -> slots 0..7 (Left) and 8..15 (Right).
        static readonly int[] SeedSlots = { 0, 15, 7, 8, 4, 11, 3, 12, 2, 13, 5, 10, 6, 9, 1, 14 };

        [Serializable]
        public class Match
        {
            public int round;
            public int index;
            public string playerA = "";
            public string playerB = "";
            public string winner = "";
            public int scoreA;
            public int scoreB;
            public int eventNumber;
            public string eventName = "";
            public float eloDelta;

            public bool HasPlayers => !string.IsNullOrEmpty(playerA) && !string.IsNullOrEmpty(playerB);
            public bool IsComplete => !string.IsNullOrEmpty(winner);
            public bool IsByeMatch => HasPlayers && (AgentElo.IsBye(playerA) || AgentElo.IsBye(playerB));
            public bool IsPlayable => !IsComplete && HasPlayers && !AgentElo.IsBye(playerA) && !AgentElo.IsBye(playerB);
        }

        public string[] initialSlots = new string[BracketSize];
        public List<Match> matches = new();
        public string champion = "";
        public int tournamentNumber = 1;

        public bool IsFinished => !string.IsNullOrEmpty(champion);

        public static TournamentBracket Create(IReadOnlyList<string> roster, bool shuffleOrder = false, int tournamentNumber = 1, System.Random rng = null)
        {
            var real = (roster ?? Array.Empty<string>())
                .Where(a => !AgentElo.IsBye(a))
                .Select(a => a.Trim().ToUpperInvariant())
                .Distinct()
                .Take(BracketSize)
                .ToList();

            if (shuffleOrder)
            {
                rng ??= new System.Random();
                for (int i = real.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (real[i], real[j]) = (real[j], real[i]);
                }
            }
            else
            {
                real = real
                    .OrderByDescending(AgentElo.GetElo)
                    .ThenBy(a => a, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var slots = new string[BracketSize];
            for (int i = 0; i < BracketSize; i++) slots[i] = Bye;
            for (int seed = 0; seed < real.Count; seed++)
                slots[SeedSlots[seed]] = real[seed];

            var bracket = new TournamentBracket
            {
                initialSlots = slots,
                tournamentNumber = tournamentNumber,
                champion = "",
            };
            bracket.BuildMatchesFromSlots();
            bracket.AutoResolveByes();
            return bracket;
        }

        void BuildMatchesFromSlots()
        {
            matches.Clear();
            int count = BracketSize / 2;
            for (int r = 0; r < RoundsCount; r++)
            {
                for (int i = 0; i < count; i++)
                {
                    var m = new Match { round = r, index = i };
                    if (r == 0)
                    {
                        m.playerA = initialSlots[i * 2];
                        m.playerB = initialSlots[i * 2 + 1];
                    }
                    matches.Add(m);
                }
                count /= 2;
            }
        }

        public Match GetMatch(int round, int index) =>
            matches.Find(m => m.round == round && m.index == index);

        public IEnumerable<Match> RoundMatches(int round) =>
            matches.Where(m => m.round == round).OrderBy(m => m.index);

        /// <summary>Auto-advances any ready match involving BYE (Player vs BYE -> Player; BYE vs BYE -> BYE).</summary>
        public void AutoResolveByes()
        {
            for (int r = 0; r < RoundsCount; r++)
            {
                foreach (var m in RoundMatches(r))
                {
                    if (m.IsComplete || !m.HasPlayers) continue;
                    if (!m.IsByeMatch) continue;

                    bool aBye = AgentElo.IsBye(m.playerA);
                    bool bBye = AgentElo.IsBye(m.playerB);
                    string win = (aBye && bBye) ? Bye : (aBye ? m.playerB : m.playerA);
                    SetWinnerInternal(m, win, 0, 0, 0f);
                }
            }
        }

        /// <summary>Next real (non-BYE) match ready to be played in the arena, alternating Left and Right per round.</summary>
        public Match NextPlayableMatch()
        {
            AutoResolveByes();
            for (int r = 0; r < RoundsCount; r++)
            {
                var roundList = RoundMatches(r).ToList();
                int half = roundList.Count / 2;
                if (half == 0)
                {
                    if (roundList.Count == 1 && roundList[0].IsPlayable) return roundList[0];
                    continue;
                }
                for (int i = 0; i < half; i++)
                {
                    if (roundList[i].IsPlayable) return roundList[i];
                    if (roundList[half + i].IsPlayable) return roundList[half + i];
                }
            }
            return null;
        }

        public bool RecordMatchResult(int round, int index, string winner, int scoreA, int scoreB, int eventNumber = 0, string eventName = "", float eloDelta = 0f)
        {
            var m = GetMatch(round, index);
            if (m == null || m.IsComplete) return false;
            if (!string.Equals(winner, m.playerA, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(winner, m.playerB, StringComparison.OrdinalIgnoreCase))
                return false;

            m.eventNumber = eventNumber;
            m.eventName = eventName ?? "";
            SetWinnerInternal(m, winner, scoreA, scoreB, eloDelta);
            AutoResolveByes();
            return true;
        }

        void SetWinnerInternal(Match m, string winner, int scoreA, int scoreB, float eloDelta)
        {
            m.winner = winner;
            m.scoreA = scoreA;
            m.scoreB = scoreB;
            m.eloDelta = eloDelta;

            if (m.round == RoundsCount - 1)
            {
                champion = winner;
                return;
            }

            var next = GetMatch(m.round + 1, m.index / 2);
            if (next != null)
            {
                if ((m.index & 1) == 0) next.playerA = winner;
                else next.playerB = winner;
            }
        }

        public static string RoundLabel(int round) => round switch
        {
            0 => "ROUND OF 16",
            1 => "QUARTERFINALS",
            2 => "SEMIFINALS",
            3 => "GRAND FINAL",
            _ => $"ROUND {round + 1}",
        };

        public string ToJson() => JsonUtility.ToJson(this);

        public static TournamentBracket FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var b = JsonUtility.FromJson<TournamentBracket>(json);
                if (b == null || b.initialSlots == null || b.initialSlots.Length != BracketSize || b.matches == null || b.matches.Count != 15)
                    return null;
                return b;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
