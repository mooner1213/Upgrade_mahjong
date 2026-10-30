using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UpgradeMahjong.Mahjong
{
    public sealed class MahjongTileView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private RectTransform tileRect;
        [SerializeField] private float hoverOffset = 24f;
        private Vector2 basePosition;

        public int TileIndex { get; private set; } = -1;
        public bool IsRed { get; private set; }
        public bool IsRiichiTile { get; private set; }

        public void Bind(MahjongTileData tile)
        {
            TileIndex = tile.Index;
            IsRed = tile.IsRed;
            IsRiichiTile = false;
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            if (tileRect == null) tileRect = transform as RectTransform;
            basePosition = position;
            tileRect.anchoredPosition = position;
        }

        public void SetRiichiTile(bool value)
        {
            IsRiichiTile = value;
            transform.localRotation = value ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
        }

        public void SetCalledAway(bool value)
        {
            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = value ? 0.35f : 1f;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tileRect == null) tileRect = transform as RectTransform;
            tileRect.anchoredPosition = basePosition + Vector2.up * hoverOffset;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tileRect == null) tileRect = transform as RectTransform;
            tileRect.anchoredPosition = basePosition;
        }
    }

    public sealed class MahjongHandLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform concealedRoot;
        [SerializeField] private RectTransform tsumoRoot;
        [SerializeField] private RectTransform openRoot;
        [SerializeField] private float tileWidth = 80f;
        [SerializeField] private float tileHeight = 112f;
        [SerializeField] private float tsumoGapRatio = 0.4f;
        [SerializeField] private float openMeldGap = 24f;

        public void LayoutHand(IList<MahjongTileView> concealed, MahjongTileView tsumo, IList<MahjongTileView> openTiles)
        {
            LayoutConcealed(concealed);
            LayoutTsumo(tsumo, concealed == null ? 0 : concealed.Count);
            LayoutOpen(openTiles, concealed == null ? 0 : concealed.Count);
        }

        private void LayoutConcealed(IList<MahjongTileView> tiles)
        {
            if (concealedRoot == null || tiles == null) return;
            float totalWidth = Mathf.Max(0f, (tiles.Count - 1) * tileWidth);
            float startX = -totalWidth * 0.5f;
            for (int i = 0; i < tiles.Count; i++)
            {
                MahjongTileView view = tiles[i];
                view.transform.SetParent(concealedRoot, false);
                view.SetAnchoredPosition(new Vector2(startX + i * tileWidth, 0f));
            }
        }

        private void LayoutTsumo(MahjongTileView tsumo, int concealedCount)
        {
            if (tsumoRoot == null || tsumo == null) return;
            float totalWidth = Mathf.Max(0f, (concealedCount - 1) * tileWidth);
            tsumo.transform.SetParent(tsumoRoot, false);
            tsumo.SetAnchoredPosition(new Vector2(totalWidth * 0.5f + tileWidth * (0.5f + tsumoGapRatio), 0f));
        }

        private void LayoutOpen(IList<MahjongTileView> tiles, int concealedCount)
        {
            if (openRoot == null || tiles == null) return;
            float startX = Mathf.Max(0f, concealedCount * tileWidth + openMeldGap);
            for (int i = 0; i < tiles.Count; i++)
            {
                MahjongTileView view = tiles[i];
                view.transform.SetParent(openRoot, false);
                view.SetAnchoredPosition(new Vector2(startX + i * tileWidth, 0f));
            }
        }
    }

    public sealed class MahjongRiverLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform riverRoot;
        [SerializeField] private int columns = 6;
        [SerializeField] private int rows = 3;
        [SerializeField] private float tileWidth = 56f;
        [SerializeField] private float tileHeight = 78f;
        [SerializeField] private float spacing = 3f;

        public void LayoutRiver(IList<MahjongTileView> tiles, IList<MahjongDiscard> discards)
        {
            if (riverRoot == null || tiles == null) return;
            int limit = Mathf.Min(tiles.Count, columns * rows);
            float cellWidth = tileWidth + spacing;
            float cellHeight = tileHeight + spacing;
            float originX = -((columns - 1) * cellWidth) * 0.5f;
            float originY = ((rows - 1) * cellHeight) * 0.5f;

            for (int i = 0; i < limit; i++)
            {
                MahjongTileView view = tiles[i];
                view.transform.SetParent(riverRoot, false);
                int row = i / columns;
                int column = i % columns;
                view.SetAnchoredPosition(new Vector2(originX + column * cellWidth, originY - row * cellHeight));
                view.SetRiichiTile(discards != null && i < discards.Count && discards[i].IsRiichiTile);
                view.SetCalledAway(discards != null && i < discards.Count && discards[i].IsCalledAway);
            }
        }
    }

    public sealed class MahjongMeldLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private float tileWidth = 62f;
        [SerializeField] private float meldGap = 18f;

        public void LayoutMeld(MahjongMeld meld, IList<MahjongTileView> views)
        {
            if (root == null || meld == null || views == null) return;
            for (int i = 0; i < views.Count; i++)
            {
                MahjongTileView view = views[i];
                view.transform.SetParent(root, false);
                float x = i * tileWidth + (i == 0 ? 0f : meldGap);
                view.SetAnchoredPosition(new Vector2(x, 0f));
                view.SetRiichiTile(meld.CalledTileIndex >= 0 && view.TileIndex == meld.CalledTileIndex && i == 0);
            }
        }
    }
}
