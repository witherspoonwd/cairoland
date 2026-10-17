using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Tiling.Core
{
    /// <summary>
    /// fzf-style fuzzy matching: a case-insensitive subsequence match scored by where the characters land.
    /// </summary>
    public static class FuzzyMatcher
    {
        private const int ScoreMatch = 16;
        private const int GapStart = -3;
        private const int GapExtension = -1;
        private const int BonusBoundary = 8;
        private const int BonusFirstChar = 10;
        private const int BonusCamel = 7;
        private const int BonusConsecutive = 4;
        private const int FirstCharMultiplier = 2;
        private const int LeadingPenalty = -1;
        private const int MaxLeadingPenalty = -12;

        /// <summary>The score subtracted from secondary-key matches in <see cref="Rank{T}"/>.</summary>
        public const int SecondaryPenalty = 10;

        public static int? Score(string query, string candidate)
        {
            return Match(query, candidate, out _);
        }

        /// <summary>
        /// Indexes into <paramref name="candidate"/> of the matched characters, or an empty list if there is no match.
        /// </summary>
        public static IReadOnlyList<int> MatchPositions(string query, string candidate)
        {
            return Match(query, candidate, out int[]? positions).HasValue ? positions! : (IReadOnlyList<int>)new int[0];
        }

        private static int? Match(string query, string candidate, out int[]? positions)
        {
            positions = null;
            if (query == null || candidate == null)
            {
                return null;
            }

            query = query.Trim();
            int m = query.Length;
            int n = candidate.Length;
            if (m == 0)
            {
                positions = new int[0];
                return 0;
            }

            if (m > n)
            {
                return null;
            }

            string q = query.ToLowerInvariant();
            string c = candidate.ToLowerInvariant();

            // quick subsequence check
            int qi = 0;
            for (int j = 0; j < n && qi < m; j++)
            {
                if (c[j] == q[qi]) qi++;
            }

            if (qi < m)
            {
                return null;
            }

            int[] bonus = new int[n];
            for (int j = 0; j < n; j++)
            {
                bonus[j] = PositionBonus(candidate, j);
            }

            const int none = int.MinValue / 2;
            // score[i, j]: best score with query[i] matched at candidate[j]
            int[,] score = new int[m, n];
            int[,] from = new int[m, n];
            // runBonus[i, j]: bonus of the first character of the consecutive run ending at (i, j). Like fzf, every
            // character of a run earns at least that bonus, so "Code" beats "C o d e" for "code".
            int[,] runBonus = new int[m, n];

            for (int j = 0; j < n; j++)
            {
                if (c[j] == q[0])
                {
                    score[0, j] = ScoreMatch + bonus[j] * FirstCharMultiplier + Math.Max(MaxLeadingPenalty, LeadingPenalty * j);
                }
                else
                {
                    score[0, j] = none;
                }

                from[0, j] = -1;
                runBonus[0, j] = bonus[j];
            }

            for (int i = 1; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    score[i, j] = none;
                    from[i, j] = -1;
                    if (c[j] != q[i])
                    {
                        continue;
                    }

                    for (int k = i - 1; k < j; k++)
                    {
                        int prev = score[i - 1, k];
                        if (prev == none)
                        {
                            continue;
                        }

                        int s;
                        int run;
                        if (k == j - 1)
                        {
                            run = Math.Max(runBonus[i - 1, k], bonus[j]);
                            s = prev + ScoreMatch + Math.Max(run, BonusConsecutive);
                        }
                        else
                        {
                            run = bonus[j];
                            s = prev + ScoreMatch + bonus[j] + GapStart + GapExtension * (j - k - 2);
                        }

                        if (s > score[i, j])
                        {
                            score[i, j] = s;
                            from[i, j] = k;
                            runBonus[i, j] = run;
                        }
                    }
                }
            }

            int best = none;
            int bestJ = -1;
            for (int j = 0; j < n; j++)
            {
                if (score[m - 1, j] > best)
                {
                    best = score[m - 1, j];
                    bestJ = j;
                }
            }

            if (bestJ < 0)
            {
                return null;
            }

            positions = new int[m];
            for (int i = m - 1, j = bestJ; i >= 0; j = from[i, j], i--)
            {
                positions[i] = j;
            }

            return best;
        }

        private static int PositionBonus(string s, int j)
        {
            if (j == 0)
            {
                return BonusFirstChar;
            }

            char prev = s[j - 1];
            char cur = s[j];

            if (IsSeparator(prev) && !IsSeparator(cur))
            {
                return BonusBoundary;
            }

            if (char.IsLower(prev) && char.IsUpper(cur))
            {
                return BonusCamel;
            }

            if (char.IsLetter(prev) && char.IsDigit(cur))
            {
                return BonusCamel;
            }

            return 0;
        }

        private static bool IsSeparator(char ch)
        {
            return char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.' || ch == '/' || ch == '\\' || ch == '(' || ch == ')' || ch == ',';
        }

        /// <summary>
        /// Filters and sorts items. Score is max(primary, secondary - 10); ties break on frecency, then shorter
        /// names. An empty query sorts everything by frecency, then alphabetically.
        /// </summary>
        public static List<T> Rank<T>(string query, IEnumerable<T> items, Func<T, string> primary, Func<T, string?> secondary, Func<T, int> frecency)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return items
                    .OrderByDescending(frecency)
                    .ThenBy(item => primary(item), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            List<(T Item, int Score, int Frecency, int Length)> matches = new List<(T, int, int, int)>();
            foreach (T item in items)
            {
                string name = primary(item) ?? string.Empty;
                int? primaryScore = Score(query, name);
                string? secondaryKey = secondary(item);
                int? secondaryScore = string.IsNullOrEmpty(secondaryKey) ? null : Score(query, secondaryKey!) - SecondaryPenalty;

                int? scoreValue = primaryScore.HasValue && secondaryScore.HasValue
                    ? Math.Max(primaryScore.Value, secondaryScore.Value)
                    : primaryScore ?? secondaryScore;

                if (scoreValue.HasValue)
                {
                    matches.Add((item, scoreValue.Value, frecency(item), name.Length));
                }
            }

            return matches
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Frecency)
                .ThenBy(x => x.Length)
                .Select(x => x.Item)
                .ToList();
        }
    }
}
