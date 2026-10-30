using System;

namespace UpgradeMahjong.Mahjong
{
    public enum MahjongSuit
    {
        Man,
        Pin,
        Sou,
        Honor
    }

    public enum MahjongMeldType
    {
        Sequence,
        Triplet,
        OpenKan,
        ClosedKan,
        AddedKan
    }

    public static class MahjongTiles
    {
        public const int TileTypeCount = 34;
        public const int PhysicalTileCount = 136;

        public static MahjongSuit GetSuit(int index)
        {
            if (index < 0 || index >= TileTypeCount) throw new ArgumentOutOfRangeException(nameof(index));
            return index < 9 ? MahjongSuit.Man : index < 18 ? MahjongSuit.Pin : index < 27 ? MahjongSuit.Sou : MahjongSuit.Honor;
        }

        public static int GetRank(int index)
        {
            if (index < 0 || index >= TileTypeCount) throw new ArgumentOutOfRangeException(nameof(index));
            return index < 27 ? index % 9 + 1 : index - 26;
        }

        public static int ToIndex(MahjongSuit suit, int rank)
        {
            if (rank < 1 || rank > (suit == MahjongSuit.Honor ? 7 : 9)) throw new ArgumentOutOfRangeException(nameof(rank));
            if (suit == MahjongSuit.Man) return rank - 1;
            if (suit == MahjongSuit.Pin) return 9 + rank - 1;
            if (suit == MahjongSuit.Sou) return 18 + rank - 1;
            return 27 + rank - 1;
        }

        public static bool IsHonor(int index) { return index >= 27; }

        public static bool IsTerminal(int index)
        {
            return index < 27 && index % 9 is 0 or 8;
        }

        public static bool IsTerminalOrHonor(int index) { return IsHonor(index) || IsTerminal(index); }

        public static bool IsSimple(int index) { return index >= 0 && index < 27 && index % 9 >= 1 && index % 9 <= 7; }

        public static bool IsValueHonor(int index, int roundWind, int seatWind)
        {
            return index >= 31 || index == roundWind || index == seatWind;
        }

        public static int NextDora(int indicatorIndex)
        {
            if (indicatorIndex < 0 || indicatorIndex >= TileTypeCount) throw new ArgumentOutOfRangeException(nameof(indicatorIndex));
            if (indicatorIndex < 27) return indicatorIndex % 9 == 8 ? indicatorIndex - 8 : indicatorIndex + 1;
            if (indicatorIndex <= 30) return indicatorIndex == 30 ? 27 : indicatorIndex + 1;
            return indicatorIndex == 33 ? 31 : indicatorIndex + 1;
        }

        public static bool IsRedFive(int index) { return index == 4 || index == 13 || index == 22; }
    }

    [Serializable]
    public struct MahjongTileData
    {
        public int Index;
        public int PhysicalId;
        public bool IsRed;

        public MahjongTileData(int index, int physicalId, bool isRed)
        {
            Index = index;
            PhysicalId = physicalId;
            IsRed = isRed;
        }
    }

    [Serializable]
    public sealed class MahjongMeld
    {
        public MahjongMeldType Type;
        public int[] Tiles;
        public bool IsOpen;
        public int CalledTileIndex = -1;
        public int FromSeat = -1;

        public int TileCount { get { return Tiles == null ? 0 : Tiles.Length; } }

        public MahjongMeld(MahjongMeldType type, int[] tiles, bool isOpen, int calledTileIndex = -1, int fromSeat = -1)
        {
            Type = type;
            Tiles = tiles ?? Array.Empty<int>();
            IsOpen = isOpen;
            CalledTileIndex = calledTileIndex;
            FromSeat = fromSeat;
        }
    }

    [Serializable]
    public sealed class MahjongDiscard
    {
        public int TileIndex;
        public bool IsRiichiTile;
        public bool IsCalledAway;
        public bool IsRed;

        public MahjongDiscard(int tileIndex)
        {
            TileIndex = tileIndex;
        }
    }
}
