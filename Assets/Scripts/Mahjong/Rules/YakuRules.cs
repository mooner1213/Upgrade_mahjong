using System;
using System.Collections.Generic;
using System.Linq;

namespace UpgradeMahjong.Mahjong
{
    public enum MahjongWinMethod
    {
        Ron,
        Tsumo
    }

    [Serializable]
    public sealed class RiichiRuleOptions
    {
        public bool AllowOpenTanyao = true;
        public bool EnableDoubleYakuman = true;
        public bool EnableKazoeYakuman = true;
        public bool EnableKiriageMangan;
        public bool EnableRenhou = true;
    }

    public sealed class MahjongGameContext
    {
        public int RoundWind = 27;
        public int SeatWind = 27;
        public int WinningTileIndex = -1;
        public MahjongWinMethod WinMethod;
        public MahjongWaitType WaitType = MahjongWaitType.Unknown;
        public bool Riichi;
        public bool DoubleRiichi;
        public bool Ippatsu;
        public bool Haitei;
        public bool Houtei;
        public bool Rinshan;
        public bool Chankan;
        public bool Tenhou;
        public bool Chiihou;
        public bool Renhou;
        public bool IsDealer;
        public int Honba;
        public int RiichiSticks;
        public int RedDoraCount;
        public RiichiRuleOptions Rules = new RiichiRuleOptions();
        public readonly List<int> DoraIndicators = new List<int>();
        public readonly List<int> UraDoraIndicators = new List<int>();
    }

    public interface IYaku
    {
        string Name { get; }
        int ClosedHan { get; }
        int OpenHan { get; }
        int YakumanValue { get; }
        int Check(HandCombination hand, MahjongGameContext context);
    }

    public sealed class DetectedYaku : IYaku
    {
        public string Name { get; private set; }
        public int ClosedHan { get; private set; }
        public int OpenHan { get; private set; }
        public int YakumanValue { get; private set; }

        public DetectedYaku(string name, int closedHan, int openHan, int yakumanValue = 0)
        {
            Name = name;
            ClosedHan = closedHan;
            OpenHan = openHan;
            YakumanValue = yakumanValue;
        }

        public int Check(HandCombination hand, MahjongGameContext context)
        {
            if (hand == null) return 0;
            return hand.IsClosed ? ClosedHan : OpenHan;
        }
    }

    public sealed class YakuResult
    {
        public readonly List<IYaku> Yaku = new List<IYaku>();
        public int Han;
        public int YakumanCount;
        public bool HasYaku { get { return Han > 0 || YakumanCount > 0; } }

        public void Add(IYaku yaku, int han)
        {
            if (yaku == null) return;
            if (yaku.YakumanValue > 0)
            {
                Yaku.Add(yaku);
                YakumanCount += yaku.YakumanValue;
                return;
            }
            if (han <= 0) return;
            Yaku.Add(yaku);
            Han += han;
        }

        public void Add(string name, int han)
        {
            if (han <= 0) return;
            DetectedYaku yaku = new DetectedYaku(name, han, han);
            Yaku.Add(yaku);
            Han += han;
        }

        public void AddYakuman(string name, int count = 1)
        {
            if (count <= 0) return;
            Yaku.Add(new DetectedYaku(name, 0, 0, count));
            YakumanCount += count;
        }

        public bool Contains(string name)
        {
            return Yaku.Any(yaku => yaku.Name == name);
        }
    }

    public static class YakuEvaluator
    {
        private static readonly HashSet<int> GreenTiles = new HashSet<int> { 19, 20, 21, 23, 25, 32 };

        public static YakuResult Evaluate(HandCombination hand, MahjongGameContext context, IEnumerable<IYaku> rules = null)
        {
            if (hand == null) throw new ArgumentNullException(nameof(hand));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (rules != null)
            {
                YakuResult custom = new YakuResult();
                foreach (IYaku rule in rules)
                    custom.Add(rule, rule.Check(hand, context));
                return custom;
            }

            YakuResult yakuman = EvaluateYakuman(hand, context);
            if (yakuman.YakumanCount > 0) return yakuman;

            YakuResult result = new YakuResult();
            EvaluateSituationalYaku(hand, context, result);
            EvaluateShapeYaku(hand, context, result);
            return result;
        }

        public static List<IYaku> CreateDefaultRules()
        {
            return new List<IYaku>();
        }

        private static YakuResult EvaluateYakuman(HandCombination hand, MahjongGameContext context)
        {
            YakuResult result = new YakuResult();
            RiichiRuleOptions options = context.Rules ?? new RiichiRuleOptions();
            int[] counts = hand.GetAllTileCounts();

            if (context.Tenhou) result.AddYakuman("천화");
            if (context.Chiihou) result.AddYakuman("지화");

            if (hand.IsKokushi)
            {
                bool thirteenWait = IsKokushiThirteenWait(counts, context.WinningTileIndex);
                result.AddYakuman(thirteenWait ? "국사무쌍 13면대기" : "국사무쌍", thirteenWait && options.EnableDoubleYakuman ? 2 : 1);
            }

            if (IsStandard(hand))
            {
                int concealedTriplets = CountConcealedTriplets(hand, context);
                if (concealedTriplets == 4)
                {
                    bool tanki = EffectiveWaitType(hand, context) == MahjongWaitType.Tanki;
                    result.AddYakuman(tanki ? "사암각 단기" : "사암각", tanki && options.EnableDoubleYakuman ? 2 : 1);
                }

                int dragonTriplets = CountTripletsInRange(hand, 31, 33);
                if (dragonTriplets == 3) result.AddYakuman("대삼원");

                int windTriplets = CountTripletsInRange(hand, 27, 30);
                if (windTriplets == 4)
                    result.AddYakuman("대사희", options.EnableDoubleYakuman ? 2 : 1);
                else if (windTriplets == 3 && hand.PairIndex >= 27 && hand.PairIndex <= 30)
                    result.AddYakuman("소사희");

                int kanCount = hand.Melds.Count(IsKan);
                if (kanCount == 4) result.AddYakuman("사깡쯔");
            }

            if (AllTiles(counts, MahjongTiles.IsHonor)) result.AddYakuman("자일색");
            if (AllTiles(counts, MahjongTiles.IsTerminal)) result.AddYakuman("청노두");
            if (AllTiles(counts, tile => GreenTiles.Contains(tile))) result.AddYakuman("녹일색");

            if (IsChuuren(counts, hand.IsClosed, out int chuurenSuit))
            {
                bool pureWait = IsPureChuurenWait(counts, context.WinningTileIndex, chuurenSuit);
                result.AddYakuman(pureWait ? "순정구련보등" : "구련보등", pureWait && options.EnableDoubleYakuman ? 2 : 1);
            }

            return result;
        }

        private static void EvaluateSituationalYaku(HandCombination hand, MahjongGameContext context, YakuResult result)
        {
            if (hand.IsClosed)
            {
                if (context.DoubleRiichi) result.Add("더블 리치", 2);
                else if (context.Riichi) result.Add("리치", 1);
                if (context.Ippatsu && (context.Riichi || context.DoubleRiichi)) result.Add("일발", 1);
                if (context.WinMethod == MahjongWinMethod.Tsumo) result.Add("멘젠쯔모", 1);
            }

            if (context.Rinshan) result.Add("영상개화", 1);
            if (context.Chankan) result.Add("창깡", 1);
            if (context.Haitei && context.WinMethod == MahjongWinMethod.Tsumo) result.Add("해저로월", 1);
            if (context.Houtei && context.WinMethod == MahjongWinMethod.Ron) result.Add("하저로어", 1);
            if (context.Renhou && context.WinMethod == MahjongWinMethod.Ron && (context.Rules == null || context.Rules.EnableRenhou))
                result.Add("인화", 5);
        }

        private static void EvaluateShapeYaku(HandCombination hand, MahjongGameContext context, YakuResult result)
        {
            int[] counts = hand.GetAllTileCounts();
            RiichiRuleOptions options = context.Rules ?? new RiichiRuleOptions();

            if (AllTiles(counts, MahjongTiles.IsSimple) && (hand.IsClosed || options.AllowOpenTanyao))
                result.Add("탕야오", 1);

            if (hand.IsChiitoitsu)
            {
                result.Add("치또이쯔", 2);
            }
            else if (IsStandard(hand))
            {
                if (IsPinfu(hand, context)) result.Add("평화", 1);

                int identicalSequencePairs = CountIdenticalSequencePairs(hand);
                if (hand.IsClosed && identicalSequencePairs >= 2) result.Add("량페코", 3);
                else if (hand.IsClosed && identicalSequencePairs == 1) result.Add("이페코", 1);

                AddYakuhai(hand, context, result);

                if (HasSanshokuDoujun(hand)) result.Add("삼색동순", hand.IsClosed ? 2 : 1);
                if (HasIttsu(hand)) result.Add("일기통관", hand.IsClosed ? 2 : 1);
                if (HasSanshokuDoukou(hand)) result.Add("삼색동각", 2);
                if (hand.Melds.All(IsTripletOrKan)) result.Add("또이또이", 2);
                if (CountConcealedTriplets(hand, context) >= 3) result.Add("삼암각", 2);
                if (hand.Melds.Count(IsKan) >= 3) result.Add("삼깡쯔", 2);
                if (IsShousangen(hand)) result.Add("소삼원", 2);

                bool junchan = IsJunchan(hand);
                if (junchan) result.Add("준찬타", hand.IsClosed ? 3 : 2);
                else if (IsChanta(hand)) result.Add("찬타", hand.IsClosed ? 2 : 1);
            }

            if (AllTiles(counts, MahjongTiles.IsTerminalOrHonor)) result.Add("혼노두", 2);

            int numberedSuitCount = CountNumberedSuits(counts, out bool hasHonors);
            if (numberedSuitCount == 1)
            {
                if (hasHonors) result.Add("혼일색", hand.IsClosed ? 3 : 2);
                else result.Add("청일색", hand.IsClosed ? 6 : 5);
            }
        }

        private static void AddYakuhai(HandCombination hand, MahjongGameContext context, YakuResult result)
        {
            if (HasTriplet(hand, 31)) result.Add("역패 백", 1);
            if (HasTriplet(hand, 32)) result.Add("역패 발", 1);
            if (HasTriplet(hand, 33)) result.Add("역패 중", 1);
            if (HasTriplet(hand, context.SeatWind)) result.Add("자풍패", 1);
            if (HasTriplet(hand, context.RoundWind)) result.Add("장풍패", 1);
        }

        private static bool IsPinfu(HandCombination hand, MahjongGameContext context)
        {
            return hand.IsClosed
                && hand.Melds.All(meld => meld.Type == MahjongMeldType.Sequence)
                && !MahjongTiles.IsValueHonor(hand.PairIndex, context.RoundWind, context.SeatWind)
                && EffectiveWaitType(hand, context) == MahjongWaitType.Ryanmen;
        }

        private static MahjongWaitType EffectiveWaitType(HandCombination hand, MahjongGameContext context)
        {
            return context.WaitType != MahjongWaitType.Unknown ? context.WaitType : hand.WaitType;
        }

        private static bool IsStandard(HandCombination hand)
        {
            return !hand.IsChiitoitsu && !hand.IsKokushi && hand.PairIndex >= 0 && hand.Melds.Count == 4;
        }

        private static bool IsTripletOrKan(MahjongMeld meld)
        {
            return meld.Type != MahjongMeldType.Sequence;
        }

        private static bool IsKan(MahjongMeld meld)
        {
            return meld.Type == MahjongMeldType.OpenKan || meld.Type == MahjongMeldType.ClosedKan || meld.Type == MahjongMeldType.AddedKan;
        }

        private static bool HasTriplet(HandCombination hand, int tile)
        {
            return tile >= 0 && tile < MahjongTiles.TileTypeCount
                && hand.Melds.Any(meld => IsTripletOrKan(meld) && meld.Tiles != null && meld.Tiles.Length > 0 && meld.Tiles[0] == tile);
        }

        private static int CountTripletsInRange(HandCombination hand, int minimum, int maximum)
        {
            return hand.Melds.Count(meld => IsTripletOrKan(meld)
                && meld.Tiles != null
                && meld.Tiles.Length > 0
                && meld.Tiles[0] >= minimum
                && meld.Tiles[0] <= maximum);
        }

        private static int CountConcealedTriplets(HandCombination hand, MahjongGameContext context)
        {
            int result = 0;
            for (int i = 0; i < hand.Melds.Count; i++)
            {
                MahjongMeld meld = hand.Melds[i];
                if (!IsTripletOrKan(meld) || meld.IsOpen) continue;
                bool completedByRon = context.WinMethod == MahjongWinMethod.Ron
                    && i == hand.WinningMeldIndex
                    && hand.WaitType == MahjongWaitType.Shanpon;
                if (!completedByRon) result++;
            }
            return result;
        }

        private static int CountIdenticalSequencePairs(HandCombination hand)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (MahjongMeld meld in hand.Melds)
            {
                if (meld.Type != MahjongMeldType.Sequence || meld.Tiles == null || meld.Tiles.Length == 0) continue;
                int start = meld.Tiles.Min();
                counts[start] = counts.TryGetValue(start, out int value) ? value + 1 : 1;
            }
            return counts.Values.Sum(value => value / 2);
        }

        private static bool HasSanshokuDoujun(HandCombination hand)
        {
            HashSet<int> starts = new HashSet<int>(hand.Melds
                .Where(meld => meld.Type == MahjongMeldType.Sequence)
                .Select(meld => meld.Tiles.Min()));
            for (int rankOffset = 0; rankOffset <= 6; rankOffset++)
                if (starts.Contains(rankOffset) && starts.Contains(9 + rankOffset) && starts.Contains(18 + rankOffset))
                    return true;
            return false;
        }

        private static bool HasIttsu(HandCombination hand)
        {
            HashSet<int> starts = new HashSet<int>(hand.Melds
                .Where(meld => meld.Type == MahjongMeldType.Sequence)
                .Select(meld => meld.Tiles.Min()));
            for (int suit = 0; suit < 3; suit++)
            {
                int offset = suit * 9;
                if (starts.Contains(offset) && starts.Contains(offset + 3) && starts.Contains(offset + 6)) return true;
            }
            return false;
        }

        private static bool HasSanshokuDoukou(HandCombination hand)
        {
            HashSet<int> triplets = new HashSet<int>(hand.Melds
                .Where(IsTripletOrKan)
                .Select(meld => meld.Tiles[0]));
            for (int rankOffset = 0; rankOffset < 9; rankOffset++)
                if (triplets.Contains(rankOffset) && triplets.Contains(9 + rankOffset) && triplets.Contains(18 + rankOffset))
                    return true;
            return false;
        }

        private static bool IsShousangen(HandCombination hand)
        {
            return CountTripletsInRange(hand, 31, 33) == 2 && hand.PairIndex >= 31 && hand.PairIndex <= 33;
        }

        private static bool IsChanta(HandCombination hand)
        {
            if (!IsStandard(hand) || !MahjongTiles.IsTerminalOrHonor(hand.PairIndex)) return false;
            bool hasSequence = false;
            bool hasHonor = MahjongTiles.IsHonor(hand.PairIndex);
            foreach (MahjongMeld meld in hand.Melds)
            {
                if (meld.Type == MahjongMeldType.Sequence)
                {
                    hasSequence = true;
                    int startRank = MahjongTiles.GetRank(meld.Tiles.Min());
                    if (startRank != 1 && startRank != 7) return false;
                }
                else
                {
                    int tile = meld.Tiles[0];
                    if (!MahjongTiles.IsTerminalOrHonor(tile)) return false;
                    if (MahjongTiles.IsHonor(tile)) hasHonor = true;
                }
            }
            return hasSequence && hasHonor;
        }

        private static bool IsJunchan(HandCombination hand)
        {
            if (!IsStandard(hand) || !MahjongTiles.IsTerminal(hand.PairIndex)) return false;
            bool hasSequence = false;
            foreach (MahjongMeld meld in hand.Melds)
            {
                if (meld.Type == MahjongMeldType.Sequence)
                {
                    hasSequence = true;
                    int startRank = MahjongTiles.GetRank(meld.Tiles.Min());
                    if (startRank != 1 && startRank != 7) return false;
                }
                else if (!MahjongTiles.IsTerminal(meld.Tiles[0]))
                {
                    return false;
                }
            }
            return hasSequence;
        }

        private static int CountNumberedSuits(int[] counts, out bool hasHonors)
        {
            bool[] suits = new bool[3];
            hasHonors = false;
            for (int tile = 0; tile < counts.Length; tile++)
            {
                if (counts[tile] == 0) continue;
                if (tile >= 27) hasHonors = true;
                else suits[tile / 9] = true;
            }
            return suits.Count(value => value);
        }

        private static bool AllTiles(int[] counts, Func<int, bool> predicate)
        {
            bool found = false;
            for (int tile = 0; tile < counts.Length; tile++)
            {
                if (counts[tile] <= 0) continue;
                found = true;
                if (!predicate(tile)) return false;
            }
            return found;
        }

        private static bool IsKokushiThirteenWait(int[] counts, int winningTile)
        {
            int[] required = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            if (!required.Contains(winningTile)) return false;
            int[] beforeWin = (int[])counts.Clone();
            if (beforeWin[winningTile] <= 0) return false;
            beforeWin[winningTile]--;
            return required.All(tile => beforeWin[tile] == 1) && beforeWin.Sum() == 13;
        }

        private static bool IsChuuren(int[] counts, bool isClosed, out int suit)
        {
            suit = -1;
            if (!isClosed || counts.Skip(27).Any(count => count > 0)) return false;
            for (int candidateSuit = 0; candidateSuit < 3; candidateSuit++)
            {
                int offset = candidateSuit * 9;
                int total = 0;
                bool valid = true;
                for (int tile = 0; tile < 27; tile++)
                {
                    if (tile / 9 != candidateSuit && counts[tile] > 0) valid = false;
                    if (tile / 9 == candidateSuit) total += counts[tile];
                }
                if (!valid || total != 14) continue;
                if (counts[offset] < 3 || counts[offset + 8] < 3) continue;
                if (Enumerable.Range(1, 7).Any(rank => counts[offset + rank] < 1)) continue;
                suit = candidateSuit;
                return true;
            }
            return false;
        }

        private static bool IsPureChuurenWait(int[] counts, int winningTile, int suit)
        {
            if (suit < 0 || winningTile < suit * 9 || winningTile >= suit * 9 + 9 || counts[winningTile] <= 0) return false;
            int[] beforeWin = (int[])counts.Clone();
            beforeWin[winningTile]--;
            int offset = suit * 9;
            for (int rank = 0; rank < 9; rank++)
            {
                int expected = rank == 0 || rank == 8 ? 3 : 1;
                if (beforeWin[offset + rank] != expected) return false;
            }
            return beforeWin.Sum() == 13;
        }
    }
}
