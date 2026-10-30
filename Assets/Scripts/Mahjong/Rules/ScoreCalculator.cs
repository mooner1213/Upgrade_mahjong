using System;
using System.Collections.Generic;

namespace UpgradeMahjong.Mahjong
{
    public static class DoraCalculator
    {
        public static int CountDora(HandCombination hand, MahjongGameContext context)
        {
            int[] counts = hand.GetAllTileCounts();
            int result = context.RedDoraCount;
            foreach (int indicator in context.DoraIndicators)
            {
                int dora = MahjongTiles.NextDora(indicator);
                result += counts[dora];
            }
            if (context.Riichi || context.DoubleRiichi)
            {
                foreach (int indicator in context.UraDoraIndicators)
                    result += counts[MahjongTiles.NextDora(indicator)];
            }
            return result;
        }
    }

    public static class FuCalculator
    {
        public static int Calculate(HandCombination hand, MahjongGameContext context, YakuResult yaku)
        {
            if (hand.IsKokushi || yaku.YakumanCount > 0) return 0;
            if (hand.IsChiitoitsu) return 25;

            bool pinfu = yaku.Contains("평화");
            if (pinfu && context.WinMethod == MahjongWinMethod.Tsumo) return 20;

            int fu = 20;
            if (context.WinMethod == MahjongWinMethod.Ron && hand.IsClosed) fu += 10;
            if (context.WinMethod == MahjongWinMethod.Tsumo) fu += 2;

            if (hand.PairIndex >= 31) fu += 2;
            if (hand.PairIndex == context.SeatWind) fu += 2;
            if (hand.PairIndex == context.RoundWind) fu += 2;

            for (int meldIndex = 0; meldIndex < hand.Melds.Count; meldIndex++)
            {
                MahjongMeld meld = hand.Melds[meldIndex];
                if (meld.Type == MahjongMeldType.Sequence) continue;

                int tile = meld.Tiles[0];
                bool terminalOrHonor = MahjongTiles.IsTerminalOrHonor(tile);
                bool completedByRon = context.WinMethod == MahjongWinMethod.Ron
                    && meldIndex == hand.WinningMeldIndex
                    && hand.WaitType == MahjongWaitType.Shanpon;
                bool closed = !meld.IsOpen && !completedByRon;

                if (meld.Type == MahjongMeldType.ClosedKan)
                    fu += terminalOrHonor ? 32 : 16;
                else if (meld.Type == MahjongMeldType.OpenKan || meld.Type == MahjongMeldType.AddedKan)
                    fu += terminalOrHonor ? 16 : 8;
                else if (closed)
                    fu += terminalOrHonor ? 8 : 4;
                else
                    fu += terminalOrHonor ? 4 : 2;
            }

            MahjongWaitType waitType = context.WaitType != MahjongWaitType.Unknown ? context.WaitType : hand.WaitType;
            if (waitType == MahjongWaitType.Kanchan || waitType == MahjongWaitType.Penchan || waitType == MahjongWaitType.Tanki)
                fu += 2;

            if (fu == 20 && context.WinMethod == MahjongWinMethod.Ron) fu = 30;
            return RoundUpToTen(fu);
        }

        private static int RoundUpToTen(int value)
        {
            return ((value + 9) / 10) * 10;
        }
    }

    public sealed class ScoreBreakdown
    {
        public int Han;
        public int Fu;
        public int DoraHan;
        public int YakumanCount;
        public int BasePoints;
        public int RonPoints;
        public int TsumoDealerPayment;
        public int TsumoNonDealerPayment;
        public int BonusPoints;
        public string LimitName;
        public YakuResult Yaku;
        public HandCombination Combination;
        public bool HasYaku { get { return Yaku != null && Yaku.HasYaku; } }

        public int TotalPoints
        {
            get
            {
                if (RonPoints > 0) return RonPoints + BonusPoints;
                if (TsumoNonDealerPayment > 0) return TsumoDealerPayment + TsumoNonDealerPayment * 2 + BonusPoints;
                return TsumoDealerPayment * 3 + BonusPoints;
            }
        }
    }

    public static class ScoreCalculator
    {
        public static ScoreBreakdown FindBestScore(
            int[] concealedCounts,
            IList<MahjongMeld> fixedMelds,
            MahjongGameContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            List<HandCombination> combinations = HandEvaluator.FindAllCombinations(concealedCounts, fixedMelds);
            ScoreBreakdown best = null;

            foreach (HandCombination combination in combinations)
            {
                foreach (HandCombination interpretation in HandEvaluator.ExpandWinningInterpretations(combination, context.WinningTileIndex))
                {
                    YakuResult yaku = YakuEvaluator.Evaluate(interpretation, context);
                    if (!yaku.HasYaku) continue;
                    ScoreBreakdown candidate = Calculate(interpretation, context, yaku);
                    if (best == null || candidate.TotalPoints > best.TotalPoints
                        || candidate.TotalPoints == best.TotalPoints && candidate.Han > best.Han
                        || candidate.TotalPoints == best.TotalPoints && candidate.Han == best.Han && candidate.Fu > best.Fu)
                    {
                        best = candidate;
                    }
                }
            }

            return best;
        }

        public static bool CanWin(int[] concealedCounts, IList<MahjongMeld> fixedMelds, MahjongGameContext context)
        {
            return FindBestScore(concealedCounts, fixedMelds, context) != null;
        }

        public static ScoreBreakdown Calculate(HandCombination hand, MahjongGameContext context, YakuResult yaku)
        {
            if (hand == null) throw new ArgumentNullException(nameof(hand));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (yaku == null) throw new ArgumentNullException(nameof(yaku));

            ScoreBreakdown result = new ScoreBreakdown
            {
                Combination = hand,
                Yaku = yaku,
                YakumanCount = yaku.YakumanCount,
                BonusPoints = context.RiichiSticks * 1000
            };

            if (!yaku.HasYaku)
            {
                result.LimitName = "역 없음";
                return result;
            }

            if (yaku.YakumanCount > 0)
            {
                result.Han = 0;
                result.Fu = 0;
                result.BasePoints = 8000 * yaku.YakumanCount;
                result.LimitName = yaku.YakumanCount == 1 ? "역만" : yaku.YakumanCount + "배 역만";
            }
            else
            {
                result.DoraHan = DoraCalculator.CountDora(hand, context);
                result.Han = yaku.Han + result.DoraHan;
                result.Fu = FuCalculator.Calculate(hand, context, yaku);
                result.BasePoints = CalculateBasePoints(result.Han, result.Fu, context.Rules, out string limitName);
                result.LimitName = limitName;
            }

            if (context.WinMethod == MahjongWinMethod.Ron)
            {
                result.RonPoints = RoundUpToHundred(result.BasePoints * (context.IsDealer ? 6 : 4)) + context.Honba * 300;
            }
            else if (context.IsDealer)
            {
                result.TsumoDealerPayment = RoundUpToHundred(result.BasePoints * 2) + context.Honba * 100;
            }
            else
            {
                result.TsumoDealerPayment = RoundUpToHundred(result.BasePoints * 2) + context.Honba * 100;
                result.TsumoNonDealerPayment = RoundUpToHundred(result.BasePoints) + context.Honba * 100;
            }
            return result;
        }

        private static int CalculateBasePoints(int han, int fu, RiichiRuleOptions options, out string limitName)
        {
            options = options ?? new RiichiRuleOptions();
            if (han >= 13 && options.EnableKazoeYakuman) { limitName = "카조에 역만"; return 8000; }
            if (han >= 11) { limitName = "삼배만"; return 6000; }
            if (han >= 8) { limitName = "배만"; return 4000; }
            if (han >= 6) { limitName = "하네만"; return 3000; }

            bool standardMangan = han >= 5 || han == 4 && fu >= 40 || han == 3 && fu >= 70;
            bool kiriageMangan = options.EnableKiriageMangan && (han == 4 && fu == 30 || han == 3 && fu == 60);
            if (standardMangan || kiriageMangan) { limitName = "만관"; return 2000; }

            limitName = string.Empty;
            long basePoints = (long)fu * (1L << (han + 2));
            return (int)Math.Min(2000L, basePoints);
        }

        private static int RoundUpToHundred(int value)
        {
            return ((value + 99) / 100) * 100;
        }
    }
}
