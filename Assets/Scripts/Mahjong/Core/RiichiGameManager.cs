using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UpgradeMahjong.Mahjong
{
    public enum RiichiAction
    {
        Pass,
        Chi,
        Pon,
        OpenKan,
        ClosedKan,
        AddedKan,
        Riichi,
        Ron,
        Tsumo
    }

    public enum RiichiPhase
    {
        WaitingForDiscard,
        WaitingForReaction,
        HandWon,
        ExhaustiveDraw
    }

    [Serializable]
    public sealed class RiichiPlayerState
    {
        public string Nickname;
        public int Score = 25000;
        public int SeatWind;
        public bool IsDealer;
        public bool RiichiDeclared;
        public bool IppatsuEligible;
        public bool RiichiDeclarationDiscardPending;
        public int RiichiDiscardIndex = -1;
        public readonly int[] ConcealedCounts = new int[MahjongTiles.TileTypeCount];
        public readonly List<MahjongTileData> ConcealedTiles = new List<MahjongTileData>();
        public readonly List<MahjongMeld> OpenMelds = new List<MahjongMeld>();
        public readonly List<MahjongDiscard> River = new List<MahjongDiscard>();

        public RiichiPlayerState(string nickname, int seatWind, bool isDealer)
        {
            Nickname = nickname;
            SeatWind = seatWind;
            IsDealer = isDealer;
        }

        public int ConcealedTileCount { get { return ConcealedCounts.Sum(); } }
    }

    public sealed class ActionPrompt
    {
        public int RespondingPlayer;
        public int Discarder;
        public int TileIndex;
        public float ExpiresAt;
        public readonly List<RiichiAction> Actions = new List<RiichiAction>();
    }

    public sealed class RiichiGameManager : MonoBehaviour
    {
        [Header("Round")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private int startingScore = 25000;
        [SerializeField] private float reactionSeconds = 7f;
        [SerializeField] private int deadWallSize = 14;
        [SerializeField] private RiichiRuleOptions ruleOptions = new RiichiRuleOptions();

        private readonly List<MahjongTileData> liveWall = new List<MahjongTileData>();
        private readonly List<MahjongTileData> deadWall = new List<MahjongTileData>();
        private readonly List<RiichiPlayerState> players = new List<RiichiPlayerState>();
        private ActionPrompt currentPrompt;
        private int doraRevealCount;
        private int currentPlayer;
        private int lastDiscarder = -1;
        private int lastDiscardTile = -1;

        public IReadOnlyList<RiichiPlayerState> Players { get { return players; } }
        public IReadOnlyList<MahjongTileData> LiveWall { get { return liveWall; } }
        public IReadOnlyList<MahjongTileData> DeadWall { get { return deadWall; } }
        public RiichiPhase Phase { get; private set; }
        public int CurrentPlayer { get { return currentPlayer; } }
        public ActionPrompt CurrentPrompt { get { return currentPrompt; } }
        public readonly List<int> DoraIndicators = new List<int>();
        public event Action<ActionPrompt> ActionPromptChanged;
        public event Action<RiichiPhase> PhaseChanged;
        public event Action<string, ScoreBreakdown> HandWon;

        private void Start()
        {
            if (autoStart) StartRound();
        }

        private void Update()
        {
            if (currentPrompt == null || Time.time < currentPrompt.ExpiresAt) return;
            ResolveAction(RiichiAction.Pass);
        }

        public void StartRound()
        {
            liveWall.Clear();
            deadWall.Clear();
            players.Clear();
            DoraIndicators.Clear();
            doraRevealCount = 1;
            currentPlayer = 0;
            lastDiscarder = -1;
            lastDiscardTile = -1;
            currentPrompt = null;

            BuildWall();
            for (int seat = 0; seat < 4; seat++)
                players.Add(new RiichiPlayerState("Player " + seat, 27 + seat, seat == 0) { Score = startingScore });

            for (int i = 0; i < 13; i++)
                for (int seat = 0; seat < players.Count; seat++) DrawToPlayer(seat, false);
            DrawToPlayer(0, false);

            if (deadWall.Count > 4)
                DoraIndicators.Add(deadWall[4].Index);
            SetPhase(RiichiPhase.WaitingForDiscard);
        }

        private void BuildWall()
        {
            int physicalId = 0;
            for (int tile = 0; tile < MahjongTiles.TileTypeCount; tile++)
            {
                int copies = 4;
                for (int copy = 0; copy < copies; copy++)
                {
                    bool red = MahjongTiles.IsRedFive(tile) && copy == 0;
                    liveWall.Add(new MahjongTileData(tile, physicalId++, red));
                }
            }

            System.Random random = new System.Random();
            for (int i = liveWall.Count - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                MahjongTileData temp = liveWall[i];
                liveWall[i] = liveWall[swap];
                liveWall[swap] = temp;
            }

            int reserve = Math.Min(deadWallSize, liveWall.Count);
            deadWall.AddRange(liveWall.GetRange(liveWall.Count - reserve, reserve));
            liveWall.RemoveRange(liveWall.Count - reserve, reserve);
        }

        private bool DrawToPlayer(int playerIndex, bool supplemental)
        {
            if (playerIndex < 0 || playerIndex >= players.Count) return false;
            MahjongTileData tile;
            if (supplemental)
            {
                if (deadWall.Count <= 0) return false;
                tile = deadWall[deadWall.Count - 1];
                deadWall.RemoveAt(deadWall.Count - 1);
            }
            else
            {
                if (liveWall.Count == 0) return false;
                tile = liveWall[0];
                liveWall.RemoveAt(0);
            }

            RiichiPlayerState player = players[playerIndex];
            player.ConcealedTiles.Add(tile);
            player.ConcealedCounts[tile.Index]++;
            return true;
        }

        public bool TryDiscard(int playerIndex, int tileIndex)
        {
            if (Phase != RiichiPhase.WaitingForDiscard || playerIndex != currentPlayer) return false;
            RiichiPlayerState player = players[playerIndex];
            if (!RemoveTile(player, tileIndex, out MahjongTileData physicalTile)) return false;

            MahjongDiscard discard = new MahjongDiscard(tileIndex) { IsRed = physicalTile.IsRed };
            player.River.Add(discard);
            if (player.RiichiDeclared)
            {
                if (player.RiichiDeclarationDiscardPending)
                    player.RiichiDeclarationDiscardPending = false;
                else
                    player.IppatsuEligible = false;
            }
            lastDiscarder = playerIndex;
            lastDiscardTile = tileIndex;

            ActionPrompt prompt = BuildReactionPrompt(playerIndex, tileIndex);
            if (prompt.Actions.Count > 0)
            {
                currentPrompt = prompt;
                SetPhase(RiichiPhase.WaitingForReaction);
                ActionPromptChanged?.Invoke(currentPrompt);
                return true;
            }

            AdvanceTurnAfterPass(playerIndex);
            return true;
        }

        private ActionPrompt BuildReactionPrompt(int discarder, int tileIndex)
        {
            int responder = (discarder + 1) % 4;
            RiichiPlayerState player = players[responder];
            ActionPrompt prompt = new ActionPrompt
            {
                RespondingPlayer = responder,
                Discarder = discarder,
                TileIndex = tileIndex,
                ExpiresAt = Time.time + reactionSeconds
            };

            if (CanRon(player, tileIndex)) prompt.Actions.Add(RiichiAction.Ron);
            if (CanChi(player, discarder, tileIndex)) prompt.Actions.Add(RiichiAction.Chi);
            if (CanPon(player, tileIndex)) prompt.Actions.Add(RiichiAction.Pon);
            if (CanOpenKan(player, tileIndex)) prompt.Actions.Add(RiichiAction.OpenKan);
            if (prompt.Actions.Count > 0) prompt.Actions.Add(RiichiAction.Pass);
            return prompt;
        }

        public bool ResolveAction(RiichiAction action)
        {
            if (currentPrompt == null || !currentPrompt.Actions.Contains(action)) return false;
            ActionPrompt prompt = currentPrompt;
            currentPrompt = null;

            if (action == RiichiAction.Ron)
            {
                ScoreBreakdown score = CalculateWinScore(players[prompt.RespondingPlayer], prompt.TileIndex, MahjongWinMethod.Ron);
                SetPhase(RiichiPhase.HandWon);
                HandWon?.Invoke("론", score);
                ActionPromptChanged?.Invoke(null);
                return true;
            }

            if (action == RiichiAction.Tsumo)
            {
                ScoreBreakdown score = CalculateWinScore(players[prompt.RespondingPlayer], -1, MahjongWinMethod.Tsumo);
                SetPhase(RiichiPhase.HandWon);
                HandWon?.Invoke("쯔모", score);
                ActionPromptChanged?.Invoke(null);
                return true;
            }

            if (action == RiichiAction.Pass)
            {
                if (prompt.Discarder < 0)
                {
                    SetPhase(RiichiPhase.WaitingForDiscard);
                    ActionPromptChanged?.Invoke(null);
                    return true;
                }
                AdvanceTurnAfterPass(prompt.Discarder);
                ActionPromptChanged?.Invoke(null);
                return true;
            }

            RiichiPlayerState player = players[prompt.RespondingPlayer];
            if (!TryCall(player, prompt, action)) return false;
            currentPlayer = prompt.RespondingPlayer;
            SetPhase(RiichiPhase.WaitingForDiscard);
            ActionPromptChanged?.Invoke(null);
            return true;
        }

        private void AdvanceTurnAfterPass(int discarder)
        {
            if (liveWall.Count == 0)
            {
                SetPhase(RiichiPhase.ExhaustiveDraw);
                return;
            }
            currentPlayer = (discarder + 1) % 4;
            DrawToPlayer(currentPlayer, false);

            if (CanTsumo(players[currentPlayer]))
            {
                currentPrompt = new ActionPrompt
                {
                    RespondingPlayer = currentPlayer,
                    Discarder = -1,
                    TileIndex = -1,
                    ExpiresAt = Time.time + reactionSeconds
                };
                currentPrompt.Actions.Add(RiichiAction.Tsumo);
                currentPrompt.Actions.Add(RiichiAction.Pass);
                SetPhase(RiichiPhase.WaitingForReaction);
                ActionPromptChanged?.Invoke(currentPrompt);
                return;
            }
            SetPhase(RiichiPhase.WaitingForDiscard);
        }

        public bool CanRon(RiichiPlayerState player, int tileIndex)
        {
            int[] candidate = (int[])player.ConcealedCounts.Clone();
            if (candidate[tileIndex] >= 4) return false;
            candidate[tileIndex]++;
            MahjongGameContext context = CreateGameContext(player, MahjongWinMethod.Ron, tileIndex);
            return ScoreCalculator.CanWin(candidate, player.OpenMelds, context);
        }

        public bool CanTsumo(RiichiPlayerState player)
        {
            int winningTile = player.ConcealedTiles.Count == 0
                ? -1
                : player.ConcealedTiles[player.ConcealedTiles.Count - 1].Index;
            MahjongGameContext context = CreateGameContext(player, MahjongWinMethod.Tsumo, winningTile);
            return ScoreCalculator.CanWin(player.ConcealedCounts, player.OpenMelds, context);
        }

        private ScoreBreakdown CalculateWinScore(RiichiPlayerState player, int winningTile, MahjongWinMethod winMethod)
        {
            int[] counts = (int[])player.ConcealedCounts.Clone();
            if (winningTile >= 0) counts[winningTile]++;
            int resolvedWinningTile = winningTile >= 0
                ? winningTile
                : player.ConcealedTiles.Count == 0
                    ? -1
                    : player.ConcealedTiles[player.ConcealedTiles.Count - 1].Index;
            MahjongGameContext context = CreateGameContext(player, winMethod, resolvedWinningTile);
            return ScoreCalculator.FindBestScore(counts, player.OpenMelds, context);
        }

        private MahjongGameContext CreateGameContext(RiichiPlayerState player, MahjongWinMethod winMethod, int winningTile)
        {
            MahjongGameContext context = new MahjongGameContext
            {
                WinMethod = winMethod,
                WinningTileIndex = winningTile,
                IsDealer = player.IsDealer,
                SeatWind = player.SeatWind,
                RoundWind = 27,
                Riichi = player.RiichiDeclared,
                Ippatsu = player.RiichiDeclared && player.IppatsuEligible,
                RedDoraCount = player.ConcealedTiles.Count(tile => tile.IsRed),
                Rules = ruleOptions
            };
            context.DoraIndicators.AddRange(DoraIndicators);
            return context;
        }

        private static int GetScoreValue(ScoreBreakdown score)
        {
            return score == null ? 0 : score.TotalPoints;
        }

        public bool IsTenpai(RiichiPlayerState player)
        {
            return HandEvaluator.CalculateShanten(player.ConcealedCounts, player.OpenMelds.Count) == 0;
        }

        public List<int> GetWaitingTiles(RiichiPlayerState player)
        {
            return HandEvaluator.FindWinningTiles(player.ConcealedCounts, player.OpenMelds);
        }

        public List<int> GetRiichiDiscardOptions(RiichiPlayerState player)
        {
            List<int> result = new List<int>();
            if (player.RiichiDeclared || player.OpenMelds.Count > 0 || player.Score < 1000) return result;
            for (int tile = 0; tile < MahjongTiles.TileTypeCount; tile++)
            {
                if (player.ConcealedCounts[tile] == 0) continue;
                int[] afterDiscard = (int[])player.ConcealedCounts.Clone();
                afterDiscard[tile]--;
                if (HandEvaluator.FindWinningTiles(afterDiscard, player.OpenMelds).Count > 0) result.Add(tile);
            }
            return result;
        }

        public bool TryDeclareRiichi(RiichiPlayerState player, int discardIndex)
        {
            if (!GetRiichiDiscardOptions(player).Contains(discardIndex)) return false;
            player.RiichiDeclared = true;
            player.IppatsuEligible = true;
            player.RiichiDeclarationDiscardPending = true;
            player.RiichiDiscardIndex = discardIndex;
            player.Score -= 1000;
            return true;
        }

        public void RevealDoraForKan()
        {
            CancelAllIppatsu();
            int indicatorSlot = 4 + doraRevealCount * 2;
            if (indicatorSlot >= 0 && indicatorSlot < deadWall.Count)
            {
                DoraIndicators.Add(deadWall[indicatorSlot].Index);
                doraRevealCount++;
            }
        }

        private bool TryCall(RiichiPlayerState player, ActionPrompt prompt, RiichiAction action)
        {
            int tile = prompt.TileIndex;
            if (action == RiichiAction.Pon && !CanPon(player, tile)) return false;
            if (action == RiichiAction.OpenKan && !CanOpenKan(player, tile)) return false;
            if (action == RiichiAction.Chi && !CanChi(player, prompt.Discarder, tile)) return false;

            CancelAllIppatsu();

            if (action == RiichiAction.Pon)
            {
                RemoveCopies(player, tile, 2);
                player.OpenMelds.Add(new MahjongMeld(MahjongMeldType.Triplet, new[] { tile, tile, tile }, true, tile, prompt.Discarder));
            }
            else if (action == RiichiAction.OpenKan)
            {
                RemoveCopies(player, tile, 3);
                player.OpenMelds.Add(new MahjongMeld(MahjongMeldType.OpenKan, new[] { tile, tile, tile, tile }, true, tile, prompt.Discarder));
                RevealDoraForKan();
            }
            else if (action == RiichiAction.Chi)
            {
                int[] sequence = FindChi(player, prompt.Discarder, tile);
                if (sequence == null) return false;
                RemoveCopies(player, sequence[0], 1);
                RemoveCopies(player, sequence[1], 1);
                player.OpenMelds.Add(new MahjongMeld(MahjongMeldType.Sequence, new[] { sequence[0], tile, sequence[1] }, true, tile, prompt.Discarder));
            }
            return true;
        }

        private void CancelAllIppatsu()
        {
            foreach (RiichiPlayerState player in players)
                player.IppatsuEligible = false;
        }

        private bool CanPon(RiichiPlayerState player, int tile) { return player.ConcealedCounts[tile] >= 2; }
        private bool CanOpenKan(RiichiPlayerState player, int tile) { return player.ConcealedCounts[tile] >= 3; }

        private bool CanChi(RiichiPlayerState player, int discarder, int tile)
        {
            return discarder == (player.SeatWind - 27 + 3) % 4 && FindChi(player, discarder, tile) != null;
        }

        private int[] FindChi(RiichiPlayerState player, int discarder, int tile)
        {
            if (discarder != (player.SeatWind - 27 + 3) % 4 || MahjongTiles.IsHonor(tile)) return null;
            int rank = MahjongTiles.GetRank(tile);
            int suitOffset = tile - (tile % 9);
            int[][] candidates = { new[] { rank - 2, rank - 1 }, new[] { rank - 1, rank + 1 }, new[] { rank + 1, rank + 2 } };
            foreach (int[] candidate in candidates)
            {
                if (candidate[0] < 1 || candidate[1] > 9) continue;
                int first = suitOffset + candidate[0] - 1;
                int second = suitOffset + candidate[1] - 1;
                if (player.ConcealedCounts[first] > 0 && player.ConcealedCounts[second] > 0) return new[] { first, second };
            }
            return null;
        }

        private static bool RemoveTile(RiichiPlayerState player, int tileIndex, out MahjongTileData physicalTile)
        {
            physicalTile = default(MahjongTileData);
            if (tileIndex < 0 || tileIndex >= MahjongTiles.TileTypeCount || player.ConcealedCounts[tileIndex] <= 0) return false;
            for (int i = 0; i < player.ConcealedTiles.Count; i++)
            {
                if (player.ConcealedTiles[i].Index != tileIndex) continue;
                physicalTile = player.ConcealedTiles[i];
                player.ConcealedTiles.RemoveAt(i);
                player.ConcealedCounts[tileIndex]--;
                return true;
            }
            return false;
        }

        private static bool RemoveCopies(RiichiPlayerState player, int tileIndex, int amount)
        {
            for (int i = 0; i < amount; i++)
                if (!RemoveTile(player, tileIndex, out _)) return false;
            return true;
        }

        private void SetPhase(RiichiPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }
    }
}
