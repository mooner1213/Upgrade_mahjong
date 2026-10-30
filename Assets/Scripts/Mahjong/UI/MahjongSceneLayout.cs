using System;
using UnityEngine;
using UnityEngine.UI;

namespace UpgradeMahjong
{
    [Serializable]
    public sealed class MahjongUiRectLayout
    {
        [InspectorName("앵커 최소값")]
        public Vector2 anchorMin;

        [InspectorName("앵커 최대값")]
        public Vector2 anchorMax;

        [InspectorName("위치")]
        public Vector2 position;

        [InspectorName("크기")]
        public Vector2 size;

        [InspectorName("회전 각도")]
        [Range(-180f, 180f)]
        public float rotation;

        public MahjongUiRectLayout(Vector2 anchor, Vector2 position, Vector2 size, float rotation = 0f)
            : this(anchor, anchor, position, size, rotation)
        {
        }

        public MahjongUiRectLayout(Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, float rotation = 0f)
        {
            this.anchorMin = anchorMin;
            this.anchorMax = anchorMax;
            this.position = position;
            this.size = size;
            this.rotation = rotation;
        }

        public void Apply(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }
    }

    /// <summary>
    /// Scene에 저장되는 마작 UI의 정적 배치와 반복 UI Prefab 참조입니다.
    /// 게임 규칙은 갖지 않으며, Inspector에서 조정한 값을 런타임 뷰에 제공합니다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MahjongSceneLayout : MonoBehaviour
    {
        [Header("Scene UI 참조")]
        [SerializeField] private Canvas hudCanvas;
        [SerializeField] private RectTransform statusRoot;
        [SerializeField] private RectTransform lastDiscardRoot;
        [SerializeField] private RectTransform handRoot;
        [SerializeField] private RectTransform actionRoot;
        [SerializeField] private RectTransform doraRoot;
        [SerializeField] private RectTransform meldChoiceRoot;
        [SerializeField] private RectTransform resultRoot;
        [SerializeField] private RectTransform ownMeldRoot;

        [Header("플레이어 정보 참조")]
        [SerializeField] private RectTransform bottomPlayerInfoRoot;
        [SerializeField] private RectTransform rightPlayerInfoRoot;
        [SerializeField] private RectTransform topPlayerInfoRoot;
        [SerializeField] private RectTransform leftPlayerInfoRoot;

        [Header("작탁 위 버림패 영역 참조")]
        [SerializeField] private RectTransform bottomDiscardRoot;
        [SerializeField] private RectTransform rightDiscardRoot;
        [SerializeField] private RectTransform topDiscardRoot;
        [SerializeField] private RectTransform leftDiscardRoot;

        [Header("반복 UI Prefab")]
        [Tooltip("손패 한 장의 기본 Prefab입니다. 비어 있으면 기존 코드 생성 방식을 사용합니다.")]
        [SerializeField] private GameObject handTilePrefab;
        [Tooltip("치·퐁·깡 등의 행동 버튼 Prefab입니다. 비어 있으면 기존 코드 생성 방식을 사용합니다.")]
        [SerializeField] private Button actionButtonPrefab;

        [Header("상단 상태 문구")]
        [SerializeField] private MahjongUiRectLayout statusLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(900f, 48f));
        [SerializeField] private MahjongUiRectLayout lastDiscardLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 0.5f), new Vector2(0f, 175f), new Vector2(360f, 42f));

        [Header("플레이어 정보")]
        [SerializeField] private MahjongUiRectLayout bottomPlayerInfoLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(520f, 42f));
        [SerializeField] private MahjongUiRectLayout rightPlayerInfoLayout =
            new MahjongUiRectLayout(new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(250f, 90f));
        [SerializeField] private MahjongUiRectLayout topPlayerInfoLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(520f, 42f));
        [SerializeField] private MahjongUiRectLayout leftPlayerInfoLayout =
            new MahjongUiRectLayout(new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(250f, 90f));

        [Header("작탁 위 버림패 영역")]
        [SerializeField] private MahjongUiRectLayout bottomDiscardLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 0f), new Vector2(0f, 370f), new Vector2(430f, 175f));
        [SerializeField] private MahjongUiRectLayout rightDiscardLayout =
            new MahjongUiRectLayout(new Vector2(1f, 0.5f), new Vector2(-600f, 0f), new Vector2(430f, 175f), 90f);
        [SerializeField] private MahjongUiRectLayout topDiscardLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(430f, 175f), 180f);
        [SerializeField] private MahjongUiRectLayout leftDiscardLayout =
            new MahjongUiRectLayout(new Vector2(0f, 0.5f), new Vector2(600f, 0f), new Vector2(430f, 175f), -90f);

        [Header("내 손패")]
        [SerializeField] private MahjongUiRectLayout handLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1450f, 176f));
        [InspectorName("패 사이 간격")]
        [SerializeField] private float handSpacing = -2f;
        [InspectorName("패 슬롯 크기")]
        [SerializeField] private Vector2 handTileSlotSize = new Vector2(46f, 168f);
        [InspectorName("패 이미지 크기")]
        [SerializeField] private Vector2 handTileVisualSize = new Vector2(94f, 138f);
        [InspectorName("패 이미지 위치")]
        [SerializeField] private Vector2 handTileVisualPosition = new Vector2(0f, -15f);
        [InspectorName("마우스 감지 영역")]
        [SerializeField] private Vector2 handTileHitSize = new Vector2(46f, 168f);
        [InspectorName("새로 받은 패 간격")]
        [Min(0f)] [SerializeField] private float freshDrawGap = 12f;
        [InspectorName("마우스 오버 상승 거리")]
        [Min(0f)] [SerializeField] private float handHoverLift = 30f;

        [Header("행동 및 조합 선택창")]
        [SerializeField] private MahjongUiRectLayout actionLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(980f, 196f));
        [SerializeField] private MahjongUiRectLayout meldChoiceLayout =
            new MahjongUiRectLayout(new Vector2(0.5f, 0f), new Vector2(0f, 188f), new Vector2(760f, 248f));
        [InspectorName("행동 버튼 크기")]
        [SerializeField] private Vector2 actionButtonSize = new Vector2(220f, 72f);
        [InspectorName("행동 버튼 간격")]
        [SerializeField] private Vector2 actionButtonSpacing = new Vector2(10f, 10f);
        [InspectorName("한 줄 버튼 수")]
        [Min(1)] [SerializeField] private int actionColumnCount = 4;

        [Header("도라 및 내 공개패")]
        [SerializeField] private MahjongUiRectLayout doraLayout =
            new MahjongUiRectLayout(new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(420f, 144f));
        [SerializeField] private MahjongUiRectLayout ownMeldLayout =
            new MahjongUiRectLayout(new Vector2(1f, 0f), new Vector2(-28f, 238f), new Vector2(340f, 142f));

        [Header("결과창")]
        [SerializeField] private MahjongUiRectLayout resultLayout =
            new MahjongUiRectLayout(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-32f, -32f));

        [Header("작탁 위 버림패 칸 맞춤")]
        [SerializeField] private float[] discardColumnAnchors = { 0.14f, 0.284f, 0.428f, 0.572f, 0.716f, 0.86f };
        [SerializeField] private float[] discardRowAnchors = { 0.78f, 0.50f, 0.22f };
        [InspectorName("기준 패 크기")]
        [SerializeField] private Vector2 discardTileReferenceSize = new Vector2(46f, 64f);

        public Canvas HudCanvas => hudCanvas;
        public RectTransform StatusRoot => statusRoot;
        public RectTransform LastDiscardRoot => lastDiscardRoot;
        public RectTransform HandRoot => handRoot;
        public RectTransform ActionRoot => actionRoot;
        public RectTransform DoraRoot => doraRoot;
        public RectTransform MeldChoiceRoot => meldChoiceRoot;
        public RectTransform ResultRoot => resultRoot;
        public RectTransform OwnMeldRoot => ownMeldRoot;
        public GameObject HandTilePrefab => handTilePrefab;
        public Button ActionButtonPrefab => actionButtonPrefab;
        public float HandSpacing => handSpacing;
        public Vector2 HandTileSlotSize => handTileSlotSize;
        public Vector2 HandTileVisualSize => handTileVisualSize;
        public Vector2 HandTileVisualPosition => handTileVisualPosition;
        public Vector2 HandTileHitSize => handTileHitSize;
        public float FreshDrawGap => freshDrawGap;
        public float HandHoverLift => handHoverLift;
        public Vector2 ActionButtonSize => actionButtonSize;
        public Vector2 ActionButtonSpacing => actionButtonSpacing;
        public int ActionColumnCount => Mathf.Max(1, actionColumnCount);
        public float[] DiscardColumnAnchors => discardColumnAnchors;
        public float[] DiscardRowAnchors => discardRowAnchors;
        public Vector2 DiscardTileReferenceSize => discardTileReferenceSize;

        public RectTransform GetPlayerInfoRoot(int playerIndex)
        {
            switch (playerIndex)
            {
                case 1: return rightPlayerInfoRoot;
                case 2: return topPlayerInfoRoot;
                case 3: return leftPlayerInfoRoot;
                default: return bottomPlayerInfoRoot;
            }
        }

        public RectTransform GetDiscardRoot(int playerIndex)
        {
            switch (playerIndex)
            {
                case 1: return rightDiscardRoot;
                case 2: return topDiscardRoot;
                case 3: return leftDiscardRoot;
                default: return bottomDiscardRoot;
            }
        }

        public MahjongUiRectLayout GetDiscardLayout(int playerIndex)
        {
            switch (playerIndex)
            {
                case 1: return rightDiscardLayout;
                case 2: return topDiscardLayout;
                case 3: return leftDiscardLayout;
                default: return bottomDiscardLayout;
            }
        }

        [ContextMenu("Scene 참조 자동 연결")]
        public void AutoBindFromChildren()
        {
            if (hudCanvas == null) hudCanvas = GetComponentInChildren<Canvas>(true);
            if (hudCanvas == null) return;

            Transform canvasTransform = hudCanvas.transform;
            statusRoot = FindRect(canvasTransform, "StatusRoot");
            lastDiscardRoot = FindRect(canvasTransform, "LastDiscardRoot");
            handRoot = FindRect(canvasTransform, "HandPanel");
            actionRoot = FindRect(canvasTransform, "ActionPanel");
            doraRoot = FindRect(canvasTransform, "DoraIndicatorPanel");
            meldChoiceRoot = FindRect(canvasTransform, "MeldChoicePanel");
            resultRoot = FindRect(canvasTransform, "WinResultOverlay");
            ownMeldRoot = FindRect(canvasTransform, "MyOpenMelds");
            bottomPlayerInfoRoot = FindRect(canvasTransform, "PlayerInfo_Bottom");
            rightPlayerInfoRoot = FindRect(canvasTransform, "PlayerInfo_Right");
            topPlayerInfoRoot = FindRect(canvasTransform, "PlayerInfo_Top");
            leftPlayerInfoRoot = FindRect(canvasTransform, "PlayerInfo_Left");
            bottomDiscardRoot = FindRect(canvasTransform, "BoardArea_Bottom");
            rightDiscardRoot = FindRect(canvasTransform, "BoardArea_Right");
            topDiscardRoot = FindRect(canvasTransform, "BoardArea_Top");
            leftDiscardRoot = FindRect(canvasTransform, "BoardArea_Left");
            CaptureAuthoringLayoutFromScene();
        }

        [ContextMenu("Inspector 배치값 적용")]
        public void ApplyAuthoringLayout()
        {
            statusLayout?.Apply(statusRoot);
            lastDiscardLayout?.Apply(lastDiscardRoot);
            handLayout?.Apply(handRoot);
            actionLayout?.Apply(actionRoot);
            doraLayout?.Apply(doraRoot);
            meldChoiceLayout?.Apply(meldChoiceRoot);
            resultLayout?.Apply(resultRoot);
            ownMeldLayout?.Apply(ownMeldRoot);
            bottomPlayerInfoLayout?.Apply(bottomPlayerInfoRoot);
            rightPlayerInfoLayout?.Apply(rightPlayerInfoRoot);
            topPlayerInfoLayout?.Apply(topPlayerInfoRoot);
            leftPlayerInfoLayout?.Apply(leftPlayerInfoRoot);
            bottomDiscardLayout?.Apply(bottomDiscardRoot);
            rightDiscardLayout?.Apply(rightDiscardRoot);
            topDiscardLayout?.Apply(topDiscardRoot);
            leftDiscardLayout?.Apply(leftDiscardRoot);
        }

        [ContextMenu("Scene의 현재 위치를 설정값으로 저장")]
        public void CaptureAuthoringLayoutFromScene()
        {
            Capture(statusRoot, statusLayout);
            Capture(lastDiscardRoot, lastDiscardLayout);
            Capture(handRoot, handLayout);
            Capture(actionRoot, actionLayout);
            Capture(doraRoot, doraLayout);
            Capture(meldChoiceRoot, meldChoiceLayout);
            Capture(resultRoot, resultLayout);
            Capture(ownMeldRoot, ownMeldLayout);
            Capture(bottomPlayerInfoRoot, bottomPlayerInfoLayout);
            Capture(rightPlayerInfoRoot, rightPlayerInfoLayout);
            Capture(topPlayerInfoRoot, topPlayerInfoLayout);
            Capture(leftPlayerInfoRoot, leftPlayerInfoLayout);
            Capture(bottomDiscardRoot, bottomDiscardLayout);
            Capture(rightDiscardRoot, rightDiscardLayout);
            Capture(topDiscardRoot, topDiscardLayout);
            Capture(leftDiscardRoot, leftDiscardLayout);
        }

        public void AssignPrefabs(GameObject tilePrefab, Button buttonPrefab)
        {
            handTilePrefab = tilePrefab;
            actionButtonPrefab = buttonPrefab;
        }

        private void OnValidate()
        {
            actionColumnCount = Mathf.Max(1, actionColumnCount);
            freshDrawGap = Mathf.Max(0f, freshDrawGap);
            handHoverLift = Mathf.Max(0f, handHoverLift);
        }

        private static void Capture(RectTransform rect, MahjongUiRectLayout layout)
        {
            if (rect == null || layout == null) return;
            layout.anchorMin = rect.anchorMin;
            layout.anchorMax = rect.anchorMax;
            layout.position = rect.anchoredPosition;
            layout.size = rect.sizeDelta;
            float angle = rect.localEulerAngles.z;
            layout.rotation = angle > 180f ? angle - 360f : angle;
        }

        private static RectTransform FindRect(Transform root, string objectName)
        {
            if (root == null) return null;
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == objectName) return rect;
            return null;
        }
    }
}
