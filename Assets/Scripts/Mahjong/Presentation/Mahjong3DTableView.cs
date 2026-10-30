using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace UpgradeMahjong
{
    [Serializable]
    public sealed class Mahjong3DMeldVisual
    {
        public int[] TileIndices = Array.Empty<int>();
        public bool ConcealedKan;
        public bool AddedKan;
        public int CalledFromSeat = -1;
        public int CalledTileIndex = -1;
        public int AddedTileIndex = -1;
    }

    [Serializable]
    public sealed class Mahjong3DBoardSnapshot
    {
        public int[][] Hands = Array.Empty<int[]>();
        public int[][] Discards = Array.Empty<int[]>();
        public Mahjong3DMeldVisual[][] Melds = Array.Empty<Mahjong3DMeldVisual[]>();
        public bool[] RiichiDeclared = Array.Empty<bool>();
        public int[] RiichiDiscardIndices = Array.Empty<int>();
        public int[] Scores = Array.Empty<int>();
        public int[] DoraIndicators = Array.Empty<int>();
        public int CurrentPlayer = -1;
        public int[] FreshDrawIndices = Array.Empty<int>();
        public int HumanFreshDrawIndex = -1;
        public int HumanSelectedIndex = -1;
        public bool[] HumanRiichiDiscardChoices = Array.Empty<bool>();
        public bool HumanMayDiscard;
        public int PendingDiscarder = -1;
        public int PendingDiscardIndex = -1;
        public int WinningSeat = -1;
        public int WinningHandIndex = -1;
        public bool RevealAllHands;
    }

    /// <summary>
    /// 3ds Max에서 가져온 작탁/패/리치봉 모델을 기존 마작 게임 상태와 연결하는 표현 전용 컴포넌트다.
    /// 게임 규칙은 UpgradeMahjongPrototype이 계속 소유하며 이 컴포넌트는 입력과 시각화만 담당한다.
    /// </summary>
    public sealed class Mahjong3DTableView : MonoBehaviour
    {
        [Header("3D 모델")]
        [InspectorName("작탁 모델")]
        [SerializeField] private GameObject tableModelPrefab;
        [InspectorName("마작패 모델")]
        [SerializeField] private GameObject tileModelPrefab;
        [InspectorName("리치봉 모델")]
        [SerializeField] private GameObject riichiStickModelPrefab;
        [InspectorName("기존 2D 손패·버림패 숨기기")]
        [SerializeField] private bool hideLegacyTileUi = true;

        [Header("작탁 배치")]
        [InspectorName("작탁 위치")]
        [SerializeField] private Vector3 tablePosition = Vector3.zero;
        [InspectorName("작탁 회전")]
        [Tooltip("3ds Max 작탁 모델의 기본 축 보정값은 X 270도입니다.")]
        [SerializeField] private Vector3 tableRotation = new Vector3(270f, 0f, 0f);
        [InspectorName("작탁 크기")]
        [SerializeField] private Vector3 tableScale = Vector3.one;
        [InspectorName("작탁 윗면 높이")]
        [Tooltip("작탁 모델 원점을 기준으로 패가 놓일 Y 높이입니다.")]
        [SerializeField] private float tableSurfaceHeight = 0.43f;

        [Header("패 크기와 간격")]
        [InspectorName("패 크기")]
        [SerializeField] private Vector3 tileScale = Vector3.one;
        [InspectorName("손패 간격")]
        [SerializeField] private float handSpacing = 0.43f;
        [InspectorName("새로 받은 패 간격")]
        [SerializeField] private float freshDrawGap = 0.16f;
        [InspectorName("버림패 가로 간격")]
        [SerializeField] private float discardColumnSpacing = 0.42f;
        [InspectorName("버림패 세로 간격")]
        [SerializeField] private float discardRowSpacing = 0.52f;
        [InspectorName("마우스 오버 상승 높이")]
        [SerializeField] private float hoverLift = 0.18f;
        [InspectorName("상승 애니메이션 속도")]
        [SerializeField] private float hoverSpeed = 14f;
        [InspectorName("확대 애니메이션 속도")]
        [SerializeField] private float scaleAnimationSpeed = 16f;
        [InspectorName("손패 정리 이동 속도")]
        [SerializeField] private float handArrangeSpeed = 10f;
        [InspectorName("AI 타패 빈자리 표시 시간")]
        [SerializeField] private float aiDiscardGapDuration = 0.5f;

        [Header("카메라 정면 내 손패")]
        [InspectorName("카메라 정면 배치 사용")]
        [SerializeField] private bool useCameraFacingPrivateHand = true;
        [InspectorName("화면 내 손패 중심 위치")]
        [Tooltip("0~1 화면 좌표입니다. (0.5, 0.12)는 화면 하단 중앙입니다.")]
        [SerializeField] private Vector2 privateHandViewportPosition = new Vector2(0.5f, 0.12f);
        [InspectorName("카메라에서 손패까지 거리")]
        [SerializeField] private float privateHandCameraDistance = 6f;
        [InspectorName("카메라용 손패 크기 배율")]
        [SerializeField, Range(0.8f, 2.2f)] private float privateHandScaleMultiplier = 1f;
        [InspectorName("카메라용 손패 간격")]
        [SerializeField] private float privateHandCameraSpacing = 0.42f;

        [Header("카메라 정면 도라 표시패")]
        [InspectorName("도라 표시패 화면 위치")]
        [Tooltip("0~1 화면 좌표입니다. 왼쪽 위에 실제 3D 패를 배치합니다.")]
        [SerializeField] private Vector2 doraViewportPosition = new Vector2(0.065f, 0.90f);
        [InspectorName("카메라에서 도라 표시패까지 거리")]
        [SerializeField] private float doraCameraDistance = 6f;
        [InspectorName("도라 표시패 크기 배율")]
        [SerializeField, Range(0.8f, 1.6f)] private float doraScaleMultiplier = 1f;
        [InspectorName("도라 표시패 간격")]
        [SerializeField] private float doraCameraSpacing = 0.52f;

        [Header("작탁 위 영역")]
        [FormerlySerializedAs("handDistanceFromCenter")]
        [InspectorName("세워진 손패 중심 거리")]
        [SerializeField] private float standingHandDistanceFromCenter = 3.95f;
        [InspectorName("세워진 손패 바닥 높이")]
        [SerializeField] private float standingHandVerticalOffset = 0.30f;
        [InspectorName("내 개인 손패 중심 거리")]
        [SerializeField] private float privateHandDistanceFromCenter = 4.68f;
        [InspectorName("내 개인 손패 높이")]
        [SerializeField] private float privateHandHeight = 0.45f;
        [InspectorName("버림패 첫 줄 거리")]
        [SerializeField] private float discardDistanceFromCenter = 1.72f;
        [Header("중앙 점수 표시")]
        [InspectorName("점수판 중심 거리")]
        [SerializeField] private float scoreDistanceFromCenter = 1.12f;
        [InspectorName("점수판 크기")]
        [SerializeField] private Vector2 scorePanelSize = new Vector2(1.45f, 0.55f);
        [InspectorName("점수 글자 크기")]
        [SerializeField] private float scoreCharacterSize = 0.04f;
        [InspectorName("울림패 중심 위치")]
        [Tooltip("각 좌석 기준 오른쪽 아래 첫 울림패의 위치입니다. 추가 울림은 작탁 안쪽으로 쌓입니다.")]
        [SerializeField] private Vector2 meldLocalPosition = new Vector2(4.45f, -4.45f);
        [InspectorName("울림패 묶음 추가 여백")]
        [Tooltip("0이면 패 깊이에 딱 붙입니다. 값이 크면 다음 치·퐁·깡 묶음이 작탁 안쪽으로 더 떨어집니다.")]
        [Min(0f)]
        [SerializeField] private float meldStackSpacing;

        [Header("재질 색상")]
        [InspectorName("작탁 색상")]
        [SerializeField] private Color tableColor = new Color(0.012f, 0.014f, 0.018f, 1f);
        [InspectorName("패 몸체 색상")]
        [SerializeField] private Color tileBodyColor = new Color(0.96f, 0.91f, 0.76f, 1f);
        [InspectorName("패 바닥 색상")]
        [SerializeField] private Color tileBaseColor = new Color(0.05f, 0.34f, 0.25f, 1f);
        [InspectorName("패 뒷면 색상")]
        [SerializeField] private Color tileBackColor = new Color(0.04f, 0.24f, 0.19f, 1f);
        [InspectorName("리치봉 색상")]
        [SerializeField] private Color riichiStickColor = new Color(0.96f, 0.91f, 0.78f, 1f);
        [InspectorName("둥근 패 몸체 사용")]
        [SerializeField] private bool useRoundedTileBodies = true;
        [InspectorName("패 모서리 둥글기")]
        [SerializeField, Range(0.02f, 0.09f)] private float tileCornerRadius = 0.05f;

        [Header("회색 룸 배경")]
        [InspectorName("룸 색상")]
        [SerializeField] private Color roomColor = new Color(0.34f, 0.36f, 0.39f, 1f);
        [InspectorName("룸 가로 크기")]
        [SerializeField] private float roomWidth = 20f;
        [InspectorName("룸 깊이")]
        [SerializeField] private float roomDepth = 16f;
        [InspectorName("룸 벽 높이")]
        [SerializeField] private float roomHeight = 8f;

        private sealed class TileVisual
        {
            public int TileIndex;
            public int HandIndex = -1;
            public int Seat = -1;
            public Transform Root;
            public Vector3 BaseLocalPosition;
            public Vector3 BaseLocalScale;
            public Quaternion TargetLocalRotation;
            public float RevealStartTime;
            public SpriteRenderer Face;
            public GameObject MatchOverlay;
            public bool IsPrivateHumanHand;
            public bool IsPending;
            public bool IsWinningReveal;
        }

        private sealed class HandTileHit : MonoBehaviour
        {
            public int HandIndex;
            public int TileIndex;
        }

        private readonly List<TileVisual> standingHandVisuals = new List<TileVisual>();
        private readonly List<TileVisual> privateHandVisuals = new List<TileVisual>();
        private readonly List<TileVisual> discardVisuals = new List<TileVisual>();
        private readonly List<TileVisual> meldVisuals = new List<TileVisual>();
        private readonly List<TileVisual> doraVisuals = new List<TileVisual>();
        private readonly Transform[] handRoots = new Transform[4];
        private readonly Transform[] discardRoots = new Transform[4];
        private readonly Transform[] meldRoots = new Transform[4];
        private readonly Transform[] riichiRoots = new Transform[4];
        private readonly Transform[] scoreRoots = new Transform[4];
        private Transform privateHandRoot;
        private Transform doraPreviewRoot;
        private Transform meldChoicePreviewRoot;

        private Sprite[] tileSprites;
        private Material[] manTileRedMaterials;
        private Camera tableCamera;
        private Action<int> handTileClicked;
        private Action<int> tileHoverEntered;
        private Action<int> tileHoverExited;
        private Material tableMaterial;
        private Material tileBodyMaterial;
        private Material tileBaseMaterial;
        private Material tileBackMaterial;
        private Material riichiMaterial;
        private Material riichiMarkMaterial;
        private Material scorePanelMaterial;
        private Material activeScorePanelMaterial;
        private Material roomMaterial;
        private Material tileMatchOverlayMaterial;
        private Material callHighlightOverlayMaterial;
        private readonly string[] standingHandKeys = new string[4];
        private string privateHandKey = string.Empty;
        private string discardsKey = string.Empty;
        private string meldsKey = string.Empty;
        private string riichiKey = string.Empty;
        private string scoreKey = string.Empty;
        private string doraKey = string.Empty;
        private int hoveredHandIndex = -1;
        private int hoveredTileIndex = -1;
        private int highlightedTileIndex = -1;
        private int selectedHumanIndex = -1;
        private bool[] riichiDiscardChoices = Array.Empty<bool>();
        private bool pendingCallHighlight;
        private readonly int[] temporaryGapIndices = { -1, -1, -1, -1 };
        private readonly float[] temporaryGapDeadlines = new float[4];
        private bool humanMayDiscard;
        private bool initialized;

        private const float TileModelWidth = 0.42f;
        private const float TileModelHeight = 0.24f;
        private const float TileModelDepth = 0.62f;

        public bool IsReady => tableModelPrefab != null && tileModelPrefab != null && riichiStickModelPrefab != null;
        public bool HideLegacyTileUi => hideLegacyTileUi;

        public void Initialize(
            Sprite[] sprites,
            Material[] manRedMaterials,
            Camera camera,
            Action<int> onHandTileClicked,
            Action<int> onTileHoverEntered,
            Action<int> onTileHoverExited)
        {
            tileSprites = sprites;
            manTileRedMaterials = manRedMaterials;
            tableCamera = camera;
            handTileClicked = onHandTileClicked;
            tileHoverEntered = onTileHoverEntered;
            tileHoverExited = onTileHoverExited;
            if (!IsReady || initialized) return;

            CreateMaterials();
            CreatePermanentHierarchy();
            initialized = true;
        }

        public void Render(Mahjong3DBoardSnapshot snapshot)
        {
            if (!initialized || snapshot == null) return;

            humanMayDiscard = snapshot.HumanMayDiscard;
            selectedHumanIndex = snapshot.HumanSelectedIndex;
            riichiDiscardChoices = snapshot.HumanRiichiDiscardChoices ?? Array.Empty<bool>();

            int handCount = snapshot.Hands == null ? 0 : Mathf.Min(4, snapshot.Hands.Length);
            for (int seat = 0; seat < 4; seat++)
            {
                int[] seatHand = seat < handCount && snapshot.Hands[seat] != null
                    ? snapshot.Hands[seat]
                    : Array.Empty<int>();
                int freshDrawIndex = snapshot.FreshDrawIndices != null
                    && seat < snapshot.FreshDrawIndices.Length
                        ? snapshot.FreshDrawIndices[seat]
                        : seat == 0 ? snapshot.HumanFreshDrawIndex : -1;
                bool revealWinningHand = snapshot.RevealAllHands || snapshot.WinningSeat == seat;
                int winningHandIndex = revealWinningHand ? snapshot.WinningHandIndex : -1;
                string nextStandingKey = string.Join(",", seatHand)
                    + "|draw:" + freshDrawIndex
                    + "|reveal:" + revealWinningHand
                    + ":" + winningHandIndex;
                if (nextStandingKey == standingHandKeys[seat]) continue;
                RebuildStandingHandSeat(
                    seat,
                    seatHand,
                    freshDrawIndex,
                    revealWinningHand,
                    winningHandIndex);
                standingHandKeys[seat] = nextStandingKey;
            }

            int[] humanHand = handCount > 0 && snapshot.Hands[0] != null
                ? snapshot.Hands[0]
                : Array.Empty<int>();
            string nextPrivateHandKey = string.Join(",", humanHand) + "|draw:" + snapshot.HumanFreshDrawIndex;
            if (nextPrivateHandKey != privateHandKey)
            {
                RebuildPrivateHumanHand(humanHand, snapshot.HumanFreshDrawIndex);
                privateHandKey = nextPrivateHandKey;
            }

            string nextDiscardsKey = BuildNestedKey(snapshot.Discards)
                + "|pending:" + snapshot.PendingDiscarder + ":" + snapshot.PendingDiscardIndex
                + "|riichi:" + (snapshot.RiichiDiscardIndices == null
                    ? string.Empty
                    : string.Join(",", snapshot.RiichiDiscardIndices));
            if (nextDiscardsKey != discardsKey)
            {
                RebuildDiscards(
                    snapshot.Discards,
                    snapshot.PendingDiscarder,
                    snapshot.PendingDiscardIndex,
                    snapshot.RiichiDiscardIndices);
                discardsKey = nextDiscardsKey;
            }

            string nextMeldsKey = BuildMeldKey(snapshot.Melds);
            if (nextMeldsKey != meldsKey)
            {
                RebuildMelds(snapshot.Melds);
                meldsKey = nextMeldsKey;
            }

            string nextRiichiKey = snapshot.RiichiDeclared == null
                ? string.Empty
                : string.Join(",", snapshot.RiichiDeclared.Select(value => value ? "1" : "0"));
            if (nextRiichiKey != riichiKey)
            {
                RebuildRiichiSticks(snapshot.RiichiDeclared);
                riichiKey = nextRiichiKey;
            }

            string nextScoreKey = snapshot.Scores == null
                ? snapshot.CurrentPlayer.ToString()
                : string.Join(",", snapshot.Scores) + "|turn:" + snapshot.CurrentPlayer;
            if (nextScoreKey != scoreKey)
            {
                RebuildScores(snapshot.Scores, snapshot.CurrentPlayer);
                scoreKey = nextScoreKey;
            }

            string nextDoraKey = snapshot.DoraIndicators == null
                ? string.Empty
                : string.Join(",", snapshot.DoraIndicators);
            if (nextDoraKey != doraKey)
            {
                RebuildDoraIndicators(snapshot.DoraIndicators);
                doraKey = nextDoraKey;
            }

            ApplyVisualHighlights();
        }

        public void SetHighlightedTileIndex(int tileIndex)
        {
            highlightedTileIndex = tileIndex;
            ApplyVisualHighlights();
        }

        public void SetPendingCallHighlight(bool value)
        {
            if (pendingCallHighlight == value) return;
            pendingCallHighlight = value;
            ApplyVisualHighlights();
        }

        public void ShowTemporaryDiscardGap(int playerIndex, int discardedHandIndex, float duration = -1f)
        {
            if (playerIndex <= 0 || playerIndex >= temporaryGapIndices.Length) return;
            temporaryGapIndices[playerIndex] = Mathf.Max(0, discardedHandIndex);
            temporaryGapDeadlines[playerIndex] = Time.unscaledTime
                + (duration > 0f ? duration : aiDiscardGapDuration);
        }

        public void ShowMeldChoicePreviews(int[][] optionTiles, int[] calledTilePositions)
        {
            if (!initialized || meldChoicePreviewRoot == null || tableCamera == null) return;
            ClearChildren(meldChoicePreviewRoot);
            optionTiles = optionTiles ?? Array.Empty<int[]>();
            int optionCount = optionTiles.Length;
            if (optionCount == 0) return;

            // Keep each real 3D tile group centered over its corresponding UI choice card.
            // The groups remain visibly separated even when three Chi choices are available.
            float centerStep = 0.18f;
            float firstCenterX = 0.5f - centerStep * (optionCount - 1) * 0.5f;
            float cameraDistance = Mathf.Max(tableCamera.nearClipPlane + 0.1f, privateHandCameraDistance);
            Quaternion worldRotation = Quaternion.LookRotation(tableCamera.transform.up, -tableCamera.transform.forward);

            for (int optionIndex = 0; optionIndex < optionCount; optionIndex++)
            {
                int[] tiles = optionTiles[optionIndex] ?? Array.Empty<int>();
                Vector3 groupCenter = tableCamera.ViewportToWorldPoint(new Vector3(
                    Mathf.Clamp01(firstCenterX + centerStep * optionIndex),
                    0.285f,
                    cameraDistance));
                float totalWidth = Mathf.Max(0, tiles.Length - 1) * 0.48f;
                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    Vector3 worldPosition = groupCenter
                        + tableCamera.transform.right * (tileIndex * 0.48f - totalWidth * 0.5f);
                    Quaternion tileWorldRotation = worldRotation;
                    CreateTileVisual(
                        meldChoicePreviewRoot,
                        tiles[tileIndex],
                        meldChoicePreviewRoot.InverseTransformPoint(worldPosition),
                        Quaternion.Inverse(meldChoicePreviewRoot.rotation) * tileWorldRotation,
                        true,
                        false,
                        -1,
                        false,
                        0,
                        "울림 선택 실제 패",
                        privateHandScaleMultiplier);
                }
            }
        }

        public void HideMeldChoicePreviews()
        {
            if (meldChoicePreviewRoot != null) ClearChildren(meldChoicePreviewRoot);
        }

        private void Update()
        {
            if (!initialized || tableCamera == null || Mouse.current == null) return;

            HandTileHit nextHit = FindHandTileUnderPointer(Mouse.current.position.ReadValue());
            int nextHandIndex = nextHit == null ? -1 : nextHit.HandIndex;
            int nextTileIndex = nextHit == null ? -1 : nextHit.TileIndex;
            // A rebuilt hand can keep the same slot index while the tile in that slot changes.
            // Compare both values so the hover label/highlight never keeps the previous tile type.
            if (nextHandIndex != hoveredHandIndex || nextTileIndex != hoveredTileIndex)
            {
                if (hoveredTileIndex >= 0) tileHoverExited?.Invoke(hoveredTileIndex);
                hoveredHandIndex = nextHandIndex;
                hoveredTileIndex = nextTileIndex;
                if (hoveredTileIndex >= 0) tileHoverEntered?.Invoke(hoveredTileIndex);
                ApplyVisualHighlights();
            }

            UpdateExpiredDiscardGaps();
            AnimateHandTiles();
            if (pendingCallHighlight) ApplyVisualHighlights();

            if (nextHit == null || !humanMayDiscard || !Mouse.current.leftButton.wasPressedThisFrame)
                return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            handTileClicked?.Invoke(nextHit.HandIndex);
        }

        private HandTileHit FindHandTileUnderPointer(Vector2 screenPosition)
        {
            Ray ray = tableCamera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                HandTileHit hit = hits[i].collider.GetComponentInParent<HandTileHit>();
                if (hit != null) return hit;
            }
            return null;
        }

        private void CreatePermanentHierarchy()
        {
            CreateGrayRoom();

            Transform tableRoot = CreateRoot("3D 작탁");
            GameObject table = Instantiate(tableModelPrefab, tableRoot);
            table.name = "작탁 모델";
            table.transform.localPosition = tablePosition;
            table.transform.localEulerAngles = tableRotation;
            table.transform.localScale = tableScale;
            foreach (Renderer renderer in table.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = tableMaterial;
                DisableRendererShadows(renderer);
            }

            Transform dynamicRoot = CreateRoot("3D 게임 오브젝트");
            for (int seat = 0; seat < 4; seat++)
            {
                handRoots[seat] = CreateRoot("세워진 손패_" + seat, dynamicRoot);
                discardRoots[seat] = CreateRoot("버림패_" + seat, dynamicRoot);
                meldRoots[seat] = CreateRoot("울림패_" + seat, dynamicRoot);
                riichiRoots[seat] = CreateRoot("리치봉_" + seat, dynamicRoot);
                scoreRoots[seat] = CreateRoot("점수판_" + seat, dynamicRoot);
            }
            privateHandRoot = CreateRoot("내 개인 손패", dynamicRoot);
            doraPreviewRoot = CreateRoot("도라 실제 패", dynamicRoot);
            meldChoicePreviewRoot = CreateRoot("울림 선택 실제 패", dynamicRoot);
        }

        private void CreateGrayRoom()
        {
            Transform roomRoot = CreateRoot("회색 박스 룸");
            float width = Mathf.Max(14f, roomWidth);
            float depth = Mathf.Max(12f, roomDepth);
            float height = Mathf.Max(5f, roomHeight);

            CreateRoomSurface(roomRoot, "회색 바닥", new Vector3(0f, -0.62f, 0f), new Vector3(width, 0.18f, depth));
            CreateRoomSurface(roomRoot, "회색 뒷벽", new Vector3(0f, height * 0.5f - 0.62f, depth * 0.5f), new Vector3(width, height, 0.18f));
            CreateRoomSurface(roomRoot, "회색 왼쪽 벽", new Vector3(-width * 0.5f, height * 0.5f - 0.62f, 0f), new Vector3(0.18f, height, depth));
            CreateRoomSurface(roomRoot, "회색 오른쪽 벽", new Vector3(width * 0.5f, height * 0.5f - 0.62f, 0f), new Vector3(0.18f, height, depth));
        }

        private void CreateRoomSurface(Transform parent, string objectName, Vector3 position, Vector3 scale)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = objectName;
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = position;
            surface.transform.localScale = scale;
            Renderer renderer = surface.GetComponent<Renderer>();
            renderer.sharedMaterial = roomMaterial;
            DisableRendererShadows(renderer);
            Collider collider = surface.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private Transform CreateRoot(string objectName, Transform parent = null)
        {
            GameObject root = new GameObject(objectName);
            root.transform.SetParent(parent == null ? transform : parent, false);
            return root.transform;
        }

        private void CreateMaterials()
        {
            tableMaterial = CreateLitMaterial("3D 작탁 재질", tableColor, 0.18f, 0.52f);
            tileBodyMaterial = CreateLitMaterial("3D 패 몸체 재질", tileBodyColor, 0.05f, 0.64f);
            tileBaseMaterial = CreateLitMaterial("3D 패 바닥 재질", tileBaseColor, 0.08f, 0.52f);
            tileBackMaterial = CreateLitMaterial("3D 패 뒷면 재질", tileBackColor, 0.08f, 0.48f);
            riichiMaterial = CreateLitMaterial("3D 리치봉 재질", riichiStickColor, 0.05f, 0.62f);
            riichiMarkMaterial = CreateLitMaterial("3D 리치봉 표시 재질", new Color(0.72f, 0.035f, 0.025f), 0.02f, 0.4f);
            scorePanelMaterial = CreateLitMaterial("3D 점수판 재질", new Color(0.025f, 0.075f, 0.07f, 1f), 0.02f, 0.35f);
            activeScorePanelMaterial = CreateLitMaterial("3D 현재 점수판 재질", new Color(0.32f, 0.22f, 0.055f, 1f), 0.02f, 0.42f);
            roomMaterial = CreateLitMaterial("회색 룸 재질", roomColor, 0f, 0.14f);
            tileMatchOverlayMaterial = CreateTransparentUnlitMaterial(
                "동일패 초록 오버레이",
                new Color(0.02f, 0.95f, 0.16f, 0.76f));
            callHighlightOverlayMaterial = CreateTransparentUnlitMaterial(
                "호출 대상 황금 오버레이",
                new Color(1f, 0.68f, 0.04f, 0.82f));
        }

        private static Material CreateTransparentUnlitMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private static void DisableRendererShadows(Renderer renderer)
        {
            if (renderer == null) return;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material CreateLitMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private void RebuildStandingHandSeat(
            int seat,
            int[] tiles,
            int humanFreshDrawIndex,
            bool revealWinningHand,
            int winningHandIndex)
        {
            standingHandVisuals.RemoveAll(visual => visual.Seat == seat);
            ClearChildren(handRoots[seat]);
            tiles = tiles ?? Array.Empty<int>();
            bool reserveDiscardGap = seat > 0
                && temporaryGapIndices[seat] >= 0
                && Time.unscaledTime < temporaryGapDeadlines[seat];
            int reservedGapIndex = reserveDiscardGap
                ? Mathf.Clamp(temporaryGapIndices[seat], 0, tiles.Length)
                : -1;
            int separatedTileIndex = revealWinningHand ? winningHandIndex : humanFreshDrawIndex;
            float[] positions = BuildHandXPositions(tiles.Length, separatedTileIndex, reservedGapIndex);

            for (int index = 0; index < tiles.Length; index++)
            {
                Vector3 standingSeatLocal = new Vector3(
                    positions[index],
                    tableSurfaceHeight + standingHandVerticalOffset,
                    -standingHandDistanceFromCenter);
                TileVisual visual = CreateTileVisual(
                    handRoots[seat],
                    tiles[index],
                    RotateForSeat(standingSeatLocal, seat),
                    GetStandingHandRotation(seat),
                    seat == 0 || revealWinningHand,
                    false,
                    index,
                    false,
                    seat,
                    "세워진 손패");
                if (revealWinningHand)
                {
                    Vector3 revealedSeatLocal = new Vector3(
                        positions[index],
                        tableSurfaceHeight + 0.008f,
                        -standingHandDistanceFromCenter);
                    visual.BaseLocalPosition = RotateForSeat(revealedSeatLocal, seat);
                    visual.TargetLocalRotation = Quaternion.Euler(0f, SeatRotation(seat), 0f);
                    visual.RevealStartTime = Time.unscaledTime + index * 0.035f;
                    visual.IsWinningReveal = true;
                }
                standingHandVisuals.Add(visual);
            }
        }

        private void RebuildPrivateHumanHand(int[] tiles, int humanFreshDrawIndex)
        {
            // The hand may be rebuilt while the pointer is still over the same screen slot.
            // Clear the previous tile identity before replacing colliders and visuals.
            if (hoveredTileIndex >= 0)
                tileHoverExited?.Invoke(hoveredTileIndex);
            hoveredHandIndex = -1;
            hoveredTileIndex = -1;

            privateHandVisuals.Clear();
            ClearChildren(privateHandRoot);
            tiles = tiles ?? Array.Empty<int>();
            float privateSpacing = useCameraFacingPrivateHand
                ? Mathf.Max(0.1f, privateHandCameraSpacing)
                : handSpacing;
            float[] positions = BuildHandXPositions(tiles.Length, humanFreshDrawIndex, -1, privateSpacing);

            Vector3 cameraCenter = Vector3.zero;
            Quaternion cameraFacingRotation = Quaternion.identity;
            if (useCameraFacingPrivateHand && tableCamera != null)
            {
                cameraCenter = tableCamera.ViewportToWorldPoint(new Vector3(
                    Mathf.Clamp01(privateHandViewportPosition.x),
                    Mathf.Clamp01(privateHandViewportPosition.y),
                    Mathf.Max(tableCamera.nearClipPlane + 0.1f, privateHandCameraDistance)));
                cameraFacingRotation = Quaternion.LookRotation(
                    tableCamera.transform.up,
                    -tableCamera.transform.forward);
            }

            for (int index = 0; index < tiles.Length; index++)
            {
                Vector3 localPosition;
                Quaternion localRotation;
                if (useCameraFacingPrivateHand && tableCamera != null)
                {
                    Vector3 worldPosition = cameraCenter + tableCamera.transform.right * positions[index];
                    localPosition = privateHandRoot.InverseTransformPoint(worldPosition);
                    localRotation = Quaternion.Inverse(privateHandRoot.rotation) * cameraFacingRotation;
                }
                else
                {
                    localPosition = new Vector3(
                        positions[index],
                        privateHandHeight,
                        -privateHandDistanceFromCenter);
                    localRotation = Quaternion.identity;
                }
                TileVisual visual = CreateTileVisual(
                    privateHandRoot,
                    tiles[index],
                    localPosition,
                    localRotation,
                    true,
                    false,
                    index,
                    true,
                    0,
                    "내 개인 손패",
                    useCameraFacingPrivateHand ? privateHandScaleMultiplier : 1f);
                privateHandVisuals.Add(visual);
            }
        }

        private float[] BuildHandXPositions(
            int tileCount,
            int freshDrawIndex,
            int reservedGapIndex,
            float spacing = -1f)
        {
            if (spacing <= 0f) spacing = handSpacing;
            float[] positions = new float[tileCount];
            bool useFreshGap = freshDrawIndex > 0 && freshDrawIndex < tileCount;
            bool useDiscardGap = reservedGapIndex >= 0 && reservedGapIndex <= tileCount;
            float totalWidth = Mathf.Max(0, tileCount - 1) * spacing
                + (useFreshGap ? freshDrawGap : 0f)
                + (useDiscardGap ? spacing : 0f);
            float cursor = -totalWidth * 0.5f;

            for (int index = 0; index < tileCount; index++)
            {
                if (useFreshGap && index == freshDrawIndex) cursor += freshDrawGap;
                if (useDiscardGap && index == reservedGapIndex) cursor += spacing;
                positions[index] = cursor;
                cursor += spacing;
            }
            return positions;
        }

        private void RebuildScores(int[] scores, int currentPlayer)
        {
            string[] seatWinds = { "동", "남", "서", "북" };
            for (int seat = 0; seat < 4; seat++) ClearChildren(scoreRoots[seat]);
            if (scores == null) return;

            for (int seat = 0; seat < Mathf.Min(4, scores.Length); seat++)
            {
                Vector3 seatLocal = new Vector3(
                    0f,
                    tableSurfaceHeight + 0.025f,
                    -scoreDistanceFromCenter);
                Vector3 position = RotateForSeat(seatLocal, seat);
                Quaternion rotation = Quaternion.Euler(0f, SeatRotation(seat), 0f);

                GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "점수 배경";
                panel.transform.SetParent(scoreRoots[seat], false);
                panel.transform.localPosition = position;
                panel.transform.localRotation = rotation;
                panel.transform.localScale = new Vector3(scorePanelSize.x, 0.025f, scorePanelSize.y);
                Renderer panelRenderer = panel.GetComponent<Renderer>();
                panelRenderer.sharedMaterial = seat == currentPlayer
                    ? activeScorePanelMaterial
                    : scorePanelMaterial;
                DisableRendererShadows(panelRenderer);
                Collider panelCollider = panel.GetComponent<Collider>();
                if (panelCollider != null) Destroy(panelCollider);

                GameObject labelObject = new GameObject("점수 " + scores[seat].ToString("N0"));
                labelObject.transform.SetParent(scoreRoots[seat], false);
                labelObject.transform.localPosition = position + Vector3.up * 0.045f;
                labelObject.transform.localRotation = Quaternion.Euler(90f, SeatRotation(seat), 0f);
                TextMesh label = labelObject.AddComponent<TextMesh>();
                label.text = scores[seat].ToString("N0");
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 64;
                label.characterSize = scoreCharacterSize;
                label.color = seat == currentPlayer
                    ? new Color(1f, 0.82f, 0.28f, 1f)
                    : Color.white;

                Vector3 windLocal = seatLocal + new Vector3(0f, 0f, scorePanelSize.y * 0.5f + 0.24f);
                GameObject windObject = new GameObject("좌석풍 " + seatWinds[seat]);
                windObject.transform.SetParent(scoreRoots[seat], false);
                windObject.transform.localPosition = RotateForSeat(windLocal, seat) + Vector3.up * 0.047f;
                windObject.transform.localRotation = Quaternion.Euler(90f, SeatRotation(seat), 0f);
                TextMesh windLabel = windObject.AddComponent<TextMesh>();
                windLabel.text = seatWinds[seat];
                windLabel.anchor = TextAnchor.MiddleCenter;
                windLabel.alignment = TextAlignment.Center;
                windLabel.fontSize = 72;
                windLabel.characterSize = scoreCharacterSize * 1.18f;
                windLabel.fontStyle = FontStyle.Bold;
                windLabel.color = seat == currentPlayer
                    ? new Color(1f, 0.82f, 0.28f, 1f)
                    : new Color(0.88f, 0.92f, 0.86f, 1f);
            }
        }

        private void RebuildDoraIndicators(int[] indicators)
        {
            doraVisuals.Clear();
            ClearChildren(doraPreviewRoot);
            indicators = indicators ?? Array.Empty<int>();
            if (tableCamera == null || indicators.Length == 0) return;

            float cameraDistance = Mathf.Max(tableCamera.nearClipPlane + 0.1f, doraCameraDistance);
            Vector3 firstCenter = tableCamera.ViewportToWorldPoint(new Vector3(
                Mathf.Clamp01(doraViewportPosition.x),
                Mathf.Clamp01(doraViewportPosition.y),
                cameraDistance));

            for (int index = 0; index < indicators.Length; index++)
            {
                Vector3 worldPosition = firstCenter + tableCamera.transform.right * (index * doraCameraSpacing);
                Vector3 toCamera = (tableCamera.transform.position - worldPosition).normalized;
                Vector3 screenUp = Vector3.ProjectOnPlane(tableCamera.transform.up, toCamera).normalized;
                Quaternion worldRotation = Quaternion.LookRotation(screenUp, toCamera);
                TileVisual visual = CreateTileVisual(
                    doraPreviewRoot,
                    indicators[index],
                    doraPreviewRoot.InverseTransformPoint(worldPosition),
                    Quaternion.Inverse(doraPreviewRoot.rotation) * worldRotation,
                    true,
                    false,
                    -1,
                    false,
                    -1,
                    "도라 실제 패",
                    doraScaleMultiplier);
                doraVisuals.Add(visual);
            }
        }

        private void RebuildDiscards(
            int[][] discards,
            int pendingDiscarder,
            int pendingDiscardIndex,
            int[] riichiDiscardIndices)
        {
            ClearVisuals(discardVisuals);
            for (int seat = 0; seat < 4; seat++) ClearChildren(discardRoots[seat]);
            if (discards == null) return;

            for (int seat = 0; seat < Mathf.Min(4, discards.Length); seat++)
            {
                int[] tiles = discards[seat] ?? Array.Empty<int>();
                int count = Mathf.Min(18, tiles.Length);
                for (int index = 0; index < count; index++)
                {
                    int column = index % 6;
                    int row = index / 6;
                    Vector3 seatLocal = new Vector3(
                        (column - 2.5f) * discardColumnSpacing,
                        tableSurfaceHeight + 0.008f,
                        -discardDistanceFromCenter - row * discardRowSpacing);
                    bool pending = seat == pendingDiscarder && index == pendingDiscardIndex;
                    bool riichiDiscard = riichiDiscardIndices != null
                        && seat < riichiDiscardIndices.Length
                        && index == riichiDiscardIndices[seat];
                    TileVisual visual = CreateTileVisual(
                        discardRoots[seat], tiles[index], RotateForSeat(seatLocal, seat),
                        Quaternion.Euler(0f, SeatRotation(seat) + (riichiDiscard ? 90f : 0f), 0f),
                        true, pending, -1, false, seat, "버림패");
                    discardVisuals.Add(visual);
                }
            }
        }

        private void RebuildMelds(Mahjong3DMeldVisual[][] melds)
        {
            ClearVisuals(meldVisuals);
            for (int seat = 0; seat < 4; seat++) ClearChildren(meldRoots[seat]);
            if (melds == null) return;

            for (int seat = 0; seat < Mathf.Min(4, melds.Length); seat++)
            {
                Mahjong3DMeldVisual[] seatMelds = melds[seat] ?? Array.Empty<Mahjong3DMeldVisual>();
                float meldRowPitch = TileModelDepth * Mathf.Abs(tileScale.z)
                    + Mathf.Max(0f, meldStackSpacing);
                for (int meldIndex = 0; meldIndex < seatMelds.Length; meldIndex++)
                {
                    Mahjong3DMeldVisual meld = seatMelds[meldIndex];
                    int[] tiles = meld?.TileIndices ?? Array.Empty<int>();
                    List<(int Tile, bool Called)> orderedTiles = BuildMeldDisplayOrder(seat, meld, tiles);
                    float tileGap = Mathf.Max(0.01f,
                        handSpacing - TileModelWidth * Mathf.Abs(tileScale.x));
                    float cursor = 0f;
                    for (int tileIndex = 0; tileIndex < orderedTiles.Count; tileIndex++)
                    {
                        (int tile, bool called) = orderedTiles[tileIndex];
                        float footprint = called
                            ? TileModelDepth * Mathf.Abs(tileScale.z)
                            : TileModelWidth * Mathf.Abs(tileScale.x);
                        cursor -= footprint * 0.5f;
                        bool concealedBack = meld.ConcealedKan
                            && (tileIndex == 0 || tileIndex == orderedTiles.Count - 1);
                        float calledFrontEdgeOffset = called
                            ? -0.5f * (TileModelDepth * Mathf.Abs(tileScale.z)
                                - TileModelWidth * Mathf.Abs(tileScale.x))
                            : 0f;
                        Vector3 seatLocal = new Vector3(
                            meldLocalPosition.x + cursor,
                            tableSurfaceHeight + 0.006f,
                            meldLocalPosition.y + meldIndex * meldRowPitch + calledFrontEdgeOffset);
                        float calledRotation = called ? GetCalledTileRotation(seat, meld) : 0f;
                        TileVisual visual = CreateTileVisual(
                            meldRoots[seat], tile, RotateForSeat(seatLocal, seat),
                            Quaternion.Euler(0f, SeatRotation(seat) + calledRotation, 0f),
                            !concealedBack, false, -1, false, seat, "울림패");
                        meldVisuals.Add(visual);

                        if (called && meld.AddedKan && meld.AddedTileIndex >= 0
                            && meld.AddedTileIndex < tiles.Length)
                        {
                            Vector3 stackedPosition = seatLocal;
                            stackedPosition.y += TileModelHeight * Mathf.Abs(tileScale.y) + 0.012f;
                            TileVisual stackedVisual = CreateTileVisual(
                                meldRoots[seat], tiles[meld.AddedTileIndex], RotateForSeat(stackedPosition, seat),
                                Quaternion.Euler(0f, SeatRotation(seat) + calledRotation, 0f),
                                true, false, -1, false, seat, "가깡 추가패");
                            meldVisuals.Add(stackedVisual);
                        }

                        cursor -= footprint * 0.5f + tileGap;
                    }
                }
            }
        }

        private static List<(int Tile, bool Called)> BuildMeldDisplayOrder(
            int ownerSeat,
            Mahjong3DMeldVisual meld,
            int[] tiles)
        {
            List<(int Tile, bool Called)> result = tiles
                .Select((tile, index) => (Tile: tile, Called: meld != null && index == meld.CalledTileIndex, Index: index))
                .Where(entry => meld == null || !meld.AddedKan || entry.Index != meld.AddedTileIndex)
                .Select(entry => (entry.Tile, entry.Called))
                .ToList();
            if (meld == null || meld.ConcealedKan || meld.CalledFromSeat < 0
                || meld.CalledTileIndex < 0 || meld.CalledTileIndex >= tiles.Length)
                return result;

            int calledResultIndex = result.FindIndex(entry => entry.Called);
            if (calledResultIndex < 0) return result;
            (int Tile, bool Called) calledTile = result[calledResultIndex];
            result.RemoveAt(calledResultIndex);
            int relativeSource = (meld.CalledFromSeat - ownerSeat + 4) % 4;
            int insertIndex = relativeSource == 1
                ? 0
                : relativeSource == 2
                    ? Mathf.Min(1, result.Count)
                    : result.Count;
            result.Insert(insertIndex, calledTile);
            return result;
        }

        private static float GetCalledTileRotation(int ownerSeat, Mahjong3DMeldVisual meld)
        {
            if (meld == null || meld.CalledFromSeat < 0) return 90f;
            int relativeSource = (meld.CalledFromSeat - ownerSeat + 4) % 4;
            return relativeSource == 3 ? -90f : 90f;
        }

        private void RebuildRiichiSticks(bool[] declarations)
        {
            for (int seat = 0; seat < 4; seat++)
                ClearChildren(riichiRoots[seat]);
            if (declarations == null) return;

            for (int seat = 0; seat < Mathf.Min(4, declarations.Length); seat++)
            {
                if (!declarations[seat]) continue;
                GameObject stick = Instantiate(riichiStickModelPrefab, riichiRoots[seat]);
                stick.name = "리치봉";
                stick.transform.localPosition = RotateForSeat(
                    new Vector3(
                        0f,
                        tableSurfaceHeight + 0.09f,
                        -scoreDistanceFromCenter + scorePanelSize.y * 0.62f),
                    seat);
                // The imported stick's long axis is local Z. Rotate it onto each score
                // panel's long axis so it is horizontal from that player's viewpoint.
                stick.transform.localRotation = Quaternion.Euler(
                    0f,
                    SeatRotation(seat) + 90f,
                    0f);
                foreach (Renderer renderer in stick.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = riichiMaterial;
                    DisableRendererShadows(renderer);
                }

                GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mark.name = "리치봉 붉은 표시";
                mark.transform.SetParent(stick.transform, false);
                mark.transform.localPosition = new Vector3(0f, 0.055f, 0f);
                mark.transform.localScale = new Vector3(0.075f, 0.018f, 0.075f);
                mark.GetComponent<Renderer>().sharedMaterial = riichiMarkMaterial;
                DisableRendererShadows(mark.GetComponent<Renderer>());
                Collider markCollider = mark.GetComponent<Collider>();
                if (markCollider != null) Destroy(markCollider);
            }
        }

        private TileVisual CreateTileVisual(
            Transform parent,
            int tileIndex,
            Vector3 localPosition,
            Quaternion localRotation,
            bool faceUp,
            bool pending,
            int handIndex,
            bool interactive,
            int seat,
            string category,
            float scaleMultiplier = 1f)
        {
            GameObject tile = Instantiate(tileModelPrefab, parent);
            tile.name = category + "_" + tileIndex;
            tile.transform.localPosition = localPosition;
            tile.transform.localRotation = localRotation;
            Vector3 baseScale = tileScale * Mathf.Max(0.01f, scaleMultiplier);
            tile.transform.localScale = baseScale;

            foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
            {
                bool lowerBase = renderer.gameObject.name.IndexOf("Box001", StringComparison.OrdinalIgnoreCase) >= 0;
                // 비공개 손패도 몸체는 흰색, 아래 받침은 녹색으로 유지한다.
                renderer.sharedMaterial = lowerBase ? tileBaseMaterial : tileBodyMaterial;
                DisableRendererShadows(renderer);
            }

            if (useRoundedTileBodies)
                ReplaceWithRoundedTileShell(tile.transform);

            SpriteRenderer face = null;
            GameObject matchOverlay = null;
            if (faceUp && tileSprites != null && tileIndex >= 0 && tileIndex < tileSprites.Length && tileSprites[tileIndex] != null)
            {
                GameObject faceObject = new GameObject("패 그림");
                faceObject.transform.SetParent(tile.transform, false);
                faceObject.transform.localPosition = new Vector3(0f, 0.214f, 0f);
                faceObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                face = faceObject.AddComponent<SpriteRenderer>();
                face.sprite = tileSprites[tileIndex];
                face.color = Color.white;
                if (tileIndex < 9
                    && manTileRedMaterials != null
                    && tileIndex < manTileRedMaterials.Length
                    && manTileRedMaterials[tileIndex] != null)
                    face.sharedMaterial = manTileRedMaterials[tileIndex];
                face.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                face.receiveShadows = false;
                Vector2 spriteSize = face.sprite.bounds.size;
                float scaleX = spriteSize.x <= 0f ? 1f : 0.33f / spriteSize.x;
                float scaleY = spriteSize.y <= 0f ? 1f : 0.50f / spriteSize.y;
                faceObject.transform.localScale = new Vector3(scaleX, scaleY, 1f);
                matchOverlay = CreateTileMatchOverlay(tile.transform);
            }

            if (interactive && handIndex >= 0)
            {
                BoxCollider collider = tile.GetComponent<BoxCollider>();
                if (collider == null) collider = tile.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.11f, 0f);
                collider.size = new Vector3(0.42f, 0.24f, 0.62f);
                HandTileHit hit = tile.AddComponent<HandTileHit>();
                hit.HandIndex = handIndex;
                hit.TileIndex = tileIndex;
            }

            return new TileVisual
            {
                TileIndex = tileIndex,
                HandIndex = handIndex,
                Seat = seat,
                Root = tile.transform,
                BaseLocalPosition = localPosition,
                BaseLocalScale = baseScale,
                TargetLocalRotation = localRotation,
                Face = face,
                MatchOverlay = matchOverlay,
                IsPrivateHumanHand = interactive && handIndex >= 0,
                IsPending = pending
            };
        }

        private GameObject CreateTileMatchOverlay(Transform tileRoot)
        {
            GameObject overlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
            overlay.name = "동일패 초록 오버레이";
            overlay.transform.SetParent(tileRoot, false);
            overlay.transform.localPosition = new Vector3(0f, 0.222f, 0f);
            overlay.transform.localScale = new Vector3(0.39f, 0.008f, 0.59f);
            Renderer renderer = overlay.GetComponent<Renderer>();
            renderer.sharedMaterial = tileMatchOverlayMaterial;
            DisableRendererShadows(renderer);
            Collider collider = overlay.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            overlay.SetActive(false);
            return overlay;
        }

        private void ReplaceWithRoundedTileShell(Transform tileRoot)
        {
            foreach (Renderer renderer in tileRoot.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;

            float radius = Mathf.Clamp(tileCornerRadius, 0.02f, 0.09f);
            CreateRoundedPrism(
                tileRoot,
                "둥근 패 받침",
                new Vector3(0.42f, 0.07f, 0.62f),
                new Vector3(0f, 0.035f, 0f),
                radius,
                tileBaseMaterial);
            CreateRoundedPrism(
                tileRoot,
                "둥근 패 몸체",
                new Vector3(0.40f, 0.17f, 0.60f),
                new Vector3(0f, 0.125f, 0f),
                Mathf.Min(radius, 0.048f),
                tileBodyMaterial);
        }

        private static void CreateRoundedPrism(
            Transform parent,
            string objectName,
            Vector3 size,
            Vector3 center,
            float radius,
            Material material)
        {
            radius = Mathf.Min(radius, Mathf.Min(size.x, size.z) * 0.48f);
            CreateRoundedPrismPart(
                parent,
                objectName + "_가로",
                PrimitiveType.Cube,
                center,
                new Vector3(size.x - radius * 2f, size.y, size.z),
                material);
            CreateRoundedPrismPart(
                parent,
                objectName + "_세로",
                PrimitiveType.Cube,
                center,
                new Vector3(size.x, size.y, size.z - radius * 2f),
                material);

            float cornerX = size.x * 0.5f - radius;
            float cornerZ = size.z * 0.5f - radius;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    CreateRoundedPrismPart(
                        parent,
                        objectName + "_모서리",
                        PrimitiveType.Cylinder,
                        center + new Vector3(cornerX * x, 0f, cornerZ * z),
                        new Vector3(radius * 2f, size.y * 0.5f, radius * 2f),
                        material);
                }
            }
        }

        private static void CreateRoundedPrismPart(
            Transform parent,
            string objectName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = objectName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            DisableRendererShadows(renderer);
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void ApplyVisualHighlights()
        {
            ApplyHumanHandHighlights(privateHandVisuals);
            ApplyHumanHandHighlights(standingHandVisuals.Where(visual => visual.Seat == 0));

            foreach (TileVisual visual in discardVisuals)
            {
                if (visual.Face == null) continue;
                bool match = highlightedTileIndex >= 0 && visual.TileIndex == highlightedTileIndex;
                bool callTarget = pendingCallHighlight && visual.IsPending;
                if (visual.MatchOverlay != null)
                {
                    Renderer overlayRenderer = visual.MatchOverlay.GetComponent<Renderer>();
                    if (overlayRenderer != null)
                        overlayRenderer.sharedMaterial = callTarget
                            ? callHighlightOverlayMaterial
                            : tileMatchOverlayMaterial;
                    visual.MatchOverlay.SetActive(match || callTarget);
                }
                visual.Face.color = callTarget
                    ? new Color(1f, 0.82f, 0.24f, 1f)
                    : visual.IsPending
                        ? new Color(1f, 0.48f, 0.30f, 1f)
                        : Color.white;
                float callPulse = callTarget
                    ? 0.075f + Mathf.Sin(Time.unscaledTime * 18f) * 0.018f
                    : 0f;
                visual.Root.localPosition = visual.BaseLocalPosition
                    + (visual.IsPending ? Vector3.up * 0.055f : Vector3.zero)
                    + Vector3.up * callPulse;
            }

            foreach (TileVisual visual in doraVisuals)
            {
                if (visual.Face == null) continue;
                bool match = highlightedTileIndex >= 0 && visual.TileIndex == highlightedTileIndex;
                if (visual.MatchOverlay != null) visual.MatchOverlay.SetActive(match);
                visual.Face.color = Color.white;
            }
        }

        private void ApplyHumanHandHighlights(IEnumerable<TileVisual> visuals)
        {
            foreach (TileVisual visual in visuals)
            {
                if (visual.Face == null || visual.HandIndex < 0) continue;
                bool selected = visual.HandIndex == selectedHumanIndex;
                bool riichiChoice = visual.HandIndex < riichiDiscardChoices.Length
                    && riichiDiscardChoices[visual.HandIndex];
                visual.Face.color = selected
                    ? new Color(1f, 0.68f, 0.28f, 1f)
                    : riichiChoice
                        ? new Color(0.38f, 0.78f, 1f, 1f)
                        : Color.white;
            }
        }

        private void AnimateHandTiles()
        {
            float arrangeInterpolation = 1f - Mathf.Exp(-handArrangeSpeed * Time.unscaledDeltaTime);
            foreach (TileVisual visual in standingHandVisuals)
            {
                if (visual.Root == null) continue;
                if (visual.IsWinningReveal && Time.unscaledTime < visual.RevealStartTime)
                    continue;
                visual.Root.localPosition = Vector3.Lerp(
                    visual.Root.localPosition,
                    visual.BaseLocalPosition,
                    arrangeInterpolation);
                visual.Root.localRotation = Quaternion.Slerp(
                    visual.Root.localRotation,
                    visual.TargetLocalRotation,
                    arrangeInterpolation);
            }

            float hoverInterpolation = 1f - Mathf.Exp(-hoverSpeed * Time.unscaledDeltaTime);
            float scaleInterpolation = 1f - Mathf.Exp(-scaleAnimationSpeed * Time.unscaledDeltaTime);
            foreach (TileVisual visual in privateHandVisuals)
            {
                if (!visual.IsPrivateHumanHand || visual.Root == null) continue;
                bool hovered = visual.HandIndex == hoveredHandIndex;
                // 선택 상태는 색으로만 표시한다. 패를 들어 올리는 동작은 실제 마우스 오버에만 적용해
                // 선택한 패가 계속 커 보이거나 이웃 패와 겹쳐 보이지 않게 한다.
                bool lifted = hovered;
                Vector3 target = visual.BaseLocalPosition + (lifted ? Vector3.up * hoverLift : Vector3.zero);
                // Enlarging a camera-facing tile makes it cover its neighbours and can make two
                // different tiles look like one stacked tile. Selection is communicated by lift.
                float scaleMultiplier = 1f;
                visual.Root.localPosition = Vector3.Lerp(visual.Root.localPosition, target, hoverInterpolation);
                visual.Root.localScale = Vector3.Lerp(
                    visual.Root.localScale,
                    visual.BaseLocalScale * scaleMultiplier,
                    scaleInterpolation);
            }
        }

        private void UpdateExpiredDiscardGaps()
        {
            for (int seat = 1; seat < temporaryGapIndices.Length; seat++)
            {
                if (temporaryGapIndices[seat] < 0 || Time.unscaledTime < temporaryGapDeadlines[seat])
                    continue;
                temporaryGapIndices[seat] = -1;
                CompactStandingHandSeat(seat);
            }
        }

        private void CompactStandingHandSeat(int seat)
        {
            List<TileVisual> visuals = standingHandVisuals
                .Where(visual => visual.Seat == seat)
                .OrderBy(visual => visual.HandIndex)
                .ToList();
            float[] positions = BuildHandXPositions(visuals.Count, -1, -1);
            for (int index = 0; index < visuals.Count; index++)
            {
                Vector3 seatLocal = new Vector3(
                    positions[index],
                    tableSurfaceHeight + standingHandVerticalOffset,
                    -standingHandDistanceFromCenter);
                visuals[index].BaseLocalPosition = RotateForSeat(seatLocal, seat);
            }
        }

        private static Quaternion GetStandingHandRotation(int seat)
        {
            return Quaternion.Euler(0f, SeatRotation(seat), 0f)
                * Quaternion.Euler(-90f, 0f, 0f);
        }

        private static Vector3 RotateForSeat(Vector3 position, int seat)
        {
            return Quaternion.Euler(0f, SeatRotation(seat), 0f) * position;
        }

        private static float SeatRotation(int seat)
        {
            switch (seat)
            {
                case 1: return -90f;
                case 2: return 180f;
                case 3: return 90f;
                default: return 0f;
            }
        }

        private static string BuildNestedKey(int[][] values)
        {
            if (values == null) return string.Empty;
            return string.Join("|", values.Select(row => row == null ? string.Empty : string.Join(",", row)));
        }

        private static string BuildMeldKey(Mahjong3DMeldVisual[][] melds)
        {
            if (melds == null) return string.Empty;
            return string.Join("|", melds.Select(seat => seat == null
                ? string.Empty
                : string.Join("/", seat.Select(meld => meld == null
                    ? string.Empty
                    : (meld.ConcealedKan ? "C:" : meld.AddedKan ? "A:" : "O:")
                        + meld.CalledFromSeat + ":" + meld.CalledTileIndex + ":"
                        + meld.AddedTileIndex + ":"
                        + string.Join(",", meld.TileIndices ?? Array.Empty<int>())))));
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                GameObject child = parent.GetChild(index).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private static void ClearVisuals(List<TileVisual> visuals)
        {
            visuals.Clear();
        }

        private void OnDestroy()
        {
            Material[] materials =
            {
                tableMaterial, tileBodyMaterial, tileBaseMaterial,
                tileBackMaterial, riichiMaterial, riichiMarkMaterial,
                scorePanelMaterial, activeScorePanelMaterial,
                roomMaterial, tileMatchOverlayMaterial, callHighlightOverlayMaterial
            };
            foreach (Material material in materials)
                if (material != null) Destroy(material);
        }
    }
}
