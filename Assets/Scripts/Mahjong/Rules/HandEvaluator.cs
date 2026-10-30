using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UpgradeMahjong.Mahjong
{
    public enum MahjongWaitType
    {
        Unknown,
        Ryanmen,
        Shanpon,
        Kanchan,
        Penchan,
        Tanki
    }

    public sealed class HandCombination
    {
        public readonly int[] ConcealedCounts;
        public readonly List<MahjongMeld> OpenMelds;
        public readonly List<MahjongMeld> Melds = new List<MahjongMeld>();
        public int PairIndex = -1;
        public bool IsChiitoitsu;
        public bool IsKokushi;
        public MahjongWaitType WaitType = MahjongWaitType.Unknown;
        public int WinningTileIndex = -1;
        public int WinningMeldIndex = -1;

        public bool IsClosed { get { return OpenMelds.All(meld => !meld.IsOpen); } }

        public HandCombination(int[] concealedCounts, IEnumerable<MahjongMeld> openMelds)
        {
            ConcealedCounts = (int[])concealedCounts.Clone();
            OpenMelds = openMelds == null ? new List<MahjongMeld>() : new List<MahjongMeld>(openMelds);
        }

        public int[] GetAllTileCounts()
        {
            int[] result = (int[])ConcealedCounts.Clone();
            foreach (MahjongMeld meld in OpenMelds)
                foreach (int tile in meld.Tiles)
                    result[tile]++;
            return result;
        }

    }

    public static class HandEvaluator
    {
        public static bool IsWinning(int[] concealedCounts, IList<MahjongMeld> openMelds = null)
        {
            return FindAllCombinations(concealedCounts, openMelds).Count > 0;
        }

        public static List<HandCombination> ExpandWinningInterpretations(HandCombination source, int winningTileIndex)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            List<HandCombination> result = new List<HandCombination>();

            if (winningTileIndex < 0 || winningTileIndex >= MahjongTiles.TileTypeCount)
            {
                result.Add(CloneCombination(source, MahjongWaitType.Unknown, winningTileIndex, -1));
                return result;
            }

            if (source.IsChiitoitsu)
            {
                result.Add(CloneCombination(source, MahjongWaitType.Tanki, winningTileIndex, -1));
                return result;
            }

            if (source.IsKokushi)
            {
                result.Add(CloneCombination(source, MahjongWaitType.Unknown, winningTileIndex, -1));
                return result;
            }

            if (source.PairIndex == winningTileIndex)
                result.Add(CloneCombination(source, MahjongWaitType.Tanki, winningTileIndex, -1));

            for (int meldIndex = source.OpenMelds.Count; meldIndex < source.Melds.Count; meldIndex++)
            {
                MahjongMeld meld = source.Melds[meldIndex];
                if (meld.Tiles == null || !meld.Tiles.Contains(winningTileIndex)) continue;

                if (meld.Type == MahjongMeldType.Sequence)
                {
                    int start = meld.Tiles.Min();
                    int startRank = MahjongTiles.GetRank(start);
                    MahjongWaitType wait = winningTileIndex == start + 1
                        ? MahjongWaitType.Kanchan
                        : winningTileIndex == start && startRank == 7
                            ? MahjongWaitType.Penchan
                            : winningTileIndex == start + 2 && startRank == 1
                                ? MahjongWaitType.Penchan
                                : MahjongWaitType.Ryanmen;
                    result.Add(CloneCombination(source, wait, winningTileIndex, meldIndex));
                }
                else
                {
                    result.Add(CloneCombination(source, MahjongWaitType.Shanpon, winningTileIndex, meldIndex));
                }
            }

            if (result.Count == 0)
                result.Add(CloneCombination(source, MahjongWaitType.Unknown, winningTileIndex, -1));
            return result;
        }

        public static List<int> FindWinningTiles(int[] thirteenTileCounts, IList<MahjongMeld> openMelds = null)
        {
            ValidateCounts(thirteenTileCounts);
            List<int> result = new List<int>();
            for (int tile = 0; tile < MahjongTiles.TileTypeCount; tile++)
            {
                if (thirteenTileCounts[tile] >= 4) continue;
                int[] candidate = (int[])thirteenTileCounts.Clone();
                candidate[tile]++;
                if (IsWinning(candidate, openMelds)) result.Add(tile);
            }
            return result;
        }

        public static List<HandCombination> FindAllCombinations(int[] concealedCounts, IList<MahjongMeld> openMelds = null)
        {
            ValidateCounts(concealedCounts);
            List<MahjongMeld> fixedMelds = openMelds == null ? new List<MahjongMeld>() : new List<MahjongMeld>(openMelds);
            List<HandCombination> result = new List<HandCombination>();

            int[] work = (int[])concealedCounts.Clone();
            int requiredMelds = 4 - fixedMelds.Count;
            if (requiredMelds >= 0)
            {
                SearchStandard(work, 0, requiredMelds, -1, new List<MahjongMeld>(), fixedMelds, result, new HashSet<string>());
            }

            if (fixedMelds.Count == 0 && IsChiitoitsu(concealedCounts))
            {
                HandCombination hand = new HandCombination(concealedCounts, fixedMelds);
                hand.IsChiitoitsu = true;
                result.Add(hand);
            }

            if (fixedMelds.Count == 0 && IsKokushi(concealedCounts))
            {
                HandCombination hand = new HandCombination(concealedCounts, fixedMelds);
                hand.IsKokushi = true;
                result.Add(hand);
            }

            return result;
        }

        public static int CalculateShanten(int[] counts, int openMeldCount = 0)
        {
            ValidateCounts(counts);
            int[] work = (int[])counts.Clone();
            int normal = 8;
            SearchShanten(work, 0, openMeldCount, 0, 0, ref normal);
            int chiitoitsu = openMeldCount == 0 ? CalculateChiitoitsuShanten(counts) : 99;
            int kokushi = openMeldCount == 0 ? CalculateKokushiShanten(counts) : 99;
            return Math.Min(normal, Math.Min(chiitoitsu, kokushi));
        }

        private static void SearchStandard(int[] counts, int start, int requiredMelds, int pair, List<MahjongMeld> melds, List<MahjongMeld> openMelds, List<HandCombination> result, HashSet<string> signatures)
        {
            while (start < MahjongTiles.TileTypeCount && counts[start] == 0) start++;
            if (start >= MahjongTiles.TileTypeCount)
            {
                if (melds.Count != requiredMelds || pair < 0) return;
                int[] concealedCounts = new int[MahjongTiles.TileTypeCount];
                concealedCounts[pair] += 2;
                foreach (MahjongMeld meld in melds)
                    foreach (int tile in meld.Tiles)
                        concealedCounts[tile]++;
                HandCombination combination = new HandCombination(concealedCounts, openMelds);
                combination.PairIndex = pair;
                combination.Melds.AddRange(openMelds);
                combination.Melds.AddRange(melds);
                string signature = BuildSignature(combination);
                if (signatures.Add(signature)) result.Add(combination);
                return;
            }

            if (pair < 0 && counts[start] >= 2)
            {
                counts[start] -= 2;
                SearchStandard(counts, start, requiredMelds, start, melds, openMelds, result, signatures);
                counts[start] += 2;
            }

            if (melds.Count >= requiredMelds) return;

            if (counts[start] >= 3)
            {
                counts[start] -= 3;
                melds.Add(new MahjongMeld(MahjongMeldType.Triplet, new[] { start, start, start }, false));
                SearchStandard(counts, start, requiredMelds, pair, melds, openMelds, result, signatures);
                melds.RemoveAt(melds.Count - 1);
                counts[start] += 3;
            }

            if (MahjongTiles.GetSuit(start) != MahjongSuit.Honor)
            {
                int rank = MahjongTiles.GetRank(start);
                if (rank <= 7 && counts[start + 1] > 0 && counts[start + 2] > 0)
                {
                    counts[start]--;
                    counts[start + 1]--;
                    counts[start + 2]--;
                    melds.Add(new MahjongMeld(MahjongMeldType.Sequence, new[] { start, start + 1, start + 2 }, false));
                    SearchStandard(counts, start, requiredMelds, pair, melds, openMelds, result, signatures);
                    melds.RemoveAt(melds.Count - 1);
                    counts[start]++;
                    counts[start + 1]++;
                    counts[start + 2]++;
                }
            }
        }

        private static HandCombination CloneCombination(
            HandCombination source,
            MahjongWaitType waitType,
            int winningTileIndex,
            int winningMeldIndex)
        {
            HandCombination clone = new HandCombination(source.ConcealedCounts, source.OpenMelds)
            {
                PairIndex = source.PairIndex,
                IsChiitoitsu = source.IsChiitoitsu,
                IsKokushi = source.IsKokushi,
                WaitType = waitType,
                WinningTileIndex = winningTileIndex,
                WinningMeldIndex = winningMeldIndex
            };
            clone.Melds.AddRange(source.Melds);
            return clone;
        }

        private static string BuildSignature(HandCombination hand)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(hand.PairIndex).Append('|');
            foreach (MahjongMeld meld in hand.Melds)
            {
                builder.Append((int)meld.Type).Append(':');
                foreach (int tile in meld.Tiles) builder.Append(tile).Append(',');
                builder.Append(';');
            }
            return builder.ToString();
        }

        private static bool IsChiitoitsu(int[] counts)
        {
            int pairs = 0;
            int total = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                total += counts[i];
                if (counts[i] >= 2) pairs++;
            }
            return total == 14 && pairs == 7;
        }

        private static bool IsKokushi(int[] counts)
        {
            int[] terminals = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            int total = counts.Sum();
            if (total != 14) return false;
            int unique = 0;
            bool pair = false;
            foreach (int tile in terminals)
            {
                if (counts[tile] > 0) unique++;
                if (counts[tile] > 1) pair = true;
            }
            return unique == terminals.Length && pair;
        }

        private static void SearchShanten(int[] counts, int start, int melds, int pairs, int taatsu, ref int best)
        {
            while (start < counts.Length && counts[start] == 0) start++;
            if (start >= counts.Length)
            {
                int usableTaatsu = Math.Min(taatsu, 4 - melds);
                best = Math.Min(best, 8 - melds * 2 - usableTaatsu - pairs);
                return;
            }
            if (melds > 4 || taatsu > 4) return;

            counts[start]--;
            SearchShanten(counts, start, melds, pairs, taatsu, ref best);
            counts[start]++;

            if (counts[start] >= 3)
            {
                counts[start] -= 3;
                SearchShanten(counts, start, melds + 1, pairs, taatsu, ref best);
                counts[start] += 3;
            }

            if (pairs == 0 && counts[start] >= 2)
            {
                counts[start] -= 2;
                SearchShanten(counts, start, melds, 1, taatsu, ref best);
                counts[start] += 2;
            }

            if (counts[start] >= 2)
            {
                counts[start] -= 2;
                SearchShanten(counts, start, melds, pairs, taatsu + 1, ref best);
                counts[start] += 2;
            }

            if (MahjongTiles.GetSuit(start) != MahjongSuit.Honor)
            {
                int rank = MahjongTiles.GetRank(start);
                if (rank <= 7 && counts[start + 1] > 0 && counts[start + 2] > 0)
                {
                    counts[start]--;
                    counts[start + 1]--;
                    counts[start + 2]--;
                    SearchShanten(counts, start, melds + 1, pairs, taatsu, ref best);
                    counts[start]++;
                    counts[start + 1]++;
                    counts[start + 2]++;
                }
                if (rank <= 8 && counts[start + 1] > 0)
                {
                    counts[start]--;
                    counts[start + 1]--;
                    SearchShanten(counts, start, melds, pairs, taatsu + 1, ref best);
                    counts[start]++;
                    counts[start + 1]++;
                }
                if (rank <= 7 && counts[start + 2] > 0)
                {
                    counts[start]--;
                    counts[start + 2]--;
                    SearchShanten(counts, start, melds, pairs, taatsu + 1, ref best);
                    counts[start]++;
                    counts[start + 2]++;
                }
            }
        }

        private static int CalculateChiitoitsuShanten(int[] counts)
        {
            int pairs = 0;
            int unique = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0) unique++;
                if (counts[i] >= 2) pairs++;
            }
            return 6 - pairs + Math.Max(0, 7 - unique);
        }

        private static int CalculateKokushiShanten(int[] counts)
        {
            int[] terminals = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            int unique = 0;
            bool pair = false;
            foreach (int tile in terminals)
            {
                if (counts[tile] > 0) unique++;
                if (counts[tile] > 1) pair = true;
            }
            return 13 - unique - (pair ? 1 : 0);
        }

        private static void ValidateCounts(int[] counts)
        {
            if (counts == null || counts.Length != MahjongTiles.TileTypeCount)
                throw new ArgumentException("Tile count array must have exactly 34 entries.", nameof(counts));
            if (counts.Any(count => count < 0 || count > 4))
                throw new ArgumentException("Each tile count must be between 0 and 4.", nameof(counts));
        }
    }
}
