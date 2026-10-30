using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace UpgradeMahjong
{
    /// <summary>
    /// First playable 4-player mahjong vertical slice.
    /// The view and action layout are intentionally data-driven so the same UI can later
    /// host 3-player rules and augment actions without changing the table presentation.
    /// </summary>
    public sealed class UpgradeMahjongPrototype : MonoBehaviour
    {
        private enum Suit { Man, Pin, Sou, Honor }
        private enum ActionType { Pass, Chi, Pon, Kan, Riichi, Ron, Tsumo, Kita, AbortiveDraw }
        private enum KanChoiceKind { None, Daiminkan, Ankan, Kakan }

        [Serializable]
        private sealed class Tile
        {
            public Suit suit;
            public int rank;
            public int id;

            public string Label
            {
                get
                {
                    if (suit == Suit.Man) return rank + "만";
                    if (suit == Suit.Pin) return rank + "\uD1B5";
                    if (suit == Suit.Sou) return rank + "삭";
                    string[] honors = { "동", "남", "서", "북", "백", "발", "중" };
                    return honors[Mathf.Clamp(rank - 1, 0, honors.Length - 1)];
                }
            }
        }

        private sealed class MeldState
        {
            public UpgradeMahjong.Mahjong.MahjongMeldType Type;
            public readonly List<Tile> Tiles = new List<Tile>();
            public int CalledFromSeat = -1;
            public int CalledTileIndex = -1;
            public int AddedTileIndex = -1;
            public bool IsOpen { get { return Type != UpgradeMahjong.Mahjong.MahjongMeldType.ClosedKan; } }

            public MeldState(
                UpgradeMahjong.Mahjong.MahjongMeldType type,
                IEnumerable<Tile> tiles,
                int calledFromSeat = -1,
                int calledTileIndex = -1)
            {
                Type = type;
                if (tiles != null) Tiles.AddRange(tiles);
                CalledFromSeat = calledFromSeat;
                CalledTileIndex = calledTileIndex;
            }
        }

        private sealed class AiMeldDecision
        {
            public int PlayerIndex;
            public ActionType Action;
            public Suit Suit;
            public int[] HandRanks = Array.Empty<int>();
            public int ResultingShanten;
        }

        [Serializable]
        private sealed class PlayerState
        {
            public string name;
            public int score = 25000;
            public int turnCount;
            public bool isHuman;
            public bool riichiDeclared;
            public bool ippatsuEligible;
            public int riichiDiscardIndex = -1;
            public int freshDrawId = -1;
            public bool freshDrawIsRinshan;
            public readonly List<Tile> hand = new List<Tile>();
            public readonly List<Tile> discards = new List<Tile>();
            public readonly List<MeldState> melds = new List<MeldState>();
        }

        private sealed class MeldChoice
        {
            public ActionType Action;
            public Suit Suit;
            public int[] HandRanks;
            public KanChoiceKind KanKind;
            public int TargetMeldIndex = -1;

            public MeldChoice(ActionType action, Suit suit, params int[] handRanks)
            {
                Action = action;
                Suit = suit;
                HandRanks = handRanks ?? Array.Empty<int>();
            }
        }

        private sealed class DiscardTileVisual
        {
            public int TileIndex;
            public Image Image;
            public Image MatchOverlay;
            public Outline Glow;
            public bool IsPendingDiscard;
        }

        private readonly struct BoardLayoutSnapshot
        {
            public readonly Vector2 Position;
            public readonly Vector2 Size;
            public readonly float Rotation;

            public BoardLayoutSnapshot(Vector2 position, Vector2 size, float rotation)
            {
                Position = position;
                Size = size;
                Rotation = rotation;
            }
        }

        [Serializable]
        private sealed class DiscardBoardLayout
        {
            [InspectorName("위치")]
            [Tooltip("해당 플레이어 기준 앵커에서 버림패 판 중심까지의 UI 좌표입니다.")]
            public Vector2 position;

            [InspectorName("크기")]
            [Tooltip("회전하기 전 버림패 판 이미지의 가로와 세로 크기입니다.")]
            public Vector2 size = new Vector2(780f, 260f);

            [InspectorName("회전 각도")]
            [Tooltip("버림패 판과 판 위의 패에 함께 적용되는 Z축 회전 각도입니다.")]
            [Range(-180f, 180f)]
            public float rotation;

            public DiscardBoardLayout(Vector2 position, Vector2 size, float rotation)
            {
                this.position = position;
                this.size = size;
                this.rotation = rotation;
            }
        }

        [Header("Table presentation")]
        [InspectorName("Scene UI 레이아웃")]
        [Tooltip("Scene에 저장된 MahjongSceneLayout입니다. 비어 있으면 기존 런타임 생성 방식을 사용합니다.")]
        [SerializeField] private MahjongSceneLayout sceneLayout;
        [InspectorName("3D 작탁 화면")]
        [Tooltip("작탁·마작패·리치봉 모델과 게임 상태를 연결하는 3D 표현 컴포넌트입니다.")]
        [SerializeField] private Mahjong3DTableView threeDimensionalView;
        [SerializeField] private float tableWidth = 12f;
        [SerializeField] private float tableDepth = 8f;
        [InspectorName("카메라 높이")]
        [SerializeField] private float cameraHeight = 11.4f;
        [InspectorName("카메라 거리")]
        [SerializeField] private float cameraDistance = 7.6f;
        [InspectorName("카메라 시야각")]
        [SerializeField] private float cameraFov = 39.5f;
        [InspectorName("카메라 주시점")]
        [SerializeField] private Vector3 cameraLookAt = new Vector3(0f, 0.05f, -0.75f);
        [SerializeField] private float tableSurfacePitch;

        [Header("Prototype flow")]
        [SerializeField] private bool autoStart = true;
        [InspectorName("AI 생각 시간")]
        [Tooltip("AI가 패를 가져온 뒤 버리기까지 기다리는 시간입니다.")]
        [Range(1f, 2f)]
        [SerializeField] private float aiTurnDelay = 1.4f;
        [InspectorName("타패 확인 시간")]
        [Tooltip("행동 가능한 선택지가 없을 때에도 방금 버린 패를 확인할 수 있도록 유지하는 시간입니다.")]
        [Range(0.2f, 0.8f)]
        [SerializeField] private float discardPresentationDelay = 0.4f;

        [Header("Riichi rules")]
        [SerializeField] private UpgradeMahjong.Mahjong.RiichiRuleOptions riichiRules =
            new UpgradeMahjong.Mahjong.RiichiRuleOptions();

        [Header("Dora indicator UI")]
        [InspectorName("도라 표시창 위치")]
        [Tooltip("화면 좌측 상단 모서리를 기준으로 한 도라 표시창 위치입니다.")]
        [SerializeField] private Vector2 doraPanelPosition = new Vector2(24f, -24f);
        [InspectorName("도라 표시창 크기")]
        [SerializeField] private Vector2 doraPanelSize = new Vector2(420f, 144f);

        [Header("UI typography")]
        [InspectorName("UI 글꼴")]
        [Tooltip("비워 두면 설치된 한글 산세리프 글꼴을 우선순위에 따라 자동으로 선택합니다.")]
        [SerializeField] private Font uiFont;

        [Header("Action button artwork")]
        [InspectorName("치 버튼 이미지")]
        [SerializeField] private Texture2D chiActionButtonTexture;
        [InspectorName("퐁 버튼 이미지")]
        [SerializeField] private Texture2D ponActionButtonTexture;
        [InspectorName("깡 버튼 이미지")]
        [SerializeField] private Texture2D kanActionButtonTexture;
        [InspectorName("패스 버튼 이미지")]
        [SerializeField] private Texture2D passActionButtonTexture;
        [InspectorName("론 버튼 이미지")]
        [SerializeField] private Texture2D ronActionButtonTexture;
        [InspectorName("쯔모 버튼 이미지")]
        [SerializeField] private Texture2D tsumoActionButtonTexture;
        [InspectorName("유국 버튼 이미지")]
        [SerializeField] private Texture2D abortiveDrawActionButtonTexture;

        [Header("Meld choice artwork")]
        [InspectorName("치 2가지 선택 배경")]
        [SerializeField] private Texture2D twoChoiceBackgroundTexture;
        [InspectorName("치 3가지 선택 배경")]
        [SerializeField] private Texture2D threeChoiceBackgroundTexture;

        [Header("Call announcement artwork")]
        [InspectorName("치·퐁·깡·론·쯔모 말풍선 시트")]
        [SerializeField] private Texture2D callBubbleSheetTexture;
        [InspectorName("말풍선 표시 시간")]
        [Range(0.8f, 3f)]
        [SerializeField] private float callBubbleDuration = 1.55f;

        [Header("Mahjong tile artwork")]
        [SerializeField] private Texture2D manTilesTexture;
        [InspectorName("1~9만 전용 이미지 (오름차순)")]
        [SerializeField] private Texture2D[] manTileTextures = new Texture2D[9];
        [SerializeField] private Texture2D pinTilesTexture;
        [InspectorName("1~9통 전용 이미지 (오름차순)")]
        [SerializeField] private Texture2D[] pinTileTextures = new Texture2D[9];
        [SerializeField] private Texture2D souTilesTexture;
        [InspectorName("1삭 전용 이미지")]
        [SerializeField] private Texture2D oneSouTexture;
        [InspectorName("2~9삭 전용 이미지 (오름차순)")]
        [SerializeField] private Texture2D[] souTileTextures = new Texture2D[8];
        [SerializeField] private Texture2D honorTilesTexture;
        [InspectorName("자패 전용 이미지 (동·남·서·북·백·발·중)")]
        [SerializeField] private Texture2D[] honorTileTextures = new Texture2D[7];

        [Header("작탁 위 버림패 영역")]
        [InspectorName("내 버림패 영역")]
        [SerializeField] private DiscardBoardLayout bottomDiscardBoardLayout =
            new DiscardBoardLayout(new Vector2(0f, 370f), new Vector2(430f, 175f), 0f);
        [InspectorName("상가 버림패 영역")]
        [SerializeField] private DiscardBoardLayout rightDiscardBoardLayout =
            new DiscardBoardLayout(new Vector2(-600f, 0f), new Vector2(430f, 175f), 90f);
        [InspectorName("대가 버림패 영역")]
        [SerializeField] private DiscardBoardLayout topDiscardBoardLayout =
            new DiscardBoardLayout(new Vector2(0f, -300f), new Vector2(430f, 175f), 180f);
        [InspectorName("하가 버림패 영역")]
        [SerializeField] private DiscardBoardLayout leftDiscardBoardLayout =
            new DiscardBoardLayout(new Vector2(600f, 0f), new Vector2(430f, 175f), -90f);

        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly List<Tile> wall = new List<Tile>();
        private readonly List<Tile> deadWall = new List<Tile>();
        private readonly List<Tile> doraIndicators = new List<Tile>();
        private readonly List<Button> handButtons = new List<Button>();
        private readonly List<GameObject> handGapObjects = new List<GameObject>();
        private readonly List<Button> actionButtons = new List<Button>();
        private readonly List<Button> meldChoiceButtons = new List<Button>();
        private readonly List<DiscardTileVisual> discardTileVisuals = new List<DiscardTileVisual>();
        private readonly List<DiscardTileVisual> doraTileVisuals = new List<DiscardTileVisual>();
        private readonly Sprite[] tileSprites = new Sprite[34];
        private readonly Dictionary<Texture2D, Sprite> actionButtonSprites = new Dictionary<Texture2D, Sprite>();
        private readonly Sprite[,] callBubbleSprites = new Sprite[5, 4];
        private readonly RectTransform[] callBubbleRoots = new RectTransform[4];
        private readonly Image[] callBubbleImages = new Image[4];
        private readonly CanvasGroup[] callBubbleGroups = new CanvasGroup[4];

        private Camera tableCamera;
        private Canvas canvas;
        private RectTransform handPanel;
        private RectTransform actionPanel;
        private RectTransform meldChoicePanel;
        private RectTransform meldChoiceContent;
        private Image meldChoiceBackgroundImage;
        private Outline meldChoicePanelBorder;
        private HorizontalLayoutGroup meldChoiceLayout;
        private RectTransform doraIndicatorPanel;
        private RectTransform doraIndicatorContent;
        private GameObject waitingTilesPanel;
        private RectTransform waitingTilesContent;
        private readonly RectTransform[] discardContentPanels = new RectTransform[4];
        private readonly RectTransform[] meldContentPanels = new RectTransform[4];
        private readonly RectTransform[] discardBoardAreaRects = new RectTransform[4];
        private readonly RectTransform[] discardBoardVisualRects = new RectTransform[4];
        private Text statusText;
        private Text lastDiscardText;
        private GameObject resultOverlay;
        private Image resultOverlayBackground;
        private GameObject riichiAnnouncementPanel;
        private Text resultTitleText;
        private Text resultDetailText;
        private Text riichiAnnouncementText;
        private Text meldChoiceTitle;
        private readonly Text[] playerInfoTexts = new Text[4];
        private Font defaultFont;
        private bool ownsDefaultFont;
        private Material tableMaterial;
        private Material feltMaterial;
        private readonly Material[] manTileRedMaterials = new Material[9];
        private Texture2D roundedUiTexture;
        private Sprite roundedUiSprite;

        private int currentPlayer;
        private int selectedTileIndex = -1;
        private int pendingDiscarder = -1;
        private Tile pendingDiscard;
        private bool waitingForResponse;
        private bool waitingForRiichiDrawAction;
        private bool choosingMeld;
        private bool callHighlightActive;
        private bool humanMayDiscard;
        private bool declaringRiichi;
        private bool humanTemporaryFuriten;
        private bool abortiveDrawPassed;
        private bool revealAllHands;
        private readonly HashSet<int> riichiDiscardTileIds = new HashSet<int>();
        private readonly bool[] announcedRiichiStates = new bool[4];
        private Coroutine turnRoutine;
        private Coroutine riichiAnnouncementRoutine;
        private Coroutine callBubbleRoutine;
        private int replacementDrawCount;
        private int hoveredDiscardTileIndex = -1;
        private int revealedWinnerIndex = -1;
        private int revealedWinningTileHandIndex = -1;
        private string currentStatusMessage = string.Empty;
        private string renderedHandStateKey = string.Empty;

        private const int DeadWallTileCount = 14;
        private const int MaximumDoraIndicatorCount = 5;

        private static readonly Color TableColor = new Color(0.01f, 0.012f, 0.016f);
        private static readonly Color FeltColor = new Color(0.025f, 0.028f, 0.034f);
        private static readonly Color PanelColor = new Color(0.14f, 0.16f, 0.19f, 0.97f);
        private static readonly string[] PreferredUiFontNames =
        {
            "Pretendard",
            "Noto Sans KR",
            "Noto Sans CJK KR",
            "Malgun Gothic",
            "맑은 고딕",
            "Apple SD Gothic Neo",
            "NanumGothic"
        };
        private static readonly float[] HorizontalDiscardColumns = { 0.14f, 0.284f, 0.428f, 0.572f, 0.716f, 0.86f };
        private static readonly float[] HorizontalDiscardRows = { 0.78f, 0.50f, 0.22f };

        private void Start()
        {
            if (sceneLayout == null)
                sceneLayout = FindFirstObjectByType<MahjongSceneLayout>(FindObjectsInactive.Include);
            if (threeDimensionalView == null)
                threeDimensionalView = FindFirstObjectByType<Mahjong3DTableView>(FindObjectsInactive.Include);

            BuildTileSprites();
            BuildCallBubbleSprites();
            BuildPresentation();
            BuildUi();
            if (autoStart) StartMatch();
        }

        private void OnDestroy()
        {
            if (turnRoutine != null) StopCoroutine(turnRoutine);
            if (callBubbleRoutine != null) StopCoroutine(callBubbleRoutine);
            for (int i = 0; i < tileSprites.Length; i++)
                if (tileSprites[i] != null) Destroy(tileSprites[i]);
            for (int actionIndex = 0; actionIndex < callBubbleSprites.GetLength(0); actionIndex++)
            {
                for (int directionIndex = 0; directionIndex < callBubbleSprites.GetLength(1); directionIndex++)
                {
                    if (callBubbleSprites[actionIndex, directionIndex] != null)
                        Destroy(callBubbleSprites[actionIndex, directionIndex]);
                }
            }
            if (roundedUiSprite != null) Destroy(roundedUiSprite);
            if (roundedUiTexture != null) Destroy(roundedUiTexture);
            if (ownsDefaultFont && defaultFont != null) Destroy(defaultFont);
            for (int i = 0; i < manTileRedMaterials.Length; i++)
                if (manTileRedMaterials[i] != null) Destroy(manTileRedMaterials[i]);
        }

        private void OnValidate()
        {
            aiTurnDelay = Mathf.Clamp(aiTurnDelay, 1f, 2f);
            discardPresentationDelay = Mathf.Clamp(discardPresentationDelay, 0.2f, 0.8f);
            callBubbleDuration = Mathf.Clamp(callBubbleDuration, 0.8f, 3f);
            if (!Application.isPlaying) return;
            for (int playerIndex = 0; playerIndex < discardBoardAreaRects.Length; playerIndex++)
                ApplyDiscardBoardLayout(playerIndex);
            ApplyDoraPanelLayout();
        }

        private void BuildTileSprites()
        {
            CreateTileSprites(manTilesTexture, 0, 9, 0.23f, 0.77f);
            ApplyTileOverrides(manTileTextures, 0);
            CreateTileSprites(pinTilesTexture, 9, 9, 0.265f, 0.786f);
            ApplyTileOverrides(pinTileTextures, 9);
            CreateTileSprites(souTilesTexture, 18, 9, 0.251f, 0.797f);
            OverrideTileSprite(oneSouTexture, 18);
            ApplyTileOverrides(souTileTextures, 19);
            CreateTileSprites(honorTilesTexture, 27, 7, 0.229f, 0.803f);
            ApplyTileOverrides(honorTileTextures, 27);
        }

        private void BuildCallBubbleSprites()
        {
            if (callBubbleSheetTexture == null) return;

            // The artwork crosses a mathematically even 4x5 grid by a few pixels at some
            // speech-tail tips. These measured gutters isolate each connected bubble so a
            // neighboring tail never appears as a stray dot beside the selected sprite.
            float scaleX = callBubbleSheetTexture.width / 1122f;
            float scaleY = callBubbleSheetTexture.height / 1402f;
            int[] columnStarts = { 23, 289, 568, 821 };
            int[] columnEnds = { 289, 568, 821, 1105 };
            int[] rowStarts = { 1118, 838, 561, 297, 36 };
            int[] rowEnds = { 1355, 1099, 841, 563, 283 };
            for (int actionRow = 0; actionRow < 5; actionRow++)
            {
                for (int directionColumn = 0; directionColumn < 4; directionColumn++)
                {
                    Rect rect = new Rect(
                        columnStarts[directionColumn] * scaleX,
                        rowStarts[actionRow] * scaleY,
                        (columnEnds[directionColumn] - columnStarts[directionColumn]) * scaleX,
                        (rowEnds[actionRow] - rowStarts[actionRow]) * scaleY);
                    Sprite sprite = Sprite.Create(
                        callBubbleSheetTexture,
                        rect,
                        new Vector2(0.5f, 0.5f),
                        100f,
                        0,
                        SpriteMeshType.FullRect);
                    sprite.name = "CallBubble_" + actionRow + "_" + directionColumn;
                    callBubbleSprites[actionRow, directionColumn] = sprite;
                }
            }
        }

        private void ApplyTileOverrides(Texture2D[] textures, int startIndex)
        {
            if (textures == null) return;

            for (int i = 0; i < textures.Length; i++)
                OverrideTileSprite(textures[i], startIndex + i);
        }

        private void OverrideTileSprite(Texture2D texture, int tileIndex)
        {
            if (texture == null || tileIndex < 0 || tileIndex >= tileSprites.Length) return;

            if (tileSprites[tileIndex] != null)
                Destroy(tileSprites[tileIndex]);

            tileSprites[tileIndex] = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
        }

        private void CreateTileSprites(
            Texture2D texture,
            int startIndex,
            int tileCount,
            float verticalMin,
            float verticalMax)
        {
            if (texture == null) return;

            int bottom = Mathf.FloorToInt(texture.height * verticalMin);
            int top = Mathf.CeilToInt(texture.height * verticalMax);
            int height = Mathf.Max(1, top - bottom);

            for (int i = 0; i < tileCount; i++)
            {
                int left = Mathf.FloorToInt(texture.width * (float)i / tileCount);
                int right = Mathf.FloorToInt(texture.width * (float)(i + 1) / tileCount);
                int width = Mathf.Max(1, right - left);
                tileSprites[startIndex + i] = Sprite.Create(
                    texture,
                    new Rect(left, bottom, width, height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);
            }
        }

        private void BuildPresentation()
        {
            QualitySettings.shadows = ShadowQuality.Disable;
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                light.shadows = LightShadows.None;

            tableMaterial = CreateMaterial(TableColor);
            feltMaterial = CreateMaterial(FeltColor);
            Shader manTileShader = Shader.Find("UpgradeMahjong/ManTileRed");
            if (manTileShader != null)
            {
                for (int tileIndex = 0; tileIndex < manTileRedMaterials.Length; tileIndex++)
                {
                    Material material = new Material(manTileShader)
                    {
                        name = (tileIndex + 1) + "만 빨간색 표식"
                    };
                    if (tileSprites[tileIndex] != null)
                        material.SetTexture("_MainTex", tileSprites[tileIndex].texture);
                    manTileRedMaterials[tileIndex] = material;
                }
            }

            bool useThreeDimensionalModels = threeDimensionalView != null && threeDimensionalView.IsReady;
            if (!useThreeDimensionalModels)
            {
                GameObject tableRoot = new GameObject("MahjongTableView");
                tableRoot.transform.rotation = Quaternion.Euler(tableSurfacePitch, 0f, 0f);

                GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                table.name = "TableFrame";
                table.transform.SetParent(tableRoot.transform, false);
                table.transform.localPosition = new Vector3(0f, -0.45f, 0f);
                table.transform.localScale = new Vector3(tableWidth, 0.8f, tableDepth);
                Renderer tableRenderer = table.GetComponent<Renderer>();
                tableRenderer.sharedMaterial = tableMaterial;
                DisableRendererShadows(tableRenderer);

                GameObject felt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                felt.name = "FeltSurface";
                felt.transform.SetParent(tableRoot.transform, false);
                felt.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                felt.transform.localScale = new Vector3(tableWidth - 0.45f, 0.08f, tableDepth - 0.45f);
                Renderer feltRenderer = felt.GetComponent<Renderer>();
                feltRenderer.sharedMaterial = feltMaterial;
                DisableRendererShadows(feltRenderer);

                CreateSeatMarker(tableRoot.transform, new Vector3(0f, 0.08f, -tableDepth * 0.5f + 0.7f), "동");
                CreateSeatMarker(tableRoot.transform, new Vector3(tableWidth * 0.5f - 0.7f, 0.08f, 0f), "남");
                CreateSeatMarker(tableRoot.transform, new Vector3(0f, 0.08f, tableDepth * 0.5f - 0.7f), "서");
                CreateSeatMarker(tableRoot.transform, new Vector3(-tableWidth * 0.5f + 0.7f, 0.08f, 0f), "북");
            }

            tableCamera = Camera.main;
            if (tableCamera == null)
            {
                GameObject cameraObject = new GameObject("MahjongTableCamera");
                tableCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            tableCamera.fieldOfView = cameraFov;
            tableCamera.transform.position = new Vector3(0f, cameraHeight, -cameraDistance);
            tableCamera.transform.LookAt(cameraLookAt);
            tableCamera.clearFlags = CameraClearFlags.SolidColor;
            tableCamera.backgroundColor = new Color(0.20f, 0.22f, 0.24f);

            if (useThreeDimensionalModels)
            {
                threeDimensionalView.Initialize(
                    tileSprites,
                    manTileRedMaterials,
                    tableCamera,
                    OnHandTileClicked,
                    SetHoveredDiscardTile,
                    ClearHoveredDiscardTile);
            }
        }

        private void CreateSeatMarker(Transform parent, Vector3 position, string label)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Seat_" + label;
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = position;
            marker.transform.localScale = new Vector3(0.9f, 0.08f, 0.45f);
            Renderer markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = tableMaterial;
            DisableRendererShadows(markerRenderer);
        }

        private static void DisableRendererShadows(Renderer renderer)
        {
            if (renderer == null) return;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void BuildUi()
        {
            defaultFont = ResolveUiFont();

            GameObject canvasObject;
            if (sceneLayout != null && sceneLayout.HudCanvas != null)
            {
                canvas = sceneLayout.HudCanvas;
                canvasObject = canvas.gameObject;
            }
            else
            {
                canvasObject = new GameObject("MahjongHUD");
                canvas = canvasObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (canvasObject.GetComponent<GraphicRaycaster>() == null)
                canvasObject.AddComponent<GraphicRaycaster>();
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<InputSystemUIInputModule>();
            }

            RectTransform statusContainer = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.StatusRoot,
                "StatusRoot",
                canvas.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(900f, 48f),
                new Vector2(0f, -28f));
            Image statusBackground = statusContainer.GetComponent<Image>();
            if (statusBackground == null) statusBackground = statusContainer.gameObject.AddComponent<Image>();
            statusBackground.color = new Color(0.035f, 0.045f, 0.055f, 0.88f);
            statusBackground.raycastTarget = false;
            ApplyRoundedImage(statusBackground);
            Outline statusBorder = statusContainer.GetComponent<Outline>();
            if (statusBorder == null) statusBorder = statusContainer.gameObject.AddComponent<Outline>();
            statusBorder.effectColor = new Color(0.44f, 0.52f, 0.56f, 0.72f);
            statusBorder.effectDistance = new Vector2(1f, -1f);

            statusText = CreateText(statusContainer, "상태", 27, TextAnchor.MiddleCenter, new Color(0.96f, 0.97f, 0.98f));
            statusText.fontStyle = FontStyle.Bold;
            StretchToParent(statusText.rectTransform);
            statusContainer.gameObject.SetActive(false);

            RectTransform lastDiscardContainer = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.LastDiscardRoot,
                "LastDiscardRoot",
                canvas.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(360f, 42f),
                new Vector2(0f, 175f));
            lastDiscardText = CreateText(lastDiscardContainer, "최근 타패", 22, TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.47f));
            StretchToParent(lastDiscardText.rectTransform);
            lastDiscardContainer.gameObject.SetActive(false);

            CreatePlayerInfo(0, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(520f, 42f), new Vector2(0f, 205f), TextAnchor.MiddleCenter);
            CreatePlayerInfo(1, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(250f, 90f), new Vector2(-26f, 0f), TextAnchor.MiddleRight);
            CreatePlayerInfo(2, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(520f, 42f), new Vector2(0f, -95f), TextAnchor.MiddleCenter);
            CreatePlayerInfo(3, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(250f, 90f), new Vector2(26f, 0f), TextAnchor.MiddleLeft);

            CreateBoardArea(0, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            CreateBoardArea(1, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            CreateBoardArea(2, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            CreateBoardArea(3, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));

            handPanel = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.HandRoot,
                "HandPanel",
                canvas.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(1450f, 176f),
                Vector2.zero);
            GameObject handObject = handPanel.gameObject;
            HorizontalLayoutGroup handLayout = handObject.GetComponent<HorizontalLayoutGroup>();
            if (handLayout == null) handLayout = handObject.AddComponent<HorizontalLayoutGroup>();
            handLayout.spacing = sceneLayout == null ? -2f : sceneLayout.HandSpacing;
            handLayout.childAlignment = TextAnchor.MiddleCenter;
            handLayout.childControlWidth = false;
            handLayout.childControlHeight = false;

            actionPanel = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.ActionRoot,
                "ActionPanel",
                canvas.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(920f, 172f),
                new Vector2(0f, 158f));
            SetAnchors(
                actionPanel,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(980f, 196f),
                new Vector2(0f, 170f));
            GameObject actionObject = actionPanel.gameObject;
            Image actionBackground = actionObject.GetComponent<Image>();
            if (actionBackground == null) actionBackground = actionObject.AddComponent<Image>();
            actionBackground.color = PanelColor;
            ApplyRoundedImage(actionBackground);
            actionBackground.raycastTarget = false;
            Outline actionPanelBorder = actionObject.GetComponent<Outline>();
            if (actionPanelBorder == null) actionPanelBorder = actionObject.AddComponent<Outline>();
            actionPanelBorder.effectColor = new Color(0.62f, 0.70f, 0.74f, 0.95f);
            actionPanelBorder.effectDistance = new Vector2(2f, -2f);
            GridLayoutGroup actionLayout = actionObject.GetComponent<GridLayoutGroup>();
            if (actionLayout == null) actionLayout = actionObject.AddComponent<GridLayoutGroup>();
            actionLayout.padding = new RectOffset(20, 20, 18, 18);
            actionLayout.spacing = sceneLayout == null ? new Vector2(10f, 10f) : sceneLayout.ActionButtonSpacing;
            actionLayout.cellSize = sceneLayout == null ? new Vector2(205f, 62f) : sceneLayout.ActionButtonSize;
            actionLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            actionLayout.constraintCount = sceneLayout == null ? 4 : sceneLayout.ActionColumnCount;
            actionLayout.childAlignment = TextAnchor.MiddleCenter;

            CreateDoraIndicatorPanel();
            CreateWaitingTilesPanel();
            CreateRiichiAnnouncementPanel();
            CreateMeldChoicePanel();
            CreateResultOverlay();
            CreateCallBubblePanels();
            SetLegacyTileUiVisible(threeDimensionalView == null
                || !threeDimensionalView.IsReady
                || !threeDimensionalView.HideLegacyTileUi);
            RebuildActionButtons();
        }

        private void SetLegacyTileUiVisible(bool visible)
        {
            if (handPanel != null) handPanel.gameObject.SetActive(visible);
            for (int playerIndex = 0; playerIndex < discardBoardAreaRects.Length; playerIndex++)
            {
                if (discardBoardAreaRects[playerIndex] != null)
                    discardBoardAreaRects[playerIndex].gameObject.SetActive(visible);
            }

            if (meldContentPanels[0] != null && meldContentPanels[0].parent != null)
                meldContentPanels[0].parent.gameObject.SetActive(visible);
        }

        private void CreateDoraIndicatorPanel()
        {
            doraIndicatorPanel = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.DoraRoot,
                "DoraIndicatorPanel",
                canvas.transform,
                Vector2.one,
                Vector2.one,
                doraPanelSize,
                doraPanelPosition);
            GameObject panelObject = doraIndicatorPanel.gameObject;
            SetAnchors(
                doraIndicatorPanel,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(500f, 190f),
                doraPanelPosition);

            Image background = panelObject.GetComponent<Image>();
            if (background == null) background = panelObject.AddComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = false;
            background.enabled = false;

            Outline border = panelObject.GetComponent<Outline>();
            if (border == null) border = panelObject.AddComponent<Outline>();
            border.enabled = false;

            GameObject contentObject = new GameObject("DoraIndicatorSlots");
            doraIndicatorContent = contentObject.AddComponent<RectTransform>();
            doraIndicatorContent.SetParent(canvas.transform, false);
            SetAnchors(
                doraIndicatorContent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(520f, 138f),
                doraPanelPosition + new Vector2(-12f, 12f));
            doraIndicatorContent.SetAsLastSibling();
            doraIndicatorContent.gameObject.SetActive(false);

            HorizontalLayoutGroup layout = contentObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            RefreshDoraIndicatorPanel();
        }

        private void CreateWaitingTilesPanel()
        {
            RectTransform panel = ResolveUiRoot(
                null,
                "WaitingTilesPanel",
                canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(520f, 69f),
                doraPanelPosition + new Vector2(-12f, -145f));
            waitingTilesPanel = panel.gameObject;
            waitingTilesContent = panel;

            Image registrationGraphic = waitingTilesPanel.AddComponent<Image>();
            registrationGraphic.color = Color.clear;
            registrationGraphic.raycastTarget = false;

            HorizontalLayoutGroup layout = waitingTilesPanel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            waitingTilesPanel.SetActive(false);
        }

        private void CreateRiichiAnnouncementPanel()
        {
            RectTransform panel = ResolveUiRoot(
                null,
                "RiichiAnnouncementPanel",
                canvas.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(620f, 112f),
                new Vector2(0f, 132f));
            riichiAnnouncementPanel = panel.gameObject;

            Image background = riichiAnnouncementPanel.AddComponent<Image>();
            background.color = new Color(0.18f, 0.015f, 0.012f, 0.98f);
            background.raycastTarget = false;
            ApplyRoundedImage(background);

            Outline border = riichiAnnouncementPanel.AddComponent<Outline>();
            border.effectColor = new Color(1f, 0.70f, 0.10f, 1f);
            border.effectDistance = new Vector2(5f, -5f);

            riichiAnnouncementText = CreateText(
                riichiAnnouncementPanel.transform,
                "리치!",
                44,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.92f, 0.48f, 1f));
            riichiAnnouncementText.fontStyle = FontStyle.Bold;
            StretchToParent(riichiAnnouncementText.rectTransform);
            riichiAnnouncementPanel.SetActive(false);
        }

        private void ApplyDoraPanelLayout()
        {
            if (doraIndicatorPanel == null) return;
            Vector2 safeSize = new Vector2(Mathf.Max(500f, doraPanelSize.x), Mathf.Max(190f, doraPanelSize.y));
            SetAnchors(doraIndicatorPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), safeSize, doraPanelPosition);
            if (doraIndicatorContent != null)
            {
                SetAnchors(
                    doraIndicatorContent,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(520f, 138f),
                    doraPanelPosition + new Vector2(-12f, 12f));
                doraIndicatorContent.SetAsLastSibling();
            }
            if (waitingTilesPanel != null)
            {
                RectTransform waitingRect = waitingTilesPanel.GetComponent<RectTransform>();
                SetAnchors(
                    waitingRect,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    waitingRect.sizeDelta,
                    doraPanelPosition + new Vector2(-12f, -145f));
            }
        }

        private void RefreshDoraIndicatorPanel()
        {
            if (doraIndicatorContent == null) return;
            ClearChildren(doraIndicatorContent);
            doraTileVisuals.Clear();
            doraIndicatorContent.gameObject.SetActive(false);
            RefreshDiscardHighlights();
        }

        private void CreateMeldChoicePanel()
        {
            meldChoicePanel = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.MeldChoiceRoot,
                "MeldChoicePanel",
                canvas.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(1040f, 178f),
                new Vector2(0f, 158f));
            GameObject panelObject = meldChoicePanel.gameObject;
            SetAnchors(
                meldChoicePanel,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(1000f, 340f),
                new Vector2(0f, 190f));

            meldChoiceBackgroundImage = panelObject.GetComponent<Image>();
            if (meldChoiceBackgroundImage == null) meldChoiceBackgroundImage = panelObject.AddComponent<Image>();
            meldChoiceBackgroundImage.color = new Color(0.035f, 0.11f, 0.105f, 0.97f);
            meldChoiceBackgroundImage.raycastTarget = true;
            meldChoiceBackgroundImage.sprite = null;
            meldChoiceBackgroundImage.type = Image.Type.Simple;

            meldChoicePanelBorder = panelObject.GetComponent<Outline>();
            if (meldChoicePanelBorder == null) meldChoicePanelBorder = panelObject.AddComponent<Outline>();
            meldChoicePanelBorder.enabled = true;
            meldChoicePanelBorder.effectColor = new Color(0.92f, 0.73f, 0.28f, 0.95f);
            meldChoicePanelBorder.effectDistance = new Vector2(4f, -4f);

            meldChoiceTitle = CreateText(panelObject.transform, "조합을 선택하세요", 34, TextAnchor.MiddleCenter, new Color(1f, 0.91f, 0.48f));
            meldChoiceTitle.fontStyle = FontStyle.Bold;
            SetAnchors(meldChoiceTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(760f, 58f), new Vector2(-62f, -12f));

            GameObject contentObject = new GameObject("MeldChoiceContent");
            meldChoiceContent = contentObject.AddComponent<RectTransform>();
            meldChoiceContent.SetParent(meldChoicePanel, false);
            SetAnchors(meldChoiceContent, Vector2.zero, Vector2.one, new Vector2(-48f, -94f), new Vector2(0f, -34f));

            meldChoiceLayout = contentObject.AddComponent<HorizontalLayoutGroup>();
            meldChoiceLayout.spacing = 40f;
            meldChoiceLayout.childAlignment = TextAnchor.MiddleCenter;
            meldChoiceLayout.childControlWidth = false;
            meldChoiceLayout.childControlHeight = false;

            Button cancelButton = CreateButton(panelObject.transform, "취소", new Color(0.40f, 0.15f, 0.15f), HideMeldChoices);
            SetAnchors(cancelButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(150f, 54f), new Vector2(-18f, -12f));

            panelObject.SetActive(false);
        }

        private void CreateResultOverlay()
        {
            RectTransform overlayRect = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.ResultRoot,
                "WinResultOverlay",
                canvas.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(-32f, -32f),
                Vector2.zero);
            resultOverlay = overlayRect.gameObject;

            resultOverlayBackground = resultOverlay.GetComponent<Image>();
            if (resultOverlayBackground == null) resultOverlayBackground = resultOverlay.AddComponent<Image>();
            resultOverlayBackground.color = new Color(0.015f, 0.02f, 0.035f, 0.96f);
            resultOverlayBackground.raycastTarget = true;

            resultTitleText = CreateText(resultOverlay.transform, "화료", 52, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.32f));
            SetAnchors(resultTitleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(900f, 84f), new Vector2(0f, 310f));

            resultDetailText = CreateText(resultOverlay.transform, string.Empty, 24, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(resultDetailText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1180f, 510f), new Vector2(0f, 5f));

            Button nextButton = CreateButton(resultOverlay.transform, "다음 국", new Color(0.13f, 0.45f, 0.35f), StartMatch);
            SetAnchors(nextButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(260f, 70f), new Vector2(0f, -330f));
            resultOverlay.SetActive(false);
        }

        private void CreateCallBubblePanels()
        {
            Vector2[] anchors =
            {
                new Vector2(0.5f, 0f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 0.5f)
            };
            Vector2[] pivots =
            {
                new Vector2(0.5f, 0f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 0.5f)
            };
            Vector2[] positions =
            {
                new Vector2(0f, 260f),
                new Vector2(-105f, 0f),
                new Vector2(0f, -55f),
                new Vector2(105f, 0f)
            };
            string[] names = { "Bottom", "Right", "Top", "Left" };

            for (int playerIndex = 0; playerIndex < callBubbleRoots.Length; playerIndex++)
            {
                GameObject bubbleObject = new GameObject("CallBubble_" + names[playerIndex]);
                RectTransform bubbleRect = bubbleObject.AddComponent<RectTransform>();
                bubbleRect.SetParent(canvas.transform, false);
                bubbleRect.anchorMin = anchors[playerIndex];
                bubbleRect.anchorMax = anchors[playerIndex];
                bubbleRect.pivot = pivots[playerIndex];
                bubbleRect.sizeDelta = new Vector2(270f, 225f);
                bubbleRect.anchoredPosition = positions[playerIndex];

                Image bubbleImage = bubbleObject.AddComponent<Image>();
                bubbleImage.color = Color.white;
                bubbleImage.preserveAspect = true;
                bubbleImage.raycastTarget = false;

                CanvasGroup bubbleGroup = bubbleObject.AddComponent<CanvasGroup>();
                bubbleGroup.alpha = 0f;
                bubbleGroup.interactable = false;
                bubbleGroup.blocksRaycasts = false;

                callBubbleRoots[playerIndex] = bubbleRect;
                callBubbleImages[playerIndex] = bubbleImage;
                callBubbleGroups[playerIndex] = bubbleGroup;
                bubbleObject.SetActive(false);
            }
        }

        private static int GetCallBubbleActionRow(ActionType action)
        {
            switch (action)
            {
                case ActionType.Pon: return 0;
                case ActionType.Kan: return 1;
                case ActionType.Chi: return 2;
                case ActionType.Ron: return 3;
                case ActionType.Tsumo: return 4;
                default: return -1;
            }
        }

        private static int GetCallBubbleDirectionColumn(int playerIndex)
        {
            // Sheet columns point north, east, south, west. Each bubble tail points back
            // toward the player occupying top, right, bottom, or left respectively.
            switch (playerIndex)
            {
                case 0: return 2;
                case 1: return 1;
                case 2: return 0;
                case 3: return 3;
                default: return -1;
            }
        }

        private void ShowCallBubble(int playerIndex, ActionType action)
        {
            int actionRow = GetCallBubbleActionRow(action);
            int directionColumn = GetCallBubbleDirectionColumn(playerIndex);
            if (actionRow < 0 || directionColumn < 0
                || playerIndex < 0 || playerIndex >= callBubbleRoots.Length)
                return;

            RectTransform root = callBubbleRoots[playerIndex];
            Image image = callBubbleImages[playerIndex];
            CanvasGroup group = callBubbleGroups[playerIndex];
            Sprite sprite = callBubbleSprites[actionRow, directionColumn];
            if (root == null || image == null || group == null || sprite == null) return;

            if (callBubbleRoutine != null)
            {
                StopCoroutine(callBubbleRoutine);
                callBubbleRoutine = null;
            }
            HideCallBubbles();

            image.sprite = sprite;
            root.localScale = Vector3.one * 0.68f;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            group.alpha = 1f;
            callBubbleRoutine = StartCoroutine(CallBubbleRoutine(root, group));
        }

        private IEnumerator CallBubbleRoutine(RectTransform root, CanvasGroup group)
        {
            const float popDuration = 0.16f;
            float elapsed = 0f;
            while (elapsed < popDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / popDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                root.localScale = Vector3.one * Mathf.Lerp(0.68f, 1.08f, eased);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < 0.09f)
            {
                elapsed += Time.unscaledDeltaTime;
                root.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, Mathf.Clamp01(elapsed / 0.09f));
                yield return null;
            }
            root.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(Mathf.Max(0.35f, callBubbleDuration - 0.45f));
            elapsed = 0f;
            const float fadeDuration = 0.2f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
                yield return null;
            }

            root.gameObject.SetActive(false);
            group.alpha = 0f;
            callBubbleRoutine = null;
        }

        private void HideCallBubbles()
        {
            for (int playerIndex = 0; playerIndex < callBubbleRoots.Length; playerIndex++)
            {
                if (callBubbleGroups[playerIndex] != null)
                    callBubbleGroups[playerIndex].alpha = 0f;
                if (callBubbleRoots[playerIndex] != null)
                {
                    callBubbleRoots[playerIndex].localScale = Vector3.one;
                    callBubbleRoots[playerIndex].gameObject.SetActive(false);
                }
            }
        }

        private void CreateBoardArea(int playerIndex, Vector2 anchorMin, Vector2 anchorMax)
        {
            BoardLayoutSnapshot layout = GetDiscardBoardLayout(playerIndex);
            Vector2 boardSize = GetSafeBoardSize(layout);
            RectTransform area = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.GetDiscardRoot(playerIndex),
                "BoardArea_" + playersAreaName(playerIndex),
                canvas.transform,
                anchorMin,
                anchorMax,
                GetRotatedBounds(boardSize, layout.Rotation),
                layout.Position);
            discardBoardAreaRects[playerIndex] = area;
            bool usesAuthoredBoard = sceneLayout != null && sceneLayout.GetDiscardRoot(playerIndex) == area;

            // 판 이미지는 만들지 않고, 작탁 위에 패를 놓기 위한 투명 좌표 영역만 유지한다.
            GameObject placementObject = new GameObject("DiscardPlacementArea");
            RectTransform placementRect = placementObject.AddComponent<RectTransform>();
            placementRect.SetParent(area, false);
            SetAnchors(placementRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), boardSize, Vector2.zero);
            placementRect.localRotation = usesAuthoredBoard
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, layout.Rotation);
            discardBoardVisualRects[playerIndex] = placementRect;

            RectTransform meldRect;
            if (playerIndex == 0)
            {
                meldRect = CreateOwnMeldArea();
            }
            else
            {
                GameObject meldObject = new GameObject("OpenMelds");
                meldRect = meldObject.AddComponent<RectTransform>();
                meldRect.SetParent(area, false);
                SetAnchors(meldRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-18f, 52f), new Vector2(0f, -52f));
                if (playerIndex == 1 || playerIndex == 3)
                {
                    VerticalLayoutGroup meldLayout = meldObject.AddComponent<VerticalLayoutGroup>();
                    meldLayout.spacing = 3f;
                    meldLayout.childAlignment = TextAnchor.UpperCenter;
                    meldLayout.childControlWidth = false;
                    meldLayout.childControlHeight = false;
                }
                else
                {
                    HorizontalLayoutGroup meldLayout = meldObject.AddComponent<HorizontalLayoutGroup>();
                    meldLayout.spacing = 5f;
                    meldLayout.childAlignment = TextAnchor.MiddleCenter;
                    meldLayout.childControlWidth = false;
                    meldLayout.childControlHeight = false;
                }
            }

            meldContentPanels[playerIndex] = meldRect;
            discardContentPanels[playerIndex] = placementRect;
        }

        private RectTransform CreateOwnMeldArea()
        {
            RectTransform panel = ResolveUiRoot(
                sceneLayout == null ? null : sceneLayout.OwnMeldRoot,
                "MyOpenMelds",
                canvas.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(340f, 142f),
                new Vector2(-28f, 238f));
            GameObject panelObject = panel.gameObject;

            Image background = panelObject.GetComponent<Image>();
            if (background == null) background = panelObject.AddComponent<Image>();
            background.color = new Color(0.015f, 0.05f, 0.055f, 0.96f);
            background.raycastTarget = false;

            Text header = CreateText(panel, "내 퐁·치·깡", 18, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.42f));
            SetAnchors(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-12f, 28f), new Vector2(0f, -18f));

            GameObject contentObject = new GameObject("MeldContent");
            RectTransform content = contentObject.AddComponent<RectTransform>();
            content.SetParent(panel, false);
            SetAnchors(content, Vector2.zero, Vector2.one, new Vector2(-18f, -42f), new Vector2(0f, -21f));

            GridLayoutGroup contentLayout = contentObject.AddComponent<GridLayoutGroup>();
            contentLayout.padding = new RectOffset(3, 3, 3, 3);
            contentLayout.spacing = new Vector2(4f, 4f);
            contentLayout.cellSize = new Vector2(152f, 44f);
            contentLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            contentLayout.constraintCount = 2;
            contentLayout.childAlignment = TextAnchor.UpperCenter;

            return content;
        }

        private static string playersAreaName(int playerIndex)
        {
            switch (playerIndex)
            {
                case 0: return "동 (나)";
                case 1: return "남 (상가)";
                case 2: return "서 (대가)";
                default: return "북 (하가)";
            }
        }

        private void CreatePlayerInfo(int index, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position, TextAnchor alignment)
        {
            RectTransform authoredRoot = sceneLayout == null ? null : sceneLayout.GetPlayerInfoRoot(index);
            RectTransform container = ResolveUiRoot(
                authoredRoot,
                "PlayerInfo_" + index,
                canvas.transform,
                anchorMin,
                anchorMax,
                size,
                position);
            Text text = CreateText(container, "플레이어", 22, alignment, Color.white);
            StretchToParent(text.rectTransform);
            playerInfoTexts[index] = text;
        }

        private Text CreateText(Transform parent, string value, int fontSize, TextAnchor alignment, Color color)
        {
            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(parent, false);
            Text text = textObject.AddComponent<Text>();
            text.font = defaultFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.alignByGeometry = true;
            text.lineSpacing = 1.05f;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Font ResolveUiFont()
        {
            if (uiFont != null) return uiFont;

            HashSet<string> installedFonts = new HashSet<string>(
                Font.GetOSInstalledFontNames(),
                StringComparer.OrdinalIgnoreCase);
            foreach (string fontName in PreferredUiFontNames)
            {
                if (!installedFonts.Contains(fontName)) continue;
                Font runtimeFont = Font.CreateDynamicFontFromOSFont(fontName, 32);
                if (runtimeFont == null) continue;
                runtimeFont.name = "Mahjong UI - " + fontName;
                ownsDefaultFont = true;
                return runtimeFont;
            }

            ownsDefaultFont = false;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 size, Vector2 position)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static RectTransform ResolveUiRoot(
            RectTransform authoredRoot,
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size,
            Vector2 position)
        {
            if (authoredRoot != null) return authoredRoot;

            GameObject rootObject = new GameObject(objectName);
            RectTransform root = rootObject.AddComponent<RectTransform>();
            root.SetParent(parent, false);
            SetAnchors(root, anchorMin, anchorMax, size, position);
            return root;
        }

        private static void StretchToParent(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void StartMatch()
        {
            if (turnRoutine != null) StopCoroutine(turnRoutine);
            if (callBubbleRoutine != null)
            {
                StopCoroutine(callBubbleRoutine);
                callBubbleRoutine = null;
            }
            HideCallBubbles();
            if (resultOverlay != null) resultOverlay.SetActive(false);
            if (resultOverlayBackground != null)
                resultOverlayBackground.color = new Color(0.015f, 0.02f, 0.035f, 0.96f);
            players.Clear();
            players.Add(new PlayerState { name = "동 (나)", isHuman = true });
            players.Add(new PlayerState { name = "남 (상가)" });
            players.Add(new PlayerState { name = "서 (대가)" });
            players.Add(new PlayerState { name = "북 (하가)" });

            wall.Clear();
            deadWall.Clear();
            doraIndicators.Clear();
            replacementDrawCount = 0;
            BuildWall();
            RevealNextDoraIndicator();
            DealInitialHands();
            currentPlayer = 0;
            selectedTileIndex = -1;
            pendingDiscarder = -1;
            pendingDiscard = null;
            waitingForResponse = false;
            waitingForRiichiDrawAction = false;
            choosingMeld = false;
            humanMayDiscard = true;
            declaringRiichi = false;
            humanTemporaryFuriten = false;
            abortiveDrawPassed = false;
            revealAllHands = false;
            revealedWinnerIndex = -1;
            revealedWinningTileHandIndex = -1;
            riichiDiscardTileIds.Clear();
            Array.Clear(announcedRiichiStates, 0, announcedRiichiStates.Length);
            if (riichiAnnouncementRoutine != null)
            {
                StopCoroutine(riichiAnnouncementRoutine);
                riichiAnnouncementRoutine = null;
            }
            if (riichiAnnouncementPanel != null)
                riichiAnnouncementPanel.SetActive(false);
            renderedHandStateKey = string.Empty;
            if (meldChoicePanel != null) meldChoicePanel.gameObject.SetActive(false);
            UpdateView("버릴 패를 선택하세요.");
        }

        private void BuildWall()
        {
            int id = 0;
            for (int suit = 0; suit < 3; suit++)
            {
                for (int rank = 1; rank <= 9; rank++)
                {
                    for (int copy = 0; copy < 4; copy++)
                        wall.Add(new Tile { suit = (Suit)suit, rank = rank, id = id++ });
                }
            }

            for (int rank = 1; rank <= 7; rank++)
            {
                for (int copy = 0; copy < 4; copy++)
                    wall.Add(new Tile { suit = Suit.Honor, rank = rank, id = id++ });
            }

            for (int i = wall.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                Tile temp = wall[i];
                wall[i] = wall[swapIndex];
                wall[swapIndex] = temp;
            }

            int deadWallStart = Mathf.Max(0, wall.Count - DeadWallTileCount);
            deadWall.AddRange(wall.GetRange(deadWallStart, wall.Count - deadWallStart));
            wall.RemoveRange(deadWallStart, wall.Count - deadWallStart);
        }

        private void DealInitialHands()
        {
            for (int round = 0; round < 13; round++)
            {
                for (int player = 0; player < players.Count; player++)
                    players[player].hand.Add(DrawTile());
            }

            // 최초 13장은 정렬하되, 친의 14번째 패는 실제 배패처럼 맨 오른쪽에 둔다.
            // freshDrawId 패에 별도 간격을 주는 UI가 정렬된 손패 중간을 가르지 않게 한다.
            for (int i = 0; i < players.Count; i++) SortHand(players[i].hand);

            Tile dealerDraw = DrawTile();
            if (dealerDraw != null)
            {
                players[0].hand.Add(dealerDraw);
                players[0].freshDrawId = dealerDraw.id;
            }
            else
            {
                players[0].freshDrawId = -1;
            }
            players[0].freshDrawIsRinshan = false;
        }

        private Tile DrawTile()
        {
            if (wall.Count == 0) return null;
            Tile tile = wall[0];
            wall.RemoveAt(0);
            return tile;
        }

        private Tile DrawReplacementTile()
        {
            if (replacementDrawCount >= 4 || replacementDrawCount >= deadWall.Count) return null;

            Tile tile = deadWall[replacementDrawCount++];
            if (wall.Count > 0)
                wall.RemoveAt(wall.Count - 1);
            return tile;
        }

        private void RevealNextDoraIndicator()
        {
            if (doraIndicators.Count >= MaximumDoraIndicatorCount) return;

            int indicatorSlot = 4 + doraIndicators.Count * 2;
            if (indicatorSlot >= deadWall.Count) return;
            doraIndicators.Add(deadWall[indicatorSlot]);
            RefreshDoraIndicatorPanel();
        }

        private void OnHandTileClicked(int index)
        {
            if (!humanMayDiscard || waitingForResponse || waitingForRiichiDrawAction || currentPlayer != 0) return;
            if (index < 0 || index >= players[0].hand.Count) return;

            Tile discarded = players[0].hand[index];
            if (declaringRiichi && !riichiDiscardTileIds.Contains(discarded.id))
            {
                UpdateView("리치 후 텐파이가 유지되는 패만 버릴 수 있습니다.");
                return;
            }
            if (players[0].riichiDeclared && !declaringRiichi && discarded.id != players[0].freshDrawId)
            {
                UpdateView("리치 선언 후에는 새로 뽑은 패만 버릴 수 있습니다.");
                return;
            }

            bool declaredRiichiThisDiscard = declaringRiichi;
            if (declaringRiichi)
            {
                players[0].riichiDeclared = true;
                players[0].ippatsuEligible = true;
                players[0].score -= 1000;
                declaringRiichi = false;
                riichiDiscardTileIds.Clear();
            }
            players[0].hand.RemoveAt(index);
            players[0].freshDrawId = -1;
            players[0].freshDrawIsRinshan = false;
            SortHand(players[0].hand);
            players[0].discards.Add(discarded);
            if (declaredRiichiThisDiscard)
                players[0].riichiDiscardIndex = players[0].discards.Count - 1;
            else if (players[0].riichiDeclared)
                players[0].ippatsuEligible = false;
            selectedTileIndex = -1;
            humanMayDiscard = false;
            pendingDiscarder = 0;
            pendingDiscard = discarded;
            lastDiscardText.text = "내 타패: " + discarded.Label;
            UpdateView(string.Empty);
            if (TryResolveAiRon(discarded, 0)) return;
            if (TryResolveAiCall(discarded, 0)) return;
            turnRoutine = StartCoroutine(ContinueAfterDiscardRoutine());
        }

        private IEnumerator ContinueAfterDiscardRoutine()
        {
            yield return new WaitForSeconds(Mathf.Clamp(discardPresentationDelay, 0.2f, 0.8f));
            pendingDiscard = null;
            pendingDiscarder = -1;
            currentPlayer = 1;
            StartNextTurn();
        }

        private void StartNextTurn()
        {
            if (wall.Count <= 0)
            {
                UpdateView("유국 — 패산이 소진되었습니다.");
                return;
            }

            if (currentPlayer == 0)
            {
                BeginHumanTurn(true);
                return;
            }

            turnRoutine = StartCoroutine(AiTurnRoutine(currentPlayer));
        }

        private void BeginHumanTurn(bool draw)
        {
            if (draw)
            {
                if (!players[0].riichiDeclared)
                    humanTemporaryFuriten = false;
                Tile tile = DrawTile();
                if (tile != null)
                {
                    players[0].hand.Add(tile);
                    players[0].freshDrawId = tile.id;
                    players[0].freshDrawIsRinshan = false;
                }
                players[0].turnCount++;
            }

            waitingForRiichiDrawAction = false;
            humanMayDiscard = true;
            if (players[0].riichiDeclared)
            {
                bool canTsumo = CanTsumo();
                bool canKan = GetSelfKanChoices().Count > 0;
                if (canTsumo || canKan)
                {
                    waitingForRiichiDrawAction = true;
                    UpdateView(canTsumo
                        ? "리치 상태 — 쯔모하거나 자동 타패하세요."
                        : "리치 상태 — 안깡하거나 자동 타패하세요.");
                    return;
                }

                humanMayDiscard = false;
                UpdateView("리치 상태 — 모패를 자동 타패합니다.");
                turnRoutine = StartCoroutine(AutoDiscardRiichiDrawRoutine(0.42f));
                return;
            }

            UpdateView(CanTsumo()
                ? "쯔모 가능 — 행동을 선택하세요."
                : "버릴 패를 선택하세요.");
        }

        private IEnumerator AutoDiscardRiichiDrawRoutine(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (players.Count == 0 || currentPlayer != 0 || !players[0].riichiDeclared
                || waitingForResponse || waitingForRiichiDrawAction)
                yield break;

            int freshDrawIndex = players[0].hand.FindIndex(tile => tile.id == players[0].freshDrawId);
            if (freshDrawIndex < 0) yield break;
            humanMayDiscard = true;
            OnHandTileClicked(freshDrawIndex);
        }

        private IEnumerator AiTurnRoutine(int playerIndex)
        {
            PlayerState player = players[playerIndex];
            Tile drawn = DrawTile();
            if (drawn == null)
            {
                UpdateView("유국 — 패산이 소진되었습니다.");
                yield break;
            }

            // 기존 13장을 정렬한 뒤 새 패를 맨 끝에 놓아 실제 모패처럼 분리해서 보여 준다.
            SortHand(player.hand);
            player.hand.Add(drawn);
            player.freshDrawId = drawn.id;
            player.freshDrawIsRinshan = false;
            player.turnCount++;
            UpdateView(string.Empty);
            yield return new WaitForSeconds(Mathf.Clamp(aiTurnDelay, 1f, 2f));

            if (CanPlayerTsumo(playerIndex))
            {
                CompleteWin(
                    playerIndex,
                    UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo,
                    GetFreshDrawTileIndex(playerIndex));
                yield break;
            }

            int discardIndex = ChooseAiDiscard(player);
            yield return DiscardForAiRoutine(playerIndex, discardIndex, true);
        }

        private IEnumerator DiscardForAiRoutine(int playerIndex, int discardIndex, bool allowRiichi)
        {
            PlayerState player = players[playerIndex];
            if (discardIndex < 0 || discardIndex >= player.hand.Count) yield break;

            bool declaredRiichiThisDiscard = allowRiichi && ShouldAiDeclareRiichi(player, discardIndex);
            if (declaredRiichiThisDiscard)
            {
                player.riichiDeclared = true;
                player.ippatsuEligible = true;
                player.score -= 1000;
            }
            Tile discarded = player.hand[discardIndex];
            if (threeDimensionalView != null && threeDimensionalView.IsReady)
                threeDimensionalView.ShowTemporaryDiscardGap(playerIndex, discardIndex, 0.5f);
            player.hand.RemoveAt(discardIndex);
            player.freshDrawId = -1;
            player.freshDrawIsRinshan = false;
            player.discards.Add(discarded);
            if (declaredRiichiThisDiscard)
                player.riichiDiscardIndex = player.discards.Count - 1;
            else if (player.riichiDeclared)
                player.ippatsuEligible = false;
            pendingDiscarder = playerIndex;
            pendingDiscard = discarded;
            lastDiscardText.text = player.name + " 타패: " + discarded.Label;
            UpdateView(string.Empty);

            if (CanRon(discarded))
            {
                waitingForResponse = true;
                UpdateView("론 또는 패스를 선택하세요.");
                yield break;
            }

            if (TryResolveAiRon(discarded, playerIndex))
                yield break;

            if (CanHumanRespondToDiscard(discarded, playerIndex))
            {
                waitingForResponse = true;
                UpdateView("행동을 선택하세요.");
                yield break;
            }

            if (TryResolveAiCall(discarded, playerIndex))
                yield break;

            yield return new WaitForSeconds(Mathf.Clamp(discardPresentationDelay, 0.2f, 0.8f));
            pendingDiscard = null;
            pendingDiscarder = -1;
            currentPlayer = (playerIndex + 1) % 4;
            StartNextTurn();
        }

        private IEnumerator AiDiscardAfterCallRoutine(int playerIndex, string callLabel)
        {
            UpdateView(players[playerIndex].name + " " + callLabel + " — 타패를 고르는 중입니다.");
            yield return new WaitForSeconds(Mathf.Clamp(aiTurnDelay * 0.55f, 0.45f, 1.1f));
            int discardIndex = ChooseAiDiscard(players[playerIndex]);
            yield return DiscardForAiRoutine(playerIndex, discardIndex, false);
        }

        private int ChooseAiDiscard(PlayerState player)
        {
            if (player.hand.Count == 0) return 0;
            if (player.riichiDeclared)
            {
                int freshDrawIndex = player.hand.FindIndex(tile => tile.id == player.freshDrawId);
                return freshDrawIndex >= 0 ? freshDrawIndex : player.hand.Count - 1;
            }

            int bestShanten = int.MaxValue;
            int bestUkeire = -1;
            int bestDoraPenalty = int.MaxValue;
            List<int> bestIndices = new List<int>();
            int[] handCounts = BuildTileCounts(player.hand);

            for (int index = 0; index < player.hand.Count; index++)
            {
                int tileIndex = GetTileIndex(player.hand[index]);
                handCounts[tileIndex]--;
                int shanten = UpgradeMahjong.Mahjong.HandEvaluator.CalculateShanten(handCounts, player.melds.Count);
                int ukeire = CountImprovingTileCopies(handCounts, player.melds.Count, shanten);
                int doraPenalty = IsDoraTile(tileIndex) ? 1 : 0;
                handCounts[tileIndex]++;

                bool isBetter = shanten < bestShanten
                    || shanten == bestShanten && ukeire > bestUkeire
                    || shanten == bestShanten && ukeire == bestUkeire && doraPenalty < bestDoraPenalty;
                if (isBetter)
                {
                    bestShanten = shanten;
                    bestUkeire = ukeire;
                    bestDoraPenalty = doraPenalty;
                    bestIndices.Clear();
                    bestIndices.Add(index);
                }
                else if (shanten == bestShanten && ukeire == bestUkeire && doraPenalty == bestDoraPenalty)
                {
                    bestIndices.Add(index);
                }
            }

            return bestIndices.Count == 0
                ? player.hand.Count - 1
                : bestIndices[UnityEngine.Random.Range(0, bestIndices.Count)];
        }

        private static int CountImprovingTileCopies(int[] counts, int openMeldCount, int currentShanten)
        {
            int result = 0;
            for (int tileIndex = 0; tileIndex < 34; tileIndex++)
            {
                if (counts[tileIndex] >= 4) continue;
                int remainingCopies = 4 - counts[tileIndex];
                counts[tileIndex]++;
                int nextShanten = UpgradeMahjong.Mahjong.HandEvaluator.CalculateShanten(counts, openMeldCount);
                counts[tileIndex]--;
                if (nextShanten < currentShanten)
                    result += remainingCopies;
            }
            return result;
        }

        private bool IsDoraTile(int tileIndex)
        {
            return doraIndicators.Any(indicator =>
                UpgradeMahjong.Mahjong.MahjongTiles.NextDora(GetTileIndex(indicator)) == tileIndex);
        }

        private static bool ShouldAiDeclareRiichi(PlayerState player, int discardIndex)
        {
            if (player == null || player.riichiDeclared || player.score < 1000
                || player.melds.Any(meld => meld.IsOpen)
                || discardIndex < 0 || discardIndex >= player.hand.Count)
                return false;

            int[] counts = BuildTileCounts(player.hand);
            counts[GetTileIndex(player.hand[discardIndex])]--;
            return UpgradeMahjong.Mahjong.HandEvaluator.CalculateShanten(counts, player.melds.Count) == 0;
        }

        private void BeginRiichiSelection()
        {
            List<int> eligibleIndices = GetRiichiDiscardIndices();
            if (eligibleIndices.Count == 0) return;

            declaringRiichi = true;
            riichiDiscardTileIds.Clear();
            foreach (int index in eligibleIndices)
                riichiDiscardTileIds.Add(players[0].hand[index].id);
            UpdateView("리치 선언: 파란 테두리의 패 중 하나를 버리세요.");
        }

        private List<int> GetRiichiDiscardIndices()
        {
            List<int> result = new List<int>();
            if (players.Count == 0) return result;
            PlayerState player = players[0];
            if (!humanMayDiscard || waitingForResponse || waitingForRiichiDrawAction || currentPlayer != 0
                || player.riichiDeclared || player.melds.Any(meld => meld.IsOpen)
                || player.score < 1000 || wall.Count < 4)
                return result;

            int[] counts = BuildTileCounts(player.hand);
            List<UpgradeMahjong.Mahjong.MahjongMeld> fixedMelds = BuildOpenMelds(player);
            for (int index = 0; index < player.hand.Count; index++)
            {
                int tileIndex = GetTileIndex(player.hand[index]);
                counts[tileIndex]--;
                // Use the same concrete winning-tile test as the waiting-tile panel.
                // This keeps the Riichi button and the displayed tenpai waits in sync.
                if (GetWinningTileIndices(counts, fixedMelds).Count > 0)
                    result.Add(index);
                counts[tileIndex]++;
            }
            return result;
        }

        private int GetFreshDrawTileIndex()
        {
            return GetFreshDrawTileIndex(0);
        }

        private int GetFreshDrawTileIndex(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= players.Count) return -1;
            PlayerState player = players[playerIndex];
            Tile freshDraw = player.hand.FirstOrDefault(tile => tile.id == player.freshDrawId);
            return freshDraw == null ? -1 : GetTileIndex(freshDraw);
        }

        private UpgradeMahjong.Mahjong.MahjongGameContext CreateHumanGameContext(
            UpgradeMahjong.Mahjong.MahjongWinMethod winMethod,
            int winningTileIndex)
        {
            return CreatePlayerGameContext(0, winMethod, winningTileIndex);
        }

        private UpgradeMahjong.Mahjong.MahjongGameContext CreatePlayerGameContext(
            int playerIndex,
            UpgradeMahjong.Mahjong.MahjongWinMethod winMethod,
            int winningTileIndex)
        {
            PlayerState player = playerIndex >= 0 && playerIndex < players.Count
                ? players[playerIndex]
                : null;
            UpgradeMahjong.Mahjong.MahjongGameContext context = new UpgradeMahjong.Mahjong.MahjongGameContext
            {
                WinMethod = winMethod,
                WinningTileIndex = winningTileIndex,
                IsDealer = playerIndex == 0,
                SeatWind = 27 + Mathf.Clamp(playerIndex, 0, 3),
                RoundWind = 27,
                Riichi = player != null && player.riichiDeclared,
                Ippatsu = player != null && player.riichiDeclared && player.ippatsuEligible,
                Rinshan = winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo
                    && player != null
                    && player.freshDrawIsRinshan,
                Tenhou = winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo
                    && playerIndex == 0
                    && player != null
                    && player.turnCount == 0
                    && players.All(candidate => candidate.discards.Count == 0 && candidate.melds.Count == 0),
                Rules = riichiRules
            };
            context.DoraIndicators.AddRange(doraIndicators.Select(GetTileIndex));
            return context;
        }

        private static List<UpgradeMahjong.Mahjong.MahjongMeld> BuildOpenMelds(PlayerState player)
        {
            List<UpgradeMahjong.Mahjong.MahjongMeld> result = new List<UpgradeMahjong.Mahjong.MahjongMeld>();
            foreach (MeldState oldMeld in player.melds)
            {
                int[] tiles = oldMeld.Tiles.Select(GetTileIndex).OrderBy(index => index).ToArray();
                if (tiles.Length == 0) continue;
                result.Add(new UpgradeMahjong.Mahjong.MahjongMeld(oldMeld.Type, tiles, oldMeld.IsOpen));
            }
            return result;
        }

        private bool CanHumanRespondToDiscard(Tile discard, int discarder)
        {
            if (discard == null || players.Count == 0) return false;
            return CanRon(discard) || CanPon(discard) || CanKan(discard) || CanChi(discard, discarder);
        }

        private bool CanRon(Tile discard)
        {
            if (!CanRonIgnoringFuriten(discard)) return false;

            List<int> waits = GetWinningTileIndices(
                BuildTileCounts(players[0].hand),
                BuildOpenMelds(players[0]));
            return !IsHumanFuriten(waits);
        }

        private bool CanRonIgnoringFuriten(Tile discard)
        {
            if (discard == null || players.Count == 0 || pendingDiscarder == 0) return false;

            int[] counts = BuildTileCounts(players[0].hand);
            int winningTileIndex = GetTileIndex(discard);
            counts[winningTileIndex]++;
            UpgradeMahjong.Mahjong.MahjongGameContext context = CreateHumanGameContext(
                UpgradeMahjong.Mahjong.MahjongWinMethod.Ron,
                winningTileIndex);
            return UpgradeMahjong.Mahjong.ScoreCalculator.CanWin(counts, BuildOpenMelds(players[0]), context);
        }

        private bool IsHumanFuriten(IList<int> waits)
        {
            if (players.Count == 0 || waits == null || waits.Count == 0) return false;
            if (humanTemporaryFuriten) return true;

            HashSet<int> waitSet = new HashSet<int>(waits);
            return players[0].discards.Any(tile => waitSet.Contains(GetTileIndex(tile)));
        }

        private bool CanTsumo()
        {
            return CanPlayerTsumo(0);
        }

        private bool CanPlayerTsumo(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= players.Count) return false;
            PlayerState player = players[playerIndex];
            int winningTileIndex = GetFreshDrawTileIndex(playerIndex);
            if (winningTileIndex < 0) return false;
            UpgradeMahjong.Mahjong.MahjongGameContext context = CreatePlayerGameContext(
                playerIndex,
                UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo,
                winningTileIndex);
            return UpgradeMahjong.Mahjong.ScoreCalculator.CanWin(
                BuildTileCounts(player.hand),
                BuildOpenMelds(player),
                context);
        }

        private bool CanPlayerRon(int playerIndex, Tile discard, int discarder)
        {
            if (discard == null || playerIndex <= 0 || playerIndex >= players.Count || playerIndex == discarder)
                return false;

            PlayerState player = players[playerIndex];
            int winningTileIndex = GetTileIndex(discard);
            int[] counts = BuildTileCounts(player.hand);
            if (counts[winningTileIndex] >= 4) return false;

            List<UpgradeMahjong.Mahjong.MahjongMeld> openMelds = BuildOpenMelds(player);
            List<int> waits = GetWinningTileIndices(counts, openMelds);
            if (waits.Count == 0 || !waits.Contains(winningTileIndex)) return false;

            HashSet<int> waitSet = new HashSet<int>(waits);
            if (player.discards.Any(tile => waitSet.Contains(GetTileIndex(tile)))) return false;

            counts[winningTileIndex]++;
            UpgradeMahjong.Mahjong.MahjongGameContext context = CreatePlayerGameContext(
                playerIndex,
                UpgradeMahjong.Mahjong.MahjongWinMethod.Ron,
                winningTileIndex);
            return UpgradeMahjong.Mahjong.ScoreCalculator.CanWin(counts, openMelds, context);
        }

        private bool TryResolveAiRon(Tile discard, int discarder)
        {
            for (int offset = 1; offset < players.Count; offset++)
            {
                int candidate = (discarder + offset) % players.Count;
                if (candidate == 0 || !CanPlayerRon(candidate, discard, discarder)) continue;

                CompleteWin(
                    candidate,
                    UpgradeMahjong.Mahjong.MahjongWinMethod.Ron,
                    GetTileIndex(discard),
                    discard,
                    discarder);
                return true;
            }
            return false;
        }

        private bool TryResolveAiCall(Tile discard, int discarder)
        {
            AiMeldDecision decision = FindBestAiCall(discard, discarder);
            if (decision == null) return false;

            PlayerState caller = players[decision.PlayerIndex];
            if (!TryTakePlayerHandTiles(caller, decision.Suit, decision.HandRanks, out List<Tile> consumedTiles))
                return false;

            CancelAllIppatsu();

            Tile calledTile = discard;
            List<Tile> meldTiles = new List<Tile> { calledTile };
            meldTiles.AddRange(consumedTiles);
            meldTiles.Sort((a, b) =>
            {
                int suitCompare = a.suit.CompareTo(b.suit);
                return suitCompare != 0 ? suitCompare : a.rank.CompareTo(b.rank);
            });

            if (discarder >= 0 && discarder < players.Count)
                players[discarder].discards.Remove(discard);
            UpgradeMahjong.Mahjong.MahjongMeldType meldType = decision.Action == ActionType.Chi
                ? UpgradeMahjong.Mahjong.MahjongMeldType.Sequence
                : UpgradeMahjong.Mahjong.MahjongMeldType.Triplet;
            caller.melds.Add(new MeldState(
                meldType,
                meldTiles,
                discarder,
                meldTiles.IndexOf(calledTile)));
            caller.freshDrawId = -1;
            caller.freshDrawIsRinshan = false;
            SortHand(caller.hand);

            waitingForResponse = false;
            choosingMeld = false;
            humanMayDiscard = false;
            currentPlayer = decision.PlayerIndex;
            if (meldChoicePanel != null) meldChoicePanel.gameObject.SetActive(false);
            pendingDiscard = null;
            pendingDiscarder = -1;
            string callLabel = GetActionLabel(decision.Action);
            ShowCallBubble(decision.PlayerIndex, decision.Action);
            turnRoutine = StartCoroutine(AiDiscardAfterCallRoutine(decision.PlayerIndex, callLabel));
            return true;
        }

        private AiMeldDecision FindBestAiCall(Tile discard, int discarder)
        {
            if (discard == null || discarder < 0 || discarder >= players.Count) return null;

            List<AiMeldDecision> candidates = new List<AiMeldDecision>();
            for (int offset = 1; offset < players.Count; offset++)
            {
                int playerIndex = (discarder + offset) % players.Count;
                if (playerIndex == 0) continue;
                PlayerState player = players[playerIndex];
                if (player.riichiDeclared || player.melds.Count >= 4) continue;

                int beforeShanten = UpgradeMahjong.Mahjong.HandEvaluator.CalculateShanten(
                    BuildTileCounts(player.hand),
                    player.melds.Count);
                if (player.hand.Count(tile => tile.suit == discard.suit && tile.rank == discard.rank) >= 2)
                {
                    AddAiCallCandidate(
                        candidates,
                        player,
                        playerIndex,
                        ActionType.Pon,
                        discard.suit,
                        new[] { discard.rank, discard.rank },
                        beforeShanten);
                }

                if (playerIndex == (discarder + 1) % players.Count && discard.suit != Suit.Honor)
                {
                    foreach (int[] ranks in GetChiRankChoices(player, discard))
                    {
                        AddAiCallCandidate(
                            candidates,
                            player,
                            playerIndex,
                            ActionType.Chi,
                            discard.suit,
                            ranks,
                            beforeShanten);
                    }
                }
            }

            return candidates
                .OrderBy(candidate => candidate.ResultingShanten)
                .ThenBy(candidate => candidate.Action == ActionType.Pon ? 0 : 1)
                .ThenBy(candidate => (candidate.PlayerIndex - discarder + players.Count) % players.Count)
                .FirstOrDefault();
        }

        private static void AddAiCallCandidate(
            ICollection<AiMeldDecision> candidates,
            PlayerState player,
            int playerIndex,
            ActionType action,
            Suit suit,
            int[] handRanks,
            int beforeShanten)
        {
            int resultingShanten = CalculateBestShantenAfterCall(player, suit, handRanks);
            // Keep closed hands closed for Riichi unless the call has a clear strategic payoff.
            // This also prevents the AI from repeatedly calling merely because shanten stays even.
            if (resultingShanten >= beforeShanten) return;

            bool alreadyOpen = player.melds.Any(meld => meld.IsOpen);
            bool valuedHonorPon = action == ActionType.Pon
                && suit == Suit.Honor
                && handRanks != null
                && handRanks.Length > 0
                && (handRanks[0] >= 5 || handRanks[0] == 1 || handRanks[0] == playerIndex + 1);
            bool jumpsDirectlyToTenpai = beforeShanten >= 2 && resultingShanten == 0;

            if (!alreadyOpen && !valuedHonorPon && !jumpsDirectlyToTenpai) return;
            if (alreadyOpen && player.melds.Count > 0 && resultingShanten > 0) return;

            candidates.Add(new AiMeldDecision
            {
                PlayerIndex = playerIndex,
                Action = action,
                Suit = suit,
                HandRanks = handRanks,
                ResultingShanten = resultingShanten
            });
        }

        private static int CalculateBestShantenAfterCall(PlayerState player, Suit suit, IEnumerable<int> handRanks)
        {
            int[] counts = BuildTileCounts(player.hand);
            foreach (int rank in handRanks)
            {
                int tileIndex = GetTileIndex(new Tile { suit = suit, rank = rank });
                if (counts[tileIndex] <= 0) return int.MaxValue;
                counts[tileIndex]--;
            }

            int best = int.MaxValue;
            for (int tileIndex = 0; tileIndex < counts.Length; tileIndex++)
            {
                if (counts[tileIndex] <= 0) continue;
                counts[tileIndex]--;
                best = Mathf.Min(
                    best,
                    UpgradeMahjong.Mahjong.HandEvaluator.CalculateShanten(counts, player.melds.Count + 1));
                counts[tileIndex]++;
            }
            return best;
        }

        private static List<int[]> GetChiRankChoices(PlayerState player, Tile discard)
        {
            List<int[]> choices = new List<int[]>();
            if (player == null || discard == null || discard.suit == Suit.Honor) return choices;
            if (HasTile(player, discard.suit, discard.rank - 2) && HasTile(player, discard.suit, discard.rank - 1))
                choices.Add(new[] { discard.rank - 2, discard.rank - 1 });
            if (HasTile(player, discard.suit, discard.rank - 1) && HasTile(player, discard.suit, discard.rank + 1))
                choices.Add(new[] { discard.rank - 1, discard.rank + 1 });
            if (HasTile(player, discard.suit, discard.rank + 1) && HasTile(player, discard.suit, discard.rank + 2))
                choices.Add(new[] { discard.rank + 1, discard.rank + 2 });
            return choices;
        }

        private static bool HasTile(PlayerState player, Suit suit, int rank)
        {
            return rank >= 1 && rank <= 9 && player.hand.Any(tile => tile.suit == suit && tile.rank == rank);
        }

        private static bool TryTakePlayerHandTiles(
            PlayerState player,
            Suit suit,
            IEnumerable<int> ranks,
            out List<Tile> consumedTiles)
        {
            consumedTiles = new List<Tile>();
            List<Tile> availableTiles = new List<Tile>(player.hand);
            foreach (int rank in ranks)
            {
                int index = availableTiles.FindLastIndex(tile => tile.suit == suit && tile.rank == rank);
                if (index < 0)
                {
                    consumedTiles.Clear();
                    return false;
                }
                Tile tile = availableTiles[index];
                availableTiles.RemoveAt(index);
                consumedTiles.Add(tile);
            }

            foreach (Tile tile in consumedTiles)
                player.hand.Remove(tile);
            return true;
        }

        private bool CanPon(Tile discard)
        {
            return players[0].hand.Count(tile => tile.suit == discard.suit && tile.rank == discard.rank) >= 2;
        }

        private bool CanKan(Tile discard)
        {
            return replacementDrawCount < 4
                && wall.Count > 0
                && players[0].hand.Count(tile => tile.suit == discard.suit && tile.rank == discard.rank) >= 3;
        }

        private List<MeldChoice> GetSelfKanChoices()
        {
            List<MeldChoice> choices = new List<MeldChoice>();
            if (players.Count == 0 || !humanMayDiscard || waitingForResponse || currentPlayer != 0
                || replacementDrawCount >= 4 || wall.Count == 0)
                return choices;

            PlayerState player = players[0];
            if (player.freshDrawId < 0) return choices;
            foreach (IGrouping<int, Tile> group in player.hand.GroupBy(GetTileIndex).Where(group => group.Count() == 4))
            {
                Tile sample = group.First();
                MeldChoice choice = new MeldChoice(
                    ActionType.Kan,
                    sample.suit,
                    sample.rank,
                    sample.rank,
                    sample.rank,
                    sample.rank)
                {
                    KanKind = KanChoiceKind.Ankan
                };
                if (!player.riichiDeclared || CanDeclareRiichiAnkan(choice))
                    choices.Add(choice);
            }

            if (!player.riichiDeclared)
            {
                for (int meldIndex = 0; meldIndex < player.melds.Count; meldIndex++)
                {
                    MeldState meld = player.melds[meldIndex];
                    if (meld.Type != UpgradeMahjong.Mahjong.MahjongMeldType.Triplet
                        || meld.Tiles.Count != 3
                        || meld.Tiles.Select(GetTileIndex).Distinct().Count() != 1)
                        continue;

                    Tile sample = meld.Tiles[0];
                    if (!player.hand.Any(tile => tile.suit == sample.suit && tile.rank == sample.rank))
                        continue;

                    choices.Add(new MeldChoice(ActionType.Kan, sample.suit, sample.rank)
                    {
                        KanKind = KanChoiceKind.Kakan,
                        TargetMeldIndex = meldIndex
                    });
                }
            }

            return choices;
        }

        private bool CanDeclareRiichiAnkan(MeldChoice choice)
        {
            if (choice == null || choice.KanKind != KanChoiceKind.Ankan || players.Count == 0) return false;
            PlayerState player = players[0];
            Tile freshDraw = player.hand.FirstOrDefault(tile => tile.id == player.freshDrawId);
            if (freshDraw == null || freshDraw.suit != choice.Suit || freshDraw.rank != choice.HandRanks[0])
                return false;

            int kanTileIndex = GetTileIndex(freshDraw);
            int[] beforeCounts = BuildTileCounts(player.hand);
            beforeCounts[kanTileIndex]--;
            List<int> beforeWaits = GetWinningTileIndices(beforeCounts, BuildOpenMelds(player));
            if (beforeWaits.Count == 0) return false;

            int[] afterCounts = BuildTileCounts(player.hand);
            afterCounts[kanTileIndex] -= 4;
            List<UpgradeMahjong.Mahjong.MahjongMeld> afterMelds = BuildOpenMelds(player);
            afterMelds.Add(new UpgradeMahjong.Mahjong.MahjongMeld(
                UpgradeMahjong.Mahjong.MahjongMeldType.ClosedKan,
                new[] { kanTileIndex, kanTileIndex, kanTileIndex, kanTileIndex },
                false));
            List<int> afterWaits = GetWinningTileIndices(afterCounts, afterMelds);
            return beforeWaits.SequenceEqual(afterWaits);
        }

        private static List<int> GetWinningTileIndices(
            int[] concealedCounts,
            IList<UpgradeMahjong.Mahjong.MahjongMeld> fixedMelds)
        {
            List<int> waits = new List<int>();
            for (int tileIndex = 0; tileIndex < 34; tileIndex++)
            {
                if (concealedCounts[tileIndex] >= 4) continue;
                int[] candidate = (int[])concealedCounts.Clone();
                candidate[tileIndex]++;
                if (UpgradeMahjong.Mahjong.HandEvaluator.IsWinning(candidate, fixedMelds))
                    waits.Add(tileIndex);
            }
            return waits;
        }

        private bool CanChi(Tile discard, int discarder)
        {
            return GetChiRankChoices(discard, discarder).Count > 0;
        }

        private List<int[]> GetChiRankChoices(Tile discard, int discarder)
        {
            List<int[]> choices = new List<int[]>();
            if (discarder != 3 || discard == null || discard.suit == Suit.Honor) return choices;

            if (HasTile(discard.suit, discard.rank - 2) && HasTile(discard.suit, discard.rank - 1))
                choices.Add(new[] { discard.rank - 2, discard.rank - 1 });

            if (HasTile(discard.suit, discard.rank - 1) && HasTile(discard.suit, discard.rank + 1))
                choices.Add(new[] { discard.rank - 1, discard.rank + 1 });

            if (HasTile(discard.suit, discard.rank + 1) && HasTile(discard.suit, discard.rank + 2))
                choices.Add(new[] { discard.rank + 1, discard.rank + 2 });

            return choices;
        }

        private bool HasTile(Suit suit, int rank)
        {
            return rank >= 1 && rank <= 9 && players[0].hand.Any(tile => tile.suit == suit && tile.rank == rank);
        }

        private void RebuildActionButtons()
        {
            foreach (Button button in actionButtons)
                if (button != null) Destroy(button.gameObject);
            actionButtons.Clear();

            // Show the target discard before the player chooses Chi/Pon/Kan.
            callHighlightActive = waitingForResponse
                && pendingDiscard != null
                && (CanChi(pendingDiscard, pendingDiscarder)
                    || CanPon(pendingDiscard)
                    || CanKan(pendingDiscard));
            RefreshDiscardHighlights();

            if (actionPanel == null) return;
            if (choosingMeld)
            {
                actionPanel.gameObject.SetActive(false);
                return;
            }

            if (declaringRiichi)
            {
                actionPanel.gameObject.SetActive(false);
                return;
            }

            List<ActionType> actions = new List<ActionType>();
            if (waitingForRiichiDrawAction)
            {
                if (CanTsumo()) actions.Add(ActionType.Tsumo);
                if (GetSelfKanChoices().Count > 0) actions.Add(ActionType.Kan);
                actions.Add(ActionType.Pass);
            }
            else if (waitingForResponse)
            {
                if (pendingDiscard != null)
                {
                    if (CanRon(pendingDiscard)) actions.Add(ActionType.Ron);
                    if (CanChi(pendingDiscard, pendingDiscarder)) actions.Add(ActionType.Chi);
                    if (CanPon(pendingDiscard)) actions.Add(ActionType.Pon);
                    if (CanKan(pendingDiscard)) actions.Add(ActionType.Kan);
                }
                actions.Add(ActionType.Pass);
            }
            else if (humanMayDiscard && currentPlayer == 0)
            {
                if (CanDeclareNineTerminalsAbortiveDraw())
                {
                    actions.Add(ActionType.AbortiveDraw);
                    actions.Add(ActionType.Pass);
                }
                if (CanTsumo()) actions.Add(ActionType.Tsumo);
                if (GetSelfKanChoices().Count > 0) actions.Add(ActionType.Kan);
                if (GetRiichiDiscardIndices().Count > 0) actions.Add(ActionType.Riichi);
            }

            for (int i = 0; i < actions.Count; i++)
            {
                ActionType action = actions[i];
                Color color = action == ActionType.Ron || action == ActionType.Tsumo
                    ? new Color(0.86f, 0.18f, 0.10f)
                    : action == ActionType.Pass
                        ? new Color(0.28f, 0.31f, 0.36f)
                        : new Color(0.04f, 0.62f, 0.34f);
                Button button = CreateButton(
                    actionPanel,
                    GetActionButtonLabel(action),
                    color,
                    () => OnActionPressed(action),
                    GetActionButtonTexture(action));
                actionButtons.Add(button);
            }

            actionPanel.gameObject.SetActive(actions.Count > 0);
        }

        private void OnActionPressed(ActionType action)
        {
            if (action == ActionType.Tsumo && !waitingForResponse)
            {
                if (!humanMayDiscard || currentPlayer != 0 || !CanTsumo()) return;
                waitingForRiichiDrawAction = false;
                CompleteWin(
                    0,
                    UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo,
                    GetFreshDrawTileIndex());
                return;
            }
            if (action == ActionType.AbortiveDraw && !waitingForResponse)
            {
                if (!CanDeclareNineTerminalsAbortiveDraw()) return;
                CompleteNineTerminalsAbortiveDraw();
                return;
            }
            if (action == ActionType.Pass && !waitingForResponse && CanDeclareNineTerminalsAbortiveDraw())
            {
                abortiveDrawPassed = true;
                UpdateView("구종구패 유국을 넘겼습니다. 버릴 패를 선택하세요.");
                return;
            }
            if (action == ActionType.Riichi && !waitingForResponse && !waitingForRiichiDrawAction)
            {
                BeginRiichiSelection();
                return;
            }
            if (action == ActionType.Kan && !waitingForResponse)
            {
                if (GetSelfKanChoices().Count == 0) return;
                waitingForRiichiDrawAction = false;
                ShowMeldChoices(action);
                return;
            }
            if (action == ActionType.Pass && waitingForRiichiDrawAction)
            {
                if (CanTsumo()) humanTemporaryFuriten = true;
                waitingForRiichiDrawAction = false;
                humanMayDiscard = false;
                UpdateView("리치 상태 — 모패를 자동 타패합니다.");
                turnRoutine = StartCoroutine(AutoDiscardRiichiDrawRoutine(0.12f));
                return;
            }
            if (!waitingForResponse) return;

            if (action == ActionType.Ron)
            {
                if (!CanRon(pendingDiscard)) return;

                int winningTileIndex = GetTileIndex(pendingDiscard);
                CompleteWin(
                    0,
                    UpgradeMahjong.Mahjong.MahjongWinMethod.Ron,
                    winningTileIndex,
                    pendingDiscard,
                    pendingDiscarder);
                return;
            }

            if (action == ActionType.Pass)
            {
                if (CanRon(pendingDiscard))
                    humanTemporaryFuriten = true;
                if (TryResolveAiRon(pendingDiscard, pendingDiscarder))
                    return;
                if (TryResolveAiCall(pendingDiscard, pendingDiscarder))
                    return;
                int nextPlayer = (pendingDiscarder + 1) % 4;
                waitingForResponse = false;
                choosingMeld = false;
                currentPlayer = nextPlayer;
                if (meldChoicePanel != null) meldChoicePanel.gameObject.SetActive(false);
                pendingDiscard = null;
                pendingDiscarder = -1;
                UpdateView(string.Empty);
                StartNextTurn();
                return;
            }

            if (action == ActionType.Chi && !CanChi(pendingDiscard, pendingDiscarder)) return;
            if (action == ActionType.Pon && !CanPon(pendingDiscard)) return;
            if (action == ActionType.Kan && !CanKan(pendingDiscard)) return;

            if (action == ActionType.Chi || action == ActionType.Pon || action == ActionType.Kan)
            {
                ShowMeldChoices(action);
                return;
            }
        }

        private List<MeldChoice> GetMeldChoices(ActionType action)
        {
            List<MeldChoice> choices = new List<MeldChoice>();
            if (pendingDiscard == null) return choices;

            if (action == ActionType.Chi)
            {
                foreach (int[] ranks in GetChiRankChoices(pendingDiscard, pendingDiscarder))
                    choices.Add(new MeldChoice(ActionType.Chi, pendingDiscard.suit, ranks));
            }
            else if (action == ActionType.Pon && CanPon(pendingDiscard))
            {
                choices.Add(new MeldChoice(ActionType.Pon, pendingDiscard.suit, pendingDiscard.rank, pendingDiscard.rank));
            }
            else if (action == ActionType.Kan && CanKan(pendingDiscard))
            {
                choices.Add(new MeldChoice(ActionType.Kan, pendingDiscard.suit, pendingDiscard.rank, pendingDiscard.rank, pendingDiscard.rank)
                {
                    KanKind = KanChoiceKind.Daiminkan
                });
            }

            return choices;
        }

        private void ShowMeldChoices(ActionType action)
        {
            List<MeldChoice> choices = waitingForResponse
                ? GetMeldChoices(action)
                : action == ActionType.Kan
                    ? GetSelfKanChoices()
                    : new List<MeldChoice>();
            if (choices.Count == 0 || meldChoicePanel == null || meldChoiceContent == null) return;

            // Pon/open Kan and a single Chi pattern need no extra confirmation step.
            if (choices.Count == 1)
            {
                choosingMeld = true;
                ConfirmMeldChoice(choices[0]);
                return;
            }

            choosingMeld = true;
            ClearChildren(meldChoiceContent);
            meldChoiceButtons.Clear();

            for (int i = 0; i < choices.Count; i++)
            {
                MeldChoice choice = choices[i];
                Button button = CreateMeldChoiceButton(meldChoiceContent, choice, i, choices.Count);
                meldChoiceButtons.Add(button);
            }

            ApplyMeldChoiceArtwork(action, choices.Count);

            if (threeDimensionalView != null && threeDimensionalView.IsReady)
            {
                int[][] optionTiles = new int[choices.Count][];
                int[] calledPositions = Enumerable.Repeat(-1, choices.Count).ToArray();
                for (int optionIndex = 0; optionIndex < choices.Count; optionIndex++)
                {
                    MeldChoice option = choices[optionIndex];
                    List<int> ranks = BuildMeldPreviewRanks(option);
                    optionTiles[optionIndex] = ranks
                        .Select(rank => GetTileIndex(new Tile { suit = option.Suit, rank = rank, id = -1 }))
                        .ToArray();
                    // Choice previews stay upright. The UI card color now distinguishes
                    // hand tiles from the opponent's discard instead of rotating a tile.
                }
                threeDimensionalView.ShowMeldChoicePreviews(optionTiles, calledPositions);
            }

            meldChoiceTitle.text = waitingForResponse
                ? GetActionLabel(action) + " 방법을 선택하세요  ·  초록색은 손패, 금색 테두리는 버림패입니다"
                : "깡 조합을 선택하세요  ·  안깡 또는 기존 퐁의 가깡";
            meldChoicePanel.gameObject.SetActive(true);
            actionPanel.gameObject.SetActive(false);
            currentStatusMessage = GetActionLabel(action) + "에 사용할 패 조합을 선택하세요. 조합 카드 어디를 눌러도 확정됩니다.";
            RefreshStatusText();
        }

        private void HideMeldChoices()
        {
            choosingMeld = false;
            if (threeDimensionalView != null) threeDimensionalView.HideMeldChoicePreviews();
            if (meldChoicePanel != null) meldChoicePanel.gameObject.SetActive(false);
            if (pendingDiscard != null)
            {
                currentStatusMessage = pendingDiscard.Label + "에 대한 행동을 다시 선택하세요.";
                RefreshStatusText();
            }
            else
            {
                bool returnToRiichiDrawAction = players.Count > 0
                    && players[0].riichiDeclared
                    && currentPlayer == 0
                    && players[0].freshDrawId >= 0;
                waitingForRiichiDrawAction = returnToRiichiDrawAction;
                currentStatusMessage = returnToRiichiDrawAction
                    ? "리치 상태 — 행동을 선택하거나 자동 타패하세요."
                    : "버릴 패를 선택하세요.";
                RefreshStatusText();
            }
            RebuildActionButtons();
        }

        private void ApplyMeldChoiceArtwork(ActionType action, int choiceCount)
        {
            bool usesChiFrame = action == ActionType.Chi && (choiceCount == 2 || choiceCount == 3);
            Texture2D texture = choiceCount == 3 ? threeChoiceBackgroundTexture : twoChoiceBackgroundTexture;
            Sprite frameSprite = usesChiFrame ? GetFullTextureSprite(texture) : null;

            if (meldChoiceBackgroundImage != null)
            {
                meldChoiceBackgroundImage.sprite = frameSprite;
                meldChoiceBackgroundImage.color = frameSprite == null
                    ? new Color(0.035f, 0.11f, 0.105f, 0.97f)
                    : Color.white;
                meldChoiceBackgroundImage.preserveAspect = false;
            }
            if (meldChoicePanelBorder != null)
                meldChoicePanelBorder.enabled = frameSprite == null;

            float panelWidth = choiceCount == 3 ? 1380f : 1120f;
            float panelHeight = choiceCount == 3 ? 460f : 374f;
            float optionWidth = choiceCount == 3 ? 300f : 350f;
            float spacing = choiceCount == 3 ? 114f : 124f;
            if (!usesChiFrame)
            {
                panelWidth = 1000f;
                panelHeight = 340f;
                optionWidth = action == ActionType.Kan ? 360f : 280f;
                spacing = 40f;
            }

            SetAnchors(
                meldChoicePanel,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(panelWidth, panelHeight),
                new Vector2(0f, 190f));
            float contentWidth = optionWidth * choiceCount + spacing * Mathf.Max(0, choiceCount - 1);
            SetAnchors(
                meldChoiceContent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(contentWidth, 210f),
                new Vector2(0f, usesChiFrame ? 8f : -6f));
            if (meldChoiceLayout != null) meldChoiceLayout.spacing = spacing;

            for (int i = 0; i < meldChoiceButtons.Count; i++)
            {
                LayoutElement layout = meldChoiceButtons[i].GetComponent<LayoutElement>();
                if (layout == null) continue;
                layout.preferredWidth = optionWidth;
                layout.minWidth = optionWidth;
                RectTransform rect = meldChoiceButtons[i].GetComponent<RectTransform>();
                if (rect != null) rect.sizeDelta = new Vector2(optionWidth, 210f);
            }
        }

        private Button CreateMeldChoiceButton(Transform parent, MeldChoice choice, int index, int choiceCount)
        {
            GameObject optionObject = new GameObject(GetActionLabel(choice.Action) + "Choice_" + index);
            optionObject.transform.SetParent(parent, false);

            float chiWidth = choiceCount == 3 ? 300f : 350f;
            Vector2 optionSize = new Vector2(
                choice.Action == ActionType.Kan ? 360f : choice.Action == ActionType.Chi ? chiWidth : 280f,
                210f);
            RectTransform optionRect = optionObject.AddComponent<RectTransform>();
            optionRect.sizeDelta = optionSize;

            Image background = optionObject.AddComponent<Image>();
            bool usesChiFrame = choice.Action == ActionType.Chi && (choiceCount == 2 || choiceCount == 3);
            background.color = usesChiFrame
                ? Color.white
                : choice.KanKind == KanChoiceKind.Ankan
                ? new Color(0.10f, 0.30f, 0.23f, 0.96f)
                : choice.KanKind == KanChoiceKind.Kakan
                    ? new Color(0.10f, 0.25f, 0.38f, 0.96f)
                    : choice.Action == ActionType.Chi
                        ? new Color(0.055f, 0.27f, 0.23f, 0.96f)
                        : new Color(0.1f, 0.34f, 0.29f, 0.96f);
            background.sprite = null;
            background.type = Image.Type.Simple;
            Outline optionBorder = optionObject.AddComponent<Outline>();
            optionBorder.enabled = !usesChiFrame;
            optionBorder.effectColor = new Color(0.70f, 1f, 0.78f, 1f);
            optionBorder.effectDistance = new Vector2(4f, -4f);

            Button button = optionObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = usesChiFrame ? new Color(1f, 1f, 1f, 0.02f) : Color.white;
            colors.highlightedColor = usesChiFrame
                ? new Color(1f, 0.82f, 0.32f, 0.28f)
                : new Color(1f, 0.84f, 0.48f);
            colors.pressedColor = new Color(0.82f, 0.68f, 0.34f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => ConfirmMeldChoice(choice));

            LayoutElement optionLayout = optionObject.AddComponent<LayoutElement>();
            optionLayout.preferredWidth = optionSize.x;
            optionLayout.preferredHeight = optionSize.y;
            optionLayout.minWidth = optionSize.x;
            optionLayout.minHeight = optionSize.y;

            Text choiceLabel = CreateText(
                optionObject.transform,
                GetActionLabel(choice.Action) + " 조합 " + (index + 1),
                24,
                TextAnchor.MiddleCenter,
                usesChiFrame ? new Color(0.12f, 0.14f, 0.15f, 1f) : Color.white);
            choiceLabel.fontStyle = FontStyle.Bold;
            SetAnchors(choiceLabel.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(-24f, 44f), new Vector2(0f, -10f));

            GameObject previewRowObject = new GameObject("UprightTilePreviewRow");
            RectTransform previewRow = previewRowObject.AddComponent<RectTransform>();
            previewRow.SetParent(optionObject.transform, false);
            SetAnchors(
                previewRow,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(choice.Action == ActionType.Kan ? 330f : 250f, 132f),
                new Vector2(0f, 18f));
            HorizontalLayoutGroup previewLayout = previewRowObject.AddComponent<HorizontalLayoutGroup>();
            previewLayout.spacing = 5f;
            previewLayout.childAlignment = TextAnchor.MiddleCenter;
            previewLayout.childControlWidth = false;
            previewLayout.childControlHeight = false;

            List<int> previewRanks = BuildMeldPreviewRanks(choice);
            int calledTilePosition = pendingDiscard == null
                ? -1
                : previewRanks.FindIndex(rank => rank == pendingDiscard.rank);
            for (int tilePosition = 0; tilePosition < previewRanks.Count; tilePosition++)
            {
                bool isCalledTile = tilePosition == calledTilePosition;
                Image tileFace = CreatePhysicalTilePreview(
                    previewRow,
                    new Tile { suit = choice.Suit, rank = previewRanks[tilePosition], id = -1 },
                    new Vector2(66f, 98f),
                    false,
                    isCalledTile,
                    "ChoiceTile_" + tilePosition,
                    out GameObject previewContainer);
                if (choice.Action == ActionType.Chi && !isCalledTile)
                {
                    Transform bodyTransform = previewContainer.transform.Find("PhysicalTile/IvoryBody");
                    Image choiceBody = bodyTransform == null ? null : bodyTransform.GetComponent<Image>();
                    if (choiceBody != null)
                        choiceBody.color = new Color(0.68f, 0.92f, 0.76f, 1f);
                    tileFace.color = Color.white;
                }
            }

            return button;
        }

        private List<int> BuildMeldPreviewRanks(MeldChoice choice)
        {
            List<int> previewRanks;
            if (choice.KanKind == KanChoiceKind.Ankan)
            {
                previewRanks = new List<int>(choice.HandRanks);
            }
            else if (choice.KanKind == KanChoiceKind.Kakan)
            {
                previewRanks = new List<int>
                {
                    choice.HandRanks[0], choice.HandRanks[0], choice.HandRanks[0], choice.HandRanks[0]
                };
            }
            else
            {
                previewRanks = new List<int>(choice.HandRanks);
                if (pendingDiscard != null) previewRanks.Add(pendingDiscard.rank);
            }
            previewRanks.Sort();
            return previewRanks;
        }

        private void CreateMeldPreviewTile(Transform parent, Suit suit, int rank, bool isCalledTile)
        {
            Tile previewTile = new Tile { suit = suit, rank = rank, id = -1 };
            GameObject previewObject;
            CreatePhysicalTilePreview(
                parent,
                previewTile,
                new Vector2(82f, 120f),
                isCalledTile,
                isCalledTile,
                "Preview_" + previewTile.Label,
                out previewObject);
        }

        private Image CreatePhysicalTilePreview(
            Transform parent,
            Tile tile,
            Vector2 uprightSize,
            bool sideways,
            bool highlighted,
            string objectName,
            out GameObject containerObject)
        {
            containerObject = new GameObject(objectName);
            RectTransform containerRect = containerObject.AddComponent<RectTransform>();
            containerRect.SetParent(parent, false);

            LayoutElement layout = containerObject.AddComponent<LayoutElement>();
            layout.preferredWidth = sideways ? uprightSize.y : uprightSize.x;
            layout.preferredHeight = sideways ? uprightSize.x : uprightSize.y;

            GameObject visualObject = new GameObject("PhysicalTile");
            RectTransform visualRect = visualObject.AddComponent<RectTransform>();
            visualRect.SetParent(containerRect, false);
            visualRect.anchorMin = new Vector2(0.5f, 0.5f);
            visualRect.anchorMax = new Vector2(0.5f, 0.5f);
            visualRect.pivot = new Vector2(0.5f, 0.5f);
            visualRect.sizeDelta = uprightSize;
            visualRect.anchoredPosition = Vector2.zero;
            visualRect.localRotation = Quaternion.Euler(0f, 0f, sideways ? 90f : 0f);

            Image tileBase = visualObject.AddComponent<Image>();
            tileBase.color = new Color(0.035f, 0.30f, 0.20f, 1f);
            tileBase.raycastTarget = false;
            Shadow shadow = visualObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
            shadow.effectDistance = new Vector2(3f, -4f);

            Outline border = visualObject.AddComponent<Outline>();
            border.effectColor = highlighted
                ? new Color(1f, 0.68f, 0.12f, 1f)
                : new Color(0.10f, 0.11f, 0.10f, 1f);
            border.effectDistance = highlighted ? new Vector2(4f, -4f) : new Vector2(2f, -2f);

            GameObject bodyObject = new GameObject("IvoryBody");
            RectTransform bodyRect = bodyObject.AddComponent<RectTransform>();
            bodyRect.SetParent(visualRect, false);
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            float edge = Mathf.Max(3f, uprightSize.x * 0.045f);
            float baseHeight = Mathf.Max(8f, uprightSize.y * 0.085f);
            bodyRect.offsetMin = new Vector2(edge, baseHeight);
            bodyRect.offsetMax = new Vector2(-edge, -edge);
            Image body = bodyObject.AddComponent<Image>();
            body.color = new Color(0.96f, 0.91f, 0.76f, 1f);
            body.raycastTarget = false;

            GameObject faceObject = new GameObject("TileFace");
            RectTransform faceRect = faceObject.AddComponent<RectTransform>();
            faceRect.SetParent(bodyRect, false);
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            float faceInset = Mathf.Max(5f, uprightSize.x * 0.075f);
            faceRect.offsetMin = new Vector2(faceInset, faceInset);
            faceRect.offsetMax = new Vector2(-faceInset, -faceInset);
            Image face = faceObject.AddComponent<Image>();
            face.sprite = GetTileSprite(tile);
            ApplyTileArtworkStyle(face, tile);
            face.preserveAspect = true;
            face.raycastTarget = false;
            return face;
        }

        private void ConfirmMeldChoice(MeldChoice choice)
        {
            if (!choosingMeld || choice == null) return;

            if (!waitingForResponse)
            {
                ConfirmSelfKanChoice(choice);
                return;
            }

            if (pendingDiscard == null) return;

            MeldChoice validChoice = GetMeldChoices(choice.Action)
                .FirstOrDefault(candidate => candidate.Suit == choice.Suit
                    && candidate.KanKind == choice.KanKind
                    && candidate.HandRanks.SequenceEqual(choice.HandRanks));
            if (validChoice == null) return;

            // Choosing a call instead of an available Ron is a Ron pass for furiten purposes.
            if (CanRon(pendingDiscard))
                humanTemporaryFuriten = true;

            if (!TryTakeHandTiles(validChoice.Suit, validChoice.HandRanks, out List<Tile> consumedTiles)) return;

            CancelAllIppatsu();

            Tile calledTile = pendingDiscard;
            int calledFromSeat = pendingDiscarder;
            List<Tile> meld = new List<Tile> { calledTile };
            meld.AddRange(consumedTiles);

            meld.Sort((a, b) =>
            {
                int suitCompare = a.suit.CompareTo(b.suit);
                return suitCompare != 0 ? suitCompare : a.rank.CompareTo(b.rank);
            });

            if (pendingDiscarder >= 0 && pendingDiscarder < players.Count)
                players[pendingDiscarder].discards.Remove(pendingDiscard);
            UpgradeMahjong.Mahjong.MahjongMeldType meldType = choice.Action == ActionType.Chi
                ? UpgradeMahjong.Mahjong.MahjongMeldType.Sequence
                : choice.Action == ActionType.Pon
                    ? UpgradeMahjong.Mahjong.MahjongMeldType.Triplet
                    : UpgradeMahjong.Mahjong.MahjongMeldType.OpenKan;
            players[0].melds.Add(new MeldState(
                meldType,
                meld,
                calledFromSeat,
                meld.IndexOf(calledTile)));
            players[0].freshDrawId = -1;
            players[0].freshDrawIsRinshan = false;
            if (choice.Action == ActionType.Kan)
                DrawKanReplacement(players[0]);
            SortHand(players[0].hand);
            waitingForResponse = false;
            choosingMeld = false;
            currentPlayer = 0;
            humanMayDiscard = true;
            meldChoicePanel.gameObject.SetActive(false);
            pendingDiscard = null;
            pendingDiscarder = -1;
            ShowCallBubble(0, choice.Action);
            UpdateView(GetActionLabel(choice.Action) + " 완료 — 버릴 패를 선택하세요.");
        }

        private void ConfirmSelfKanChoice(MeldChoice choice)
        {
            if (choice.Action != ActionType.Kan || waitingForResponse || players.Count == 0) return;
            MeldChoice validChoice = GetSelfKanChoices().FirstOrDefault(candidate =>
                candidate.Suit == choice.Suit
                && candidate.KanKind == choice.KanKind
                && candidate.TargetMeldIndex == choice.TargetMeldIndex
                && candidate.HandRanks.SequenceEqual(choice.HandRanks));
            if (validChoice == null) return;

            PlayerState player = players[0];
            string kanLabel;
            if (validChoice.KanKind == KanChoiceKind.Ankan)
            {
                if (!TryTakeHandTiles(validChoice.Suit, validChoice.HandRanks, out List<Tile> concealedTiles)) return;
                concealedTiles.Sort((a, b) => a.id.CompareTo(b.id));
                player.melds.Add(new MeldState(
                    UpgradeMahjong.Mahjong.MahjongMeldType.ClosedKan,
                    concealedTiles));
                kanLabel = "안깡";
            }
            else if (validChoice.KanKind == KanChoiceKind.Kakan)
            {
                if (validChoice.TargetMeldIndex < 0 || validChoice.TargetMeldIndex >= player.melds.Count) return;
                MeldState targetMeld = player.melds[validChoice.TargetMeldIndex];
                if (targetMeld.Type != UpgradeMahjong.Mahjong.MahjongMeldType.Triplet
                    || !TryTakeHandTiles(validChoice.Suit, validChoice.HandRanks, out List<Tile> addedTiles)
                    || addedTiles.Count != 1)
                    return;
                Tile originalCalledTile = targetMeld.CalledTileIndex >= 0
                    && targetMeld.CalledTileIndex < targetMeld.Tiles.Count
                        ? targetMeld.Tiles[targetMeld.CalledTileIndex]
                        : null;
                Tile addedTile = addedTiles[0];
                targetMeld.Tiles.Add(addedTile);
                targetMeld.Tiles.Sort((a, b) => a.id.CompareTo(b.id));
                targetMeld.CalledTileIndex = originalCalledTile == null
                    ? -1
                    : targetMeld.Tiles.IndexOf(originalCalledTile);
                targetMeld.AddedTileIndex = targetMeld.Tiles.IndexOf(addedTile);
                targetMeld.Type = UpgradeMahjong.Mahjong.MahjongMeldType.AddedKan;
                kanLabel = "가깡";
            }
            else
            {
                return;
            }

            CancelAllIppatsu();
            player.freshDrawId = -1;
            player.freshDrawIsRinshan = false;
            DrawKanReplacement(player);
            SortHand(player.hand);
            choosingMeld = false;
            currentPlayer = 0;
            humanMayDiscard = true;
            meldChoicePanel.gameObject.SetActive(false);
            ShowCallBubble(0, ActionType.Kan);
            if (player.riichiDeclared)
                BeginHumanTurn(false);
            else
                UpdateView(kanLabel + " 완료 — 버릴 패를 선택하세요.");
        }

        private void DrawKanReplacement(PlayerState player)
        {
            RevealNextDoraIndicator();
            Tile replacementTile = DrawReplacementTile();
            if (replacementTile == null) return;
            player.hand.Add(replacementTile);
            player.freshDrawId = replacementTile.id;
            player.freshDrawIsRinshan = true;
        }

        private void CancelAllIppatsu()
        {
            foreach (PlayerState player in players)
                player.ippatsuEligible = false;
        }

        private bool TryTakeHandTiles(Suit suit, IEnumerable<int> ranks, out List<Tile> consumedTiles)
        {
            consumedTiles = new List<Tile>();
            List<Tile> availableTiles = new List<Tile>(players[0].hand);

            foreach (int rank in ranks)
            {
                int index = availableTiles.FindLastIndex(tile => tile.suit == suit && tile.rank == rank);
                if (index < 0)
                {
                    consumedTiles.Clear();
                    return false;
                }

                Tile tile = availableTiles[index];
                availableTiles.RemoveAt(index);
                consumedTiles.Add(tile);
            }

            foreach (Tile tile in consumedTiles)
                players[0].hand.Remove(tile);

            return true;
        }

        private static int GetOpenTileCount(PlayerState player)
        {
            return player.melds.Sum(meld => meld.Tiles.Count);
        }

        private void CompleteWin(
            int winnerIndex,
            UpgradeMahjong.Mahjong.MahjongWinMethod winMethod,
            int winningTileIndex,
            Tile winningDiscard = null,
            int discarderIndex = -1)
        {
            if (winnerIndex < 0 || winnerIndex >= players.Count) return;

            PlayerState winner = players[winnerIndex];
            int winningTileId = winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Ron
                && winningDiscard != null
                    ? winningDiscard.id
                    : winner.freshDrawId;
            if (winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Ron && winningDiscard != null)
            {
                if (discarderIndex >= 0 && discarderIndex < players.Count)
                    players[discarderIndex].discards.Remove(winningDiscard);
                winner.hand.Add(winningDiscard);
                SortHand(winner.hand);
            }

            revealedWinnerIndex = winnerIndex;
            revealedWinningTileHandIndex = winner.hand.FindIndex(tile => tile.id == winningTileId);

            waitingForResponse = false;
            waitingForRiichiDrawAction = false;
            choosingMeld = false;
            declaringRiichi = false;
            humanMayDiscard = false;
            currentPlayer = winnerIndex;
            pendingDiscard = null;
            pendingDiscarder = -1;
            turnRoutine = null;
            if (meldChoicePanel != null) meldChoicePanel.gameObject.SetActive(false);

            string methodLabel = winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo ? "쯔모" : "론";
            ShowCallBubble(
                winnerIndex,
                winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo
                    ? ActionType.Tsumo
                    : ActionType.Ron);
            UpdateView(winner.name + " " + methodLabel + " 화료");
            ShowWinResult(winnerIndex, winMethod, winningTileIndex);
            winner.freshDrawId = -1;
            winner.freshDrawIsRinshan = false;
        }

        private bool CanDeclareNineTerminalsAbortiveDraw()
        {
            if (abortiveDrawPassed || players.Count != 4 || currentPlayer != 0 || !humanMayDiscard)
                return false;
            if (waitingForResponse || waitingForRiichiDrawAction || choosingMeld || declaringRiichi)
                return false;
            if (players[0].hand.Count % 3 != 2 || players.Any(player => player.discards.Count > 0 || player.melds.Count > 0))
                return false;

            int distinctTerminalHonorTypes = players[0].hand
                .Where(tile => tile.suit == Suit.Honor || tile.rank == 1 || tile.rank == 9)
                .Select(GetTileIndex)
                .Distinct()
                .Count();
            return distinctTerminalHonorTypes >= 9;
        }

        private void CompleteNineTerminalsAbortiveDraw()
        {
            if (turnRoutine != null)
            {
                StopCoroutine(turnRoutine);
                turnRoutine = null;
            }
            waitingForResponse = false;
            waitingForRiichiDrawAction = false;
            choosingMeld = false;
            declaringRiichi = false;
            humanMayDiscard = false;
            pendingDiscard = null;
            pendingDiscarder = -1;
            revealAllHands = true;
            if (meldChoicePanel != null) meldChoicePanel.gameObject.SetActive(false);

            UpdateView("유국 — 구종구패");
            if (resultOverlay != null)
            {
                if (resultOverlayBackground != null)
                    resultOverlayBackground.color = new Color(0.015f, 0.02f, 0.035f, 0.58f);
                resultTitleText.text = "유국  ·  구종구패";
                resultDetailText.text = "첫 순에 서로 다른 1·9·자패가 9종 이상 모였습니다.\n전원의 손패를 공개합니다.\n\n3초 뒤 다음 국을 시작합니다.";
                resultOverlay.SetActive(true);
            }
            RebuildActionButtons();
            turnRoutine = StartCoroutine(RestartAfterAbortiveDrawRoutine());
        }

        private IEnumerator RestartAfterAbortiveDrawRoutine()
        {
            yield return new WaitForSeconds(3f);
            turnRoutine = null;
            StartMatch();
        }

        private void ShowWinResult(
            int winnerIndex,
            UpgradeMahjong.Mahjong.MahjongWinMethod winMethod,
            int winningTileIndex)
        {
            if (resultOverlay == null || winnerIndex < 0 || winnerIndex >= players.Count) return;
            if (resultOverlayBackground != null)
                resultOverlayBackground.color = new Color(0.015f, 0.02f, 0.035f, 0.96f);

            PlayerState winner = players[winnerIndex];
            UpgradeMahjong.Mahjong.MahjongGameContext context = CreatePlayerGameContext(
                winnerIndex,
                winMethod,
                winningTileIndex);
            UpgradeMahjong.Mahjong.ScoreBreakdown score = UpgradeMahjong.Mahjong.ScoreCalculator.FindBestScore(
                BuildTileCounts(winner.hand),
                BuildOpenMelds(winner),
                context);

            string methodLabel = winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Tsumo ? "쯔모" : "론";
            resultTitleText.text = winner.name + "  " + methodLabel + " 화료";
            if (score == null)
            {
                resultDetailText.text = "완성된 형태이지만 유효한 역이 없어 화료할 수 없습니다.";
            }
            else
            {
                List<string> scoreLines = new List<string>();
                foreach (UpgradeMahjong.Mahjong.IYaku yaku in score.Yaku.Yaku)
                {
                    if (yaku.YakumanValue > 0)
                    {
                        string yakumanValue = yaku.YakumanValue == 1
                            ? "역만"
                            : yaku.YakumanValue + "배 역만";
                        scoreLines.Add(GetYakuDisplayName(yaku.Name) + "  " + yakumanValue);
                        continue;
                    }

                    int han = yaku.Check(score.Combination, context);
                    if (han > 0)
                        scoreLines.Add(GetYakuDisplayName(yaku.Name) + "  " + han + "판");
                }
                if (score.DoraHan > 0)
                    scoreLines.Add("도라  " + score.DoraHan + "판");

                string points = winMethod == UpgradeMahjong.Mahjong.MahjongWinMethod.Ron
                    ? score.RonPoints.ToString("N0") + "점"
                    : context.IsDealer
                        ? "각자 " + score.TsumoDealerPayment.ToString("N0") + "점"
                        : "친 " + score.TsumoDealerPayment.ToString("N0") + "점 / 자 " + score.TsumoNonDealerPayment.ToString("N0") + "점";
                string handValue = score.YakumanCount > 0
                    ? score.LimitName
                    : score.Han + "판 " + score.Fu + "부"
                        + (string.IsNullOrEmpty(score.LimitName) ? string.Empty : "  ·  " + score.LimitName);
                resultDetailText.text = string.Join("\n", scoreLines)
                    + "\n\n총 " + handValue
                    + "\n" + points;
            }

            resultOverlay.SetActive(true);
            RebuildActionButtons();
        }

        private static string GetYakuDisplayName(string yakuName)
        {
            return yakuName == "평화" ? "핑후" : yakuName;
        }

        private static bool IsSequenceTiles(int[] tiles)
        {
            return tiles.Length == 3
                && UpgradeMahjong.Mahjong.MahjongTiles.GetSuit(tiles[0]) != UpgradeMahjong.Mahjong.MahjongSuit.Honor
                && tiles[1] == tiles[0] + 1
                && tiles[2] == tiles[0] + 2
                && tiles[1] / 9 == tiles[0] / 9
                && tiles[2] / 9 == tiles[0] / 9;
        }

        private string GetHandStateSummary(PlayerState player)
        {
            int shanten = CalculateShanten(player);
            if (shanten < 0) return "화료 가능";
            if (shanten == 0) return "텐파이";
            return shanten + "샹텐";
        }

        private static int CalculateShanten(PlayerState player)
        {
            int[] counts = BuildTileCounts(player.hand);
            return CalculateShanten(counts, player.melds.Count);
        }

        private static int[] BuildTileCounts(List<Tile> hand)
        {
            int[] counts = new int[34];
            foreach (Tile tile in hand)
                counts[GetTileIndex(tile)]++;
            return counts;
        }

        private static int CalculateShanten(int[] counts, int openMeldCount)
        {
            int normalShanten = 8;
            SearchStandardShanten(counts, 0, openMeldCount, 0, 0, ref normalShanten);

            int chiitoitsuShanten = openMeldCount == 0 ? CalculateChiitoitsuShanten(counts) : 99;
            int kokushiShanten = openMeldCount == 0 ? CalculateKokushiShanten(counts) : 99;
            return Mathf.Min(normalShanten, Mathf.Min(chiitoitsuShanten, kokushiShanten));
        }

        private static void SearchStandardShanten(int[] counts, int startIndex, int melds, int pairs, int taatsu, ref int best)
        {
            while (startIndex < counts.Length && counts[startIndex] == 0) startIndex++;
            if (startIndex >= counts.Length)
            {
                int usableTaatsu = Mathf.Min(taatsu, 4 - melds);
                best = Mathf.Min(best, 8 - melds * 2 - usableTaatsu - pairs);
                return;
            }

            if (melds > 4 || taatsu > 4) return;

            counts[startIndex]--;
            SearchStandardShanten(counts, startIndex, melds, pairs, taatsu, ref best);
            counts[startIndex]++;

            if (counts[startIndex] >= 3)
            {
                counts[startIndex] -= 3;
                SearchStandardShanten(counts, startIndex, melds + 1, pairs, taatsu, ref best);
                counts[startIndex] += 3;
            }

            if (pairs == 0 && counts[startIndex] >= 2)
            {
                counts[startIndex] -= 2;
                SearchStandardShanten(counts, startIndex, melds, 1, taatsu, ref best);
                counts[startIndex] += 2;
            }

            if (counts[startIndex] >= 2)
            {
                counts[startIndex] -= 2;
                SearchStandardShanten(counts, startIndex, melds, pairs, taatsu + 1, ref best);
                counts[startIndex] += 2;
            }

            if (IsSuitedIndex(startIndex))
            {
                int rankOffset = startIndex % 9;
                if (rankOffset <= 6 && counts[startIndex + 1] > 0 && counts[startIndex + 2] > 0)
                {
                    counts[startIndex]--;
                    counts[startIndex + 1]--;
                    counts[startIndex + 2]--;
                    SearchStandardShanten(counts, startIndex, melds + 1, pairs, taatsu, ref best);
                    counts[startIndex]++;
                    counts[startIndex + 1]++;
                    counts[startIndex + 2]++;
                }

                if (rankOffset <= 7 && counts[startIndex + 1] > 0)
                {
                    counts[startIndex]--;
                    counts[startIndex + 1]--;
                    SearchStandardShanten(counts, startIndex, melds, pairs, taatsu + 1, ref best);
                    counts[startIndex]++;
                    counts[startIndex + 1]++;
                }

                if (rankOffset <= 6 && counts[startIndex + 2] > 0)
                {
                    counts[startIndex]--;
                    counts[startIndex + 2]--;
                    SearchStandardShanten(counts, startIndex, melds, pairs, taatsu + 1, ref best);
                    counts[startIndex]++;
                    counts[startIndex + 2]++;
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

            return 6 - pairs + Mathf.Max(0, 7 - unique);
        }

        private static int CalculateKokushiShanten(int[] counts)
        {
            int[] terminalsAndHonors = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            int unique = 0;
            bool hasPair = false;
            foreach (int index in terminalsAndHonors)
            {
                if (counts[index] > 0) unique++;
                if (counts[index] >= 2) hasPair = true;
            }

            return 13 - unique - (hasPair ? 1 : 0);
        }

        private static int GetTileIndex(Tile tile)
        {
            if (tile.suit == Suit.Man) return tile.rank - 1;
            if (tile.suit == Suit.Pin) return 9 + tile.rank - 1;
            if (tile.suit == Suit.Sou) return 18 + tile.rank - 1;
            return 27 + tile.rank - 1;
        }

        private static bool IsSuitedIndex(int index)
        {
            return index >= 0 && index < 27;
        }

        private string GetActionLabel(ActionType action)
        {
            switch (action)
            {
                case ActionType.Chi: return "치";
                case ActionType.Pon: return "퐁";
                case ActionType.Kan: return "깡";
                case ActionType.Riichi: return "리치";
                case ActionType.Ron: return "론";
                case ActionType.Tsumo: return "쯔모";
                case ActionType.Kita: return "북빼기";
                case ActionType.AbortiveDraw: return "유국";
                default: return "패스";
            }
        }

        private string GetActionButtonLabel(ActionType action)
        {
            if (action == ActionType.Riichi && CanTsumo())
                return "리치 (쯔모 포기)";
            if (action == ActionType.Pass && waitingForRiichiDrawAction)
                return "자동 타패";
            return GetActionLabel(action);
        }

        private Texture2D GetActionButtonTexture(ActionType action)
        {
            switch (action)
            {
                case ActionType.Chi: return chiActionButtonTexture;
                case ActionType.Pon: return ponActionButtonTexture;
                case ActionType.Kan: return kanActionButtonTexture;
                case ActionType.Pass: return passActionButtonTexture;
                case ActionType.Ron: return ronActionButtonTexture;
                case ActionType.Tsumo: return tsumoActionButtonTexture;
                case ActionType.AbortiveDraw: return abortiveDrawActionButtonTexture;
                default: return null;
            }
        }

        private Sprite GetActionButtonSprite(Texture2D texture)
        {
            if (texture == null) return null;
            if (actionButtonSprites.TryGetValue(texture, out Sprite cached) && cached != null)
                return cached;

            // The supplied PNGs have a generous transparent canvas. Crop that canvas so the
            // actual pill artwork fills the grid cell without stretching or looking undersized.
            Rect artworkRect = new Rect(
                texture.width * 0.07f,
                texture.height * 0.16f,
                texture.width * 0.86f,
                texture.height * 0.68f);
            Sprite sprite = Sprite.Create(texture, artworkRect, new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name + "_ButtonSprite";
            actionButtonSprites[texture] = sprite;
            return sprite;
        }

        private Sprite GetFullTextureSprite(Texture2D texture)
        {
            if (texture == null) return null;
            if (actionButtonSprites.TryGetValue(texture, out Sprite cached) && cached != null)
                return cached;

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = texture.name + "_FullSprite";
            actionButtonSprites[texture] = sprite;
            return sprite;
        }

        private Button CreateButton(
            Transform parent,
            string label,
            Color color,
            UnityAction onClick,
            Texture2D artwork = null)
        {
            Button button = null;
            GameObject buttonObject;
            if (sceneLayout != null && sceneLayout.ActionButtonPrefab != null)
            {
                button = Instantiate(sceneLayout.ActionButtonPrefab, parent);
                buttonObject = button.gameObject;
                buttonObject.name = "Action_" + label;
            }
            else
            {
                buttonObject = new GameObject("Action_" + label);
                buttonObject.transform.SetParent(parent, false);
            }

            Image image = buttonObject.GetComponent<Image>();
            if (image == null) image = buttonObject.AddComponent<Image>();
            Sprite artworkSprite = GetActionButtonSprite(artwork);
            bool usesArtwork = artworkSprite != null;
            image.color = usesArtwork ? Color.white : color;
            if (usesArtwork)
            {
                image.sprite = artworkSprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }
            else
            {
                ApplyRoundedImage(image);
            }
            Outline buttonBorder = buttonObject.GetComponent<Outline>();
            if (buttonBorder == null) buttonBorder = buttonObject.AddComponent<Outline>();
            buttonBorder.enabled = !usesArtwork;
            buttonBorder.effectColor = new Color(0.62f, 0.76f, 0.70f, 0.92f);
            buttonBorder.effectDistance = new Vector2(1.25f, -1.25f);
            if (button == null) button = buttonObject.GetComponent<Button>();
            if (button == null) button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.9f, 0.66f);
            colors.pressedColor = new Color(0.95f, 0.72f, 0.34f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            Text text = buttonObject.GetComponentInChildren<Text>(true);
            if (text == null)
                text = CreateText(buttonObject.transform, label, 29, TextAnchor.MiddleCenter, Color.white);
            text.font = defaultFont;
            text.fontSize = 29;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            text.gameObject.SetActive(!usesArtwork);
            Outline textOutline = text.GetComponent<Outline>();
            if (textOutline == null) textOutline = text.gameObject.AddComponent<Outline>();
            textOutline.effectColor = new Color(0f, 0f, 0f, 0.68f);
            textOutline.effectDistance = new Vector2(1f, -1f);
            RectTransform textRect = text.rectTransform;
            StretchToParent(textRect);
            return button;
        }

        private Button CreateTileButton(Transform parent, Tile tile, UnityAction onClick)
        {
            Vector2 hitSize = sceneLayout == null ? new Vector2(46f, 168f) : sceneLayout.HandTileHitSize;
            Vector2 visualSize = sceneLayout == null ? new Vector2(94f, 138f) : sceneLayout.HandTileVisualSize;
            Vector2 visualPosition = sceneLayout == null ? new Vector2(0f, -15f) : sceneLayout.HandTileVisualPosition;
            float hoverAmount = sceneLayout == null ? 30f : sceneLayout.HandHoverLift;

            GameObject buttonObject;
            if (sceneLayout != null && sceneLayout.HandTilePrefab != null)
            {
                buttonObject = Instantiate(sceneLayout.HandTilePrefab, parent);
                buttonObject.name = "Tile_" + tile.Label;
            }
            else
            {
                buttonObject = new GameObject("Tile_" + tile.Label);
                buttonObject.transform.SetParent(parent, false);
            }

            Image hitArea = buttonObject.GetComponent<Image>();
            if (hitArea == null) hitArea = buttonObject.AddComponent<Image>();
            hitArea.color = new Color(1f, 1f, 1f, 0f);
            hitArea.raycastTarget = false;

            Transform existingHoverArea = buttonObject.transform.Find("HoverHitArea");
            GameObject hoverAreaObject = existingHoverArea == null
                ? new GameObject("HoverHitArea")
                : existingHoverArea.gameObject;
            RectTransform hoverAreaRect = hoverAreaObject.GetComponent<RectTransform>();
            if (hoverAreaRect == null) hoverAreaRect = hoverAreaObject.AddComponent<RectTransform>();
            if (existingHoverArea == null) hoverAreaRect.SetParent(buttonObject.transform, false);
            hoverAreaRect.anchorMin = new Vector2(0.5f, 0.5f);
            hoverAreaRect.anchorMax = new Vector2(0.5f, 0.5f);
            hoverAreaRect.pivot = new Vector2(0.5f, 0.5f);
            hoverAreaRect.sizeDelta = hitSize;
            hoverAreaRect.anchoredPosition = Vector2.zero;
            Image hoverArea = hoverAreaObject.GetComponent<Image>();
            if (hoverArea == null) hoverArea = hoverAreaObject.AddComponent<Image>();
            hoverArea.color = new Color(1f, 1f, 1f, 0f);
            hoverArea.raycastTarget = true;

            Transform existingVisual = buttonObject.transform.Find("TileVisual");
            GameObject visualObject = existingVisual == null
                ? new GameObject("TileVisual")
                : existingVisual.gameObject;
            RectTransform visualRect = visualObject.GetComponent<RectTransform>();
            if (visualRect == null) visualRect = visualObject.AddComponent<RectTransform>();
            if (existingVisual == null) visualRect.SetParent(buttonObject.transform, false);
            visualRect.anchorMin = new Vector2(0.5f, 0.5f);
            visualRect.anchorMax = new Vector2(0.5f, 0.5f);
            visualRect.pivot = new Vector2(0.5f, 0.5f);
            visualRect.sizeDelta = visualSize;
            visualRect.anchoredPosition = visualPosition;

            Image image = visualObject.GetComponent<Image>();
            if (image == null) image = visualObject.AddComponent<Image>();
            image.sprite = GetTileSprite(tile);
            ApplyTileArtworkStyle(image, tile);
            image.preserveAspect = true;
            image.raycastTarget = false;

            Button button = buttonObject.GetComponent<Button>();
            if (button == null) button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.88f, 0.58f);
            colors.pressedColor = new Color(0.95f, 0.68f, 0.3f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            TileHoverLift hoverLift = buttonObject.GetComponent<TileHoverLift>();
            if (hoverLift == null) hoverLift = buttonObject.AddComponent<TileHoverLift>();
            int tileTypeIndex = GetTileIndex(tile);
            hoverLift.Configure(
                visualRect,
                hoverAmount,
                () => SetHoveredDiscardTile(tileTypeIndex),
                () => ClearHoveredDiscardTile(tileTypeIndex));

            if (image.sprite == null)
            {
                Text text = CreateText(visualObject.transform, tile.Label, 24, TextAnchor.MiddleCenter, Color.black);
                RectTransform textRect = text.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
            }

            return button;
        }

        private Sprite GetTileSprite(Tile tile)
        {
            int index = GetTileIndex(tile);
            return index >= 0 && index < tileSprites.Length ? tileSprites[index] : null;
        }

        private void ApplyTileArtworkStyle(Image image, Tile tile)
        {
            if (image == null) return;
            image.color = Color.white;
            int manIndex = tile != null && tile.suit == Suit.Man ? tile.rank - 1 : -1;
            image.material = manIndex >= 0
                && manIndex < manTileRedMaterials.Length
                && manTileRedMaterials[manIndex] != null
                    ? manTileRedMaterials[manIndex]
                    : null;
        }

        private void SetHoveredDiscardTile(int tileIndex)
        {
            hoveredDiscardTileIndex = tileIndex;
            if (threeDimensionalView != null)
                threeDimensionalView.SetHighlightedTileIndex(tileIndex);
            RefreshDiscardHighlights();
            RefreshStatusText();
        }

        private void ClearHoveredDiscardTile(int tileIndex)
        {
            if (hoveredDiscardTileIndex != tileIndex) return;
            hoveredDiscardTileIndex = -1;
            if (threeDimensionalView != null)
                threeDimensionalView.SetHighlightedTileIndex(-1);
            RefreshDiscardHighlights();
            RefreshStatusText();
        }

        private void RebuildHandButtons()
        {
            ClearChildren(handPanel);
            handButtons.Clear();
            handGapObjects.Clear();
            if (handPanel == null || players.Count == 0) return;

            for (int i = 0; i < players[0].hand.Count; i++)
            {
                int tileIndex = i;
                Tile tile = players[0].hand[i];
                bool isFreshDraw = tile.id == players[0].freshDrawId;
                bool isRiichiDiscard = declaringRiichi && riichiDiscardTileIds.Contains(tile.id);
                if (isFreshDraw && i > 0)
                {
                    GameObject gapObject = new GameObject("FreshDrawGap");
                    gapObject.transform.SetParent(handPanel, false);
                    LayoutElement gapLayout = gapObject.AddComponent<LayoutElement>();
                    gapLayout.preferredWidth = sceneLayout == null ? 12f : sceneLayout.FreshDrawGap;
                    gapLayout.preferredHeight = 1f;
                    handGapObjects.Add(gapObject);
                }

                Button button = CreateTileButton(handPanel, tile, () => OnHandTileClicked(tileIndex));
                LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
                Vector2 slotSize = sceneLayout == null
                    ? new Vector2(46f, 168f)
                    : sceneLayout.HandTileSlotSize;
                layout.preferredWidth = slotSize.x;
                layout.preferredHeight = slotSize.y;
                if (i == selectedTileIndex || isFreshDraw || isRiichiDiscard)
                {
                    Transform visual = button.transform.Find("TileVisual");
                    Outline outline = (visual == null ? button.gameObject : visual.gameObject).AddComponent<Outline>();
                    outline.effectColor = isRiichiDiscard
                        ? new Color(0.2f, 0.72f, 1f, 0.98f)
                        : i == selectedTileIndex
                        ? new Color(0.98f, 0.58f, 0.16f, 0.98f)
                        : new Color(1f, 0.93f, 0.58f, 0.95f);
                    outline.effectDistance = new Vector2(2f, 2f);
                }
                handButtons.Add(button);
            }
        }

        private void RefreshHandButtonsIfNeeded()
        {
            string nextStateKey = GetHandViewStateKey();
            if (threeDimensionalView != null
                && threeDimensionalView.IsReady
                && threeDimensionalView.HideLegacyTileUi)
            {
                renderedHandStateKey = nextStateKey;
                return;
            }
            if (nextStateKey == renderedHandStateKey && handPanel != null && handPanel.childCount > 0)
                return;

            // A genuine hand change invalidates the old pointer target. Status-only updates do not.
            hoveredDiscardTileIndex = -1;
            RebuildHandButtons();
            renderedHandStateKey = nextStateKey;
        }

        private string GetHandViewStateKey()
        {
            if (players.Count == 0) return "empty";

            PlayerState player = players[0];
            string handIds = string.Join(",", player.hand.Select(tile => tile.id));
            string riichiIds = declaringRiichi
                ? string.Join(",", riichiDiscardTileIds.OrderBy(id => id))
                : string.Empty;
            return handIds
                + "|draw:" + player.freshDrawId
                + "|selected:" + selectedTileIndex
                + "|declaring:" + declaringRiichi
                + "|riichi:" + riichiIds;
        }

        private void RefreshBoardHistory()
        {
            if (players.Count == 0) return;

            if (threeDimensionalView != null && threeDimensionalView.IsReady)
            {
                threeDimensionalView.Render(CreateThreeDimensionalSnapshot());
                threeDimensionalView.SetHighlightedTileIndex(hoveredDiscardTileIndex);
                return;
            }

            discardTileVisuals.Clear();

            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                ClearChildren(discardContentPanels[playerIndex]);
                ClearChildren(meldContentPanels[playerIndex]);

                int visibleDiscardCount = Mathf.Min(players[playerIndex].discards.Count, 18);
                for (int discardIndex = 0; discardIndex < visibleDiscardCount; discardIndex++)
                {
                    CreateDiscardTile(
                        discardContentPanels[playerIndex],
                        players[playerIndex].discards[discardIndex],
                        playerIndex,
                        discardIndex);
                }

                foreach (MeldState meld in players[playerIndex].melds)
                {
                    GameObject meldGroup = new GameObject("MeldGroup");
                    meldGroup.transform.SetParent(meldContentPanels[playerIndex], false);
                    Image meldBackground = meldGroup.AddComponent<Image>();
                    meldBackground.color = new Color(0.1f, 0.3f, 0.27f, 0.9f);
                    meldBackground.raycastTarget = false;

                    HorizontalLayoutGroup meldLayout = meldGroup.AddComponent<HorizontalLayoutGroup>();
                    meldLayout.padding = new RectOffset(3, 3, 3, 3);
                    meldLayout.spacing = 2f;
                    meldLayout.childAlignment = TextAnchor.MiddleCenter;
                    meldLayout.childControlWidth = false;
                    meldLayout.childControlHeight = false;

                    LayoutElement groupLayout = meldGroup.AddComponent<LayoutElement>();
                    groupLayout.preferredWidth = Mathf.Max(52f, meld.Tiles.Count * 40f + 10f);
                    groupLayout.preferredHeight = 44f;

                    for (int tileIndex = 0; tileIndex < meld.Tiles.Count; tileIndex++)
                    {
                        bool concealedBack = meld.Type == UpgradeMahjong.Mahjong.MahjongMeldType.ClosedKan
                            && (tileIndex == 0 || tileIndex == meld.Tiles.Count - 1);
                        if (concealedBack)
                            CreateConcealedKanBack(meldGroup.transform, new Vector2(36f, 34f));
                        else
                            CreateBoardTile(meldGroup.transform, meld.Tiles[tileIndex], new Vector2(36f, 34f));
                    }
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(meldContentPanels[playerIndex]);
            }

            RefreshDiscardHighlights();
        }

        private Mahjong3DBoardSnapshot CreateThreeDimensionalSnapshot()
        {
            Mahjong3DBoardSnapshot snapshot = new Mahjong3DBoardSnapshot
            {
                Hands = players
                    .Select(player => player.hand.Select(GetTileIndex).ToArray())
                    .ToArray(),
                Discards = players
                    .Select(player => player.discards.Select(GetTileIndex).ToArray())
                    .ToArray(),
                Melds = players
                    .Select(player => player.melds.Select(meld => new Mahjong3DMeldVisual
                    {
                        TileIndices = meld.Tiles.Select(GetTileIndex).ToArray(),
                        ConcealedKan = meld.Type == UpgradeMahjong.Mahjong.MahjongMeldType.ClosedKan,
                        AddedKan = meld.Type == UpgradeMahjong.Mahjong.MahjongMeldType.AddedKan,
                        CalledFromSeat = meld.CalledFromSeat,
                        CalledTileIndex = meld.CalledTileIndex,
                        AddedTileIndex = meld.AddedTileIndex
                    }).ToArray())
                    .ToArray(),
                RiichiDeclared = players.Select(player => player.riichiDeclared).ToArray(),
                RiichiDiscardIndices = players.Select(player => player.riichiDiscardIndex).ToArray(),
                Scores = players.Select(player => player.score).ToArray(),
                // 도라 표시패는 3D 테이블 뷰가 카메라 왼쪽 위에 실제 패 모델로 그린다.
                DoraIndicators = doraIndicators.Select(GetTileIndex).ToArray(),
                CurrentPlayer = currentPlayer,
                FreshDrawIndices = players
                    .Select(player => player.hand.FindIndex(tile => tile.id == player.freshDrawId))
                    .ToArray(),
                HumanFreshDrawIndex = players[0].hand.FindIndex(tile => tile.id == players[0].freshDrawId),
                HumanSelectedIndex = selectedTileIndex,
                HumanRiichiDiscardChoices = players[0].hand
                    .Select(tile => declaringRiichi && riichiDiscardTileIds.Contains(tile.id))
                    .ToArray(),
                HumanMayDiscard = currentPlayer == 0 && humanMayDiscard
                    && !waitingForResponse && !waitingForRiichiDrawAction && !choosingMeld,
                PendingDiscarder = pendingDiscarder,
                PendingDiscardIndex = pendingDiscarder >= 0 && pendingDiscarder < players.Count && pendingDiscard != null
                    ? players[pendingDiscarder].discards.IndexOf(pendingDiscard)
                    : -1,
                WinningSeat = revealedWinnerIndex,
                WinningHandIndex = revealedWinningTileHandIndex,
                RevealAllHands = revealAllHands
            };
            return snapshot;
        }

        private static void ClearChildren(RectTransform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private void CreateBoardTile(Transform parent, Tile tile, Vector2 size)
        {
            GameObject tileObject = new GameObject("Tile_" + tile.Label);
            tileObject.transform.SetParent(parent, false);
            Image background = tileObject.AddComponent<Image>();
            background.color = Color.white;
            background.raycastTarget = false;
            ApplyRoundedImage(background);

            GameObject faceObject = new GameObject("TileFace");
            RectTransform faceRect = faceObject.AddComponent<RectTransform>();
            faceRect.SetParent(tileObject.transform, false);
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = new Vector2(2f, 2f);
            faceRect.offsetMax = new Vector2(-2f, -2f);
            Image face = faceObject.AddComponent<Image>();
            face.sprite = GetTileSprite(tile);
            ApplyTileArtworkStyle(face, tile);
            face.preserveAspect = true;
            face.raycastTarget = false;

            LayoutElement layout = tileObject.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            if (face.sprite == null)
            {
                Text label = CreateText(tileObject.transform, tile.Label, 16, TextAnchor.MiddleCenter, Color.black);
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }
        }

        private static void CreateConcealedKanBack(Transform parent, Vector2 size)
        {
            GameObject tileObject = new GameObject("ConcealedKanBack");
            tileObject.transform.SetParent(parent, false);
            Image image = tileObject.AddComponent<Image>();
            image.color = new Color(0.06f, 0.20f, 0.17f, 1f);
            image.raycastTarget = false;

            Outline outline = tileObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.68f, 0.55f, 0.30f, 0.9f);
            outline.effectDistance = new Vector2(1f, 1f);

            LayoutElement layout = tileObject.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
        }

        private void CreateDiscardTile(RectTransform parent, Tile tile, int playerIndex, int discardIndex)
        {
            int column = discardIndex % 6;
            int row = discardIndex / 6;
            float[] columnAnchors = sceneLayout == null ? HorizontalDiscardColumns : sceneLayout.DiscardColumnAnchors;
            float[] rowAnchors = sceneLayout == null ? HorizontalDiscardRows : sceneLayout.DiscardRowAnchors;
            float anchorX = columnAnchors != null && column < columnAnchors.Length
                ? columnAnchors[column]
                : HorizontalDiscardColumns[column];
            float anchorY = rowAnchors != null && row < rowAnchors.Length
                ? rowAnchors[row]
                : HorizontalDiscardRows[row];
            Vector2 slotAnchor = new Vector2(anchorX, anchorY);

            GameObject tileObject = new GameObject("DiscardTile_" + tile.Label);
            RectTransform tileRect = tileObject.AddComponent<RectTransform>();
            tileRect.SetParent(parent, false);
            tileRect.anchorMin = slotAnchor;
            tileRect.anchorMax = slotAnchor;
            tileRect.pivot = new Vector2(0.5f, 0.5f);
            tileRect.anchoredPosition = Vector2.zero;
            tileRect.sizeDelta = GetDiscardTileSize(GetDiscardBoardLayout(playerIndex));
            bool isRiichiDiscard = playerIndex >= 0
                && playerIndex < players.Count
                && discardIndex == players[playerIndex].riichiDiscardIndex;
            tileRect.localRotation = isRiichiDiscard
                ? Quaternion.Euler(0f, 0f, 90f)
                : Quaternion.identity;

            Image background = tileObject.AddComponent<Image>();
            background.color = Color.white;
            background.raycastTarget = false;
            ApplyRoundedImage(background);

            GameObject faceObject = new GameObject("TileFace");
            RectTransform faceRect = faceObject.AddComponent<RectTransform>();
            faceRect.SetParent(tileObject.transform, false);
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = new Vector2(2f, 2f);
            faceRect.offsetMax = new Vector2(-2f, -2f);
            Image image = faceObject.AddComponent<Image>();
            image.sprite = GetTileSprite(tile);
            ApplyTileArtworkStyle(image, tile);
            image.preserveAspect = true;
            image.raycastTarget = false;

            Image matchOverlay = CreateTileMatchOverlay(tileObject.transform, "DiscardMatchOverlay");

            Outline glow = tileObject.AddComponent<Outline>();
            glow.effectDistance = new Vector2(3f, 3f);
            glow.enabled = false;

            discardTileVisuals.Add(new DiscardTileVisual
            {
                TileIndex = GetTileIndex(tile),
                Image = image,
                MatchOverlay = matchOverlay,
                Glow = glow,
                IsPendingDiscard = ReferenceEquals(tile, pendingDiscard)
            });

            if (image.sprite == null)
            {
                Text label = CreateText(tileObject.transform, tile.Label, 16, TextAnchor.MiddleCenter, Color.black);
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }
        }

        private void RefreshDiscardHighlights()
        {
            if (threeDimensionalView != null)
            {
                threeDimensionalView.SetHighlightedTileIndex(hoveredDiscardTileIndex);
                threeDimensionalView.SetPendingCallHighlight(callHighlightActive);
            }

            ApplyMatchingTileHighlights(discardTileVisuals, true);
            ApplyMatchingTileHighlights(doraTileVisuals, false);
        }

        private void ApplyMatchingTileHighlights(List<DiscardTileVisual> visuals, bool showPendingDiscard)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                DiscardTileVisual visual = visuals[i];
                if (visual.Image == null) continue;

                bool hoveredMatch = hoveredDiscardTileIndex >= 0 && visual.TileIndex == hoveredDiscardTileIndex;
                bool pending = showPendingDiscard && visual.IsPendingDiscard;
                bool callTarget = pending && callHighlightActive;
                visual.Image.color = callTarget
                    ? new Color(1f, 0.78f, 0.18f, 1f)
                    : Color.white;
                if (visual.MatchOverlay != null)
                    visual.MatchOverlay.gameObject.SetActive(hoveredMatch);
                if (visual.Glow != null)
                {
                    visual.Glow.enabled = pending && !hoveredMatch;
                    visual.Glow.effectColor = callTarget
                        ? new Color(1f, 0.78f, 0.08f, 1f)
                        : new Color(1f, 0.36f, 0.12f, 0.96f);
                    visual.Glow.effectDistance = callTarget
                        ? new Vector2(5f, 5f)
                        : new Vector2(2f, 2f);
                }
            }
        }

        private Image CreateTileMatchOverlay(Transform parent, string objectName)
        {
            GameObject overlayObject = new GameObject(objectName);
            RectTransform overlayRect = overlayObject.AddComponent<RectTransform>();
            overlayRect.SetParent(parent, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            Image overlay = overlayObject.AddComponent<Image>();
            overlay.color = new Color(0.04f, 0.92f, 0.20f, 0.82f);
            overlay.raycastTarget = false;
            ApplyRoundedImage(overlay);
            overlayObject.SetActive(false);
            return overlay;
        }

        private void ApplyRoundedImage(Image image)
        {
            if (image == null) return;
            image.sprite = GetRoundedUiSprite();
            image.type = Image.Type.Sliced;
        }

        private Sprite GetRoundedUiSprite()
        {
            if (roundedUiSprite != null) return roundedUiSprite;

            const int textureSize = 64;
            const float radius = 15f;
            roundedUiTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "Runtime Rounded UI",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[textureSize * textureSize];
            Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
            Vector2 inner = new Vector2(center.x - radius, center.y - radius);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 distance = new Vector2(
                        Mathf.Max(Mathf.Abs(x - center.x) - inner.x, 0f),
                        Mathf.Max(Mathf.Abs(y - center.y) - inner.y, 0f));
                    float alpha = 1f - Mathf.SmoothStep(radius - 1.25f, radius + 0.25f, distance.magnitude);
                    pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            roundedUiTexture.SetPixels(pixels);
            roundedUiTexture.Apply(false, false);
            roundedUiSprite = Sprite.Create(
                roundedUiTexture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(18f, 18f, 18f, 18f));
            roundedUiSprite.name = "Runtime Rounded UI Sprite";
            return roundedUiSprite;
        }

        private BoardLayoutSnapshot GetDiscardBoardLayout(int playerIndex)
        {
            if (sceneLayout != null)
            {
                RectTransform authoredRoot = sceneLayout.GetDiscardRoot(playerIndex);
                if (authoredRoot != null)
                {
                    float angle = authoredRoot.localEulerAngles.z;
                    if (angle > 180f) angle -= 360f;
                    return new BoardLayoutSnapshot(
                        authoredRoot.anchoredPosition,
                        authoredRoot.sizeDelta,
                        angle);
                }
            }

            DiscardBoardLayout fallback;
            switch (playerIndex)
            {
                case 1: fallback = rightDiscardBoardLayout; break;
                case 2: fallback = topDiscardBoardLayout; break;
                case 3: fallback = leftDiscardBoardLayout; break;
                default: fallback = bottomDiscardBoardLayout; break;
            }

            return fallback == null
                ? new BoardLayoutSnapshot(Vector2.zero, new Vector2(780f, 260f), 0f)
                : new BoardLayoutSnapshot(fallback.position, fallback.size, fallback.rotation);
        }

        private static Vector2 GetSafeBoardSize(BoardLayoutSnapshot layout)
        {
            return new Vector2(Mathf.Max(1f, layout.Size.x), Mathf.Max(1f, layout.Size.y));
        }

        private static Vector2 GetRotatedBounds(Vector2 size, float rotation)
        {
            float radians = rotation * Mathf.Deg2Rad;
            float absoluteCosine = Mathf.Abs(Mathf.Cos(radians));
            float absoluteSine = Mathf.Abs(Mathf.Sin(radians));
            return new Vector2(
                size.x * absoluteCosine + size.y * absoluteSine,
                size.x * absoluteSine + size.y * absoluteCosine);
        }

        private Vector2 GetDiscardTileSize(BoardLayoutSnapshot layout)
        {
            if (sceneLayout != null)
            {
                Vector2 authoredSize = sceneLayout.DiscardTileReferenceSize;
                return new Vector2(Mathf.Max(1f, authoredSize.x), Mathf.Max(1f, authoredSize.y));
            }

            Vector2 boardSize = GetSafeBoardSize(layout);
            return new Vector2(46f * boardSize.x / 430f, 64f * boardSize.y / 175f);
        }

        private void ApplyDiscardBoardLayout(int playerIndex)
        {
            RectTransform area = discardBoardAreaRects[playerIndex];
            RectTransform artwork = discardBoardVisualRects[playerIndex];
            if (area == null || artwork == null) return;

            BoardLayoutSnapshot layout = GetDiscardBoardLayout(playerIndex);

            Vector2 boardSize = GetSafeBoardSize(layout);
            bool usesAuthoredBoard = sceneLayout != null && sceneLayout.GetDiscardRoot(playerIndex) == area;
            area.sizeDelta = usesAuthoredBoard ? boardSize : GetRotatedBounds(boardSize, layout.Rotation);
            area.anchoredPosition = layout.Position;
            area.localRotation = usesAuthoredBoard
                ? Quaternion.Euler(0f, 0f, layout.Rotation)
                : Quaternion.identity;
            artwork.sizeDelta = boardSize;
            artwork.localRotation = usesAuthoredBoard
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, layout.Rotation);

            RectTransform discardContent = discardContentPanels[playerIndex];
            if (discardContent == null) return;
            Vector2 tileSize = GetDiscardTileSize(layout);
            for (int childIndex = 0; childIndex < discardContent.childCount; childIndex++)
            {
                if (discardContent.GetChild(childIndex) is RectTransform tileRect)
                    tileRect.sizeDelta = tileSize;
            }
        }

        private void UpdateView(string message)
        {
            currentStatusMessage = message;
            if (!choosingMeld && threeDimensionalView != null)
                threeDimensionalView.HideMeldChoicePreviews();
            RefreshHandButtonsIfNeeded();
            RefreshStatusText();
            if (players.Count > 0)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerState player = players[i];
                    playerInfoTexts[i].gameObject.SetActive(threeDimensionalView == null || !threeDimensionalView.IsReady);
                    string active = i == currentPlayer ? " ◀" : string.Empty;
                    playerInfoTexts[i].text = player.name + active + "\n" + player.score.ToString("N0") + "점  |  턴 " + player.turnCount;
                    playerInfoTexts[i].color = i == currentPlayer
                        ? new Color(1f, 0.84f, 0.34f)
                        : Color.white;
                }
            }

            RefreshRiichiAnnouncement();
            RefreshBoardHistory();
            RefreshDoraIndicatorPanel();
            RefreshWaitingTilesPanel();
            RebuildActionButtons();
        }

        private void RefreshRiichiAnnouncement()
        {
            for (int playerIndex = 0; playerIndex < announcedRiichiStates.Length; playerIndex++)
            {
                bool declared = playerIndex < players.Count && players[playerIndex].riichiDeclared;
                if (declared && !announcedRiichiStates[playerIndex])
                {
                    if (riichiAnnouncementRoutine != null)
                        StopCoroutine(riichiAnnouncementRoutine);
                    riichiAnnouncementRoutine = StartCoroutine(
                        ShowRiichiAnnouncementRoutine(players[playerIndex].name));
                }
                announcedRiichiStates[playerIndex] = declared;
            }
        }

        private IEnumerator ShowRiichiAnnouncementRoutine(string playerName)
        {
            if (riichiAnnouncementPanel == null || riichiAnnouncementText == null)
                yield break;

            riichiAnnouncementText.text = playerName + "  리치!";
            riichiAnnouncementPanel.SetActive(true);
            RectTransform panelRect = riichiAnnouncementPanel.GetComponent<RectTransform>();
            Outline border = riichiAnnouncementPanel.GetComponent<Outline>();
            Image background = riichiAnnouncementPanel.GetComponent<Image>();
            float elapsed = 0f;
            const float flashDuration = 0.5f;
            while (elapsed < flashDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / flashDuration);
                float pulse = 1f + Mathf.Sin(progress * Mathf.PI * 4f) * 0.08f;
                panelRect.localScale = Vector3.one * pulse;
                if (border != null)
                    border.effectColor = Color.Lerp(
                        new Color(1f, 0.45f, 0.04f, 1f),
                        new Color(1f, 0.93f, 0.42f, 1f),
                        0.5f + 0.5f * Mathf.Sin(progress * Mathf.PI * 6f));
                if (background != null)
                    background.color = Color.Lerp(
                        new Color(0.30f, 0.025f, 0.015f, 0.98f),
                        new Color(0.48f, 0.22f, 0.02f, 0.98f),
                        0.5f + 0.5f * Mathf.Sin(progress * Mathf.PI * 4f));
                yield return null;
            }

            panelRect.localScale = Vector3.one;
            if (border != null)
                border.effectColor = new Color(0.88f, 0.05f, 0.03f, 1f);
            if (background != null)
                background.color = new Color(0.18f, 0.015f, 0.012f, 0.98f);
            yield return new WaitForSecondsRealtime(1.1f);
            riichiAnnouncementPanel.SetActive(false);
            riichiAnnouncementRoutine = null;
        }

        private void RefreshWaitingTilesPanel()
        {
            if (waitingTilesPanel == null || waitingTilesContent == null || players.Count == 0)
                return;

            PlayerState human = players[0];
            List<int> waits = GetWinningTileIndices(
                BuildTileCounts(human.hand),
                BuildOpenMelds(human));
            bool show = waits.Count > 0 && !IsHumanFuriten(waits);
            waitingTilesPanel.SetActive(show);
            ClearChildren(waitingTilesContent);
            if (!show) return;
            waitingTilesContent.SetAsLastSibling();

            Vector2 currentTileSize = sceneLayout == null
                ? new Vector2(94f, 138f)
                : sceneLayout.HandTileVisualSize;
            Vector2 waitingTileSize = currentTileSize * 0.5f;
            for (int index = 0; index < waits.Count; index++)
                CreateScreenTile(waitingTilesContent, waits[index], waitingTileSize, "WaitingTile_");

            const float spacing = 5f;
            RectTransform panelRect = waitingTilesPanel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(
                waits.Count * waitingTileSize.x + Mathf.Max(0, waits.Count - 1) * spacing,
                waitingTileSize.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(waitingTilesContent);
        }

        private void CreateScreenTile(
            Transform parent,
            int tileIndex,
            Vector2 size,
            string objectNamePrefix)
        {
            Tile tile = CreateDisplayTile(tileIndex);
            GameObject tileObject = new GameObject(objectNamePrefix + tile.Label);
            tileObject.transform.SetParent(parent, false);

            Image background = tileObject.AddComponent<Image>();
            background.color = new Color(0.97f, 0.94f, 0.82f, 1f);
            background.sprite = null;
            background.type = Image.Type.Simple;
            background.raycastTarget = false;

            Outline border = tileObject.AddComponent<Outline>();
            border.effectColor = new Color(0.08f, 0.28f, 0.22f, 0.92f);
            border.effectDistance = new Vector2(2f, -2f);

            GameObject faceObject = new GameObject("TileFace");
            RectTransform faceRect = faceObject.AddComponent<RectTransform>();
            faceRect.SetParent(tileObject.transform, false);
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = new Vector2(2f, 2f);
            faceRect.offsetMax = new Vector2(-2f, -2f);
            Image face = faceObject.AddComponent<Image>();
            face.sprite = GetTileSprite(tile);
            ApplyTileArtworkStyle(face, tile);
            face.preserveAspect = true;
            face.raycastTarget = false;

            LayoutElement layout = tileObject.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            if (face.sprite == null)
            {
                Text label = CreateText(tileObject.transform, tile.Label, 16, TextAnchor.MiddleCenter, Color.black);
                StretchToParent(label.rectTransform);
            }
        }

        private void RefreshStatusText()
        {
            if (statusText == null) return;

            string phase = string.Empty;
            if (waitingForResponse)
                phase = "내 행동 선택 대기";
            else if (players.Count > 0 && currentPlayer >= 0 && currentPlayer < players.Count)
                phase = players[currentPlayer].name + " 차례";

            string handState = players.Count > 0 ? "  |  내 상태: " + GetHandStateSummary(players[0]) : string.Empty;
            string phasePrefix = string.IsNullOrEmpty(phase) ? string.Empty : "【" + phase + "】 ";
            statusText.text = phasePrefix + currentStatusMessage + handState;
        }

        private static Tile CreateDisplayTile(int tileIndex)
        {
            tileIndex = Mathf.Clamp(tileIndex, 0, 33);
            if (tileIndex < 9)
                return new Tile { suit = Suit.Man, rank = tileIndex + 1, id = -1 };
            if (tileIndex < 18)
                return new Tile { suit = Suit.Pin, rank = tileIndex - 8, id = -1 };
            if (tileIndex < 27)
                return new Tile { suit = Suit.Sou, rank = tileIndex - 17, id = -1 };
            return new Tile { suit = Suit.Honor, rank = tileIndex - 26, id = -1 };
        }

        private static string GetTileLabel(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= 34) return string.Empty;
            if (tileIndex < 9) return (tileIndex + 1) + "만";
            if (tileIndex < 18) return (tileIndex - 8) + "통";
            if (tileIndex < 27) return (tileIndex - 17) + "삭";
            string[] honors = { "동", "남", "서", "북", "백", "발", "중" };
            return honors[tileIndex - 27];
        }

        private static void SortHand(List<Tile> hand)
        {
            hand.Sort((a, b) =>
            {
                int suitCompare = a.suit.CompareTo(b.suit);
                return suitCompare != 0 ? suitCompare : a.rank.CompareTo(b.rank);
            });
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

    }

    public sealed class TileHoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform tileRect;
        private Vector2 restingPosition;
        private float liftAmount = 20f;
        private float currentLift;
        private bool hovered;
        private Action pointerEntered;
        private Action pointerExited;

        public void Configure(RectTransform visualTarget, float amount, Action onPointerEntered = null, Action onPointerExited = null)
        {
            liftAmount = Mathf.Max(0f, amount);
            tileRect = visualTarget;
            pointerEntered = onPointerEntered;
            pointerExited = onPointerExited;
            if (tileRect != null) restingPosition = tileRect.anchoredPosition;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tileRect == null) return;
            restingPosition = tileRect.anchoredPosition - Vector2.up * currentLift;
            hovered = true;
            pointerEntered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pointerExited?.Invoke();
        }

        private void LateUpdate()
        {
            if (tileRect == null) return;
            if (!hovered && currentLift <= 0.01f)
                restingPosition = tileRect.anchoredPosition;

            float targetLift = hovered ? liftAmount : 0f;
            currentLift = Mathf.MoveTowards(currentLift, targetLift, 180f * Time.unscaledDeltaTime);
            tileRect.anchoredPosition = restingPosition + Vector2.up * currentLift;
        }

        private void OnDisable()
        {
            if (tileRect != null) tileRect.anchoredPosition = restingPosition;
            currentLift = 0f;
            hovered = false;
        }
    }
}
