using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UpgradeMahjong.EditorTools
{
    [CustomEditor(typeof(MahjongSceneLayout))]
    public sealed class MahjongSceneLayoutEditor : Editor
    {
        private bool showPlayerInfo;
        private bool showOtherPanels = true;
        private bool showPrefabs;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MahjongSceneLayout layout = (MahjongSceneLayout)target;

            EditorGUILayout.HelpBox(
                "아래 위치·크기·각도는 Scene의 실제 UI 오브젝트를 직접 변경합니다. " +
                "Scene 창에서 오브젝트를 옮겨도 그 위치가 그대로 게임에 적용됩니다.",
                MessageType.Info);

            DrawTitle("작탁 위 버림패 영역 위치·크기·각도");
            DrawRectLayout("내 버림패 영역 (동·아래)", layout.GetDiscardRoot(0), layout);
            DrawRectLayout("상가 버림패 영역 (남·오른쪽)", layout.GetDiscardRoot(1), layout);
            DrawRectLayout("대가 버림패 영역 (서·맞은편)", layout.GetDiscardRoot(2), layout);
            DrawRectLayout("하가 버림패 영역 (북·왼쪽)", layout.GetDiscardRoot(3), layout);

            EditorGUILayout.Space(8f);
            DrawTitle("내 손패 설정");
            DrawRectLayout("손패 영역", layout.HandRoot, layout);
            DrawProperty("handSpacing", "패 사이 간격");
            DrawProperty("handTileSlotSize", "패 슬롯 크기");
            DrawProperty("handTileVisualSize", "패 이미지 크기");
            DrawProperty("handTileVisualPosition", "패 이미지 위치");
            DrawProperty("handTileHitSize", "마우스 감지 영역 크기");
            DrawProperty("freshDrawGap", "새로 받은 패와의 간격");
            DrawProperty("handHoverLift", "마우스 오버 상승 거리");

            EditorGUILayout.Space(8f);
            DrawTitle("행동 버튼과 조합 선택창");
            DrawRectLayout("행동 버튼 영역", layout.ActionRoot, layout);
            DrawRectLayout("치·퐁·깡 조합 선택 영역", layout.MeldChoiceRoot, layout);
            DrawProperty("actionButtonSize", "행동 버튼 크기");
            DrawProperty("actionButtonSpacing", "행동 버튼 사이 간격");
            DrawProperty("actionColumnCount", "한 줄에 표시할 버튼 수");

            EditorGUILayout.Space(8f);
            showOtherPanels = EditorGUILayout.Foldout(showOtherPanels, "그 밖의 UI 위치·크기·각도", true);
            if (showOtherPanels)
            {
                EditorGUI.indentLevel++;
                DrawRectLayout("상단 상태 문구", layout.StatusRoot, layout);
                DrawRectLayout("최근 버림패 문구", layout.LastDiscardRoot, layout);
                DrawRectLayout("도라 표시창", layout.DoraRoot, layout);
                DrawRectLayout("내 공개패 영역", layout.OwnMeldRoot, layout);
                DrawRectLayout("화료 결과창", layout.ResultRoot, layout);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6f);
            showPlayerInfo = EditorGUILayout.Foldout(showPlayerInfo, "플레이어 정보 위치·크기·각도", true);
            if (showPlayerInfo)
            {
                EditorGUI.indentLevel++;
                DrawRectLayout("내 정보 (동·아래)", layout.GetPlayerInfoRoot(0), layout);
                DrawRectLayout("상가 정보 (남·오른쪽)", layout.GetPlayerInfoRoot(1), layout);
                DrawRectLayout("대가 정보 (서·맞은편)", layout.GetPlayerInfoRoot(2), layout);
                DrawRectLayout("하가 정보 (북·왼쪽)", layout.GetPlayerInfoRoot(3), layout);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8f);
            DrawTitle("작탁 위 버림패 칸 맞춤");
            DrawProperty("discardColumnAnchors", "가로 6칸 위치 비율", true);
            DrawProperty("discardRowAnchors", "세로 3줄 위치 비율", true);
            DrawProperty("discardTileReferenceSize", "버림패 한 장 크기");

            EditorGUILayout.Space(6f);
            showPrefabs = EditorGUILayout.Foldout(showPrefabs, "마작패 및 버튼 프리팹", true);
            if (showPrefabs)
            {
                EditorGUI.indentLevel++;
                DrawProperty("handTilePrefab", "손패 한 장 프리팹");
                DrawProperty("actionButtonPrefab", "행동 버튼 프리팹");
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Scene UI 참조 다시 찾기"))
            {
                Undo.RecordObject(layout, "Scene UI 참조 다시 찾기");
                layout.AutoBindFromChildren();
                EditorUtility.SetDirty(layout);
                MarkSceneDirty(layout);
                serializedObject.Update();
            }
        }

        private void DrawRectLayout(string label, RectTransform rect, MahjongSceneLayout layout)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                if (rect == null)
                {
                    EditorGUILayout.HelpBox("연결된 Scene UI 오브젝트가 없습니다.", MessageType.Warning);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                Vector2 position = EditorGUILayout.Vector2Field("위치", rect.anchoredPosition);
                Vector2 size = EditorGUILayout.Vector2Field("크기", rect.sizeDelta);
                float currentAngle = NormalizeAngle(rect.localEulerAngles.z);
                float rotation = EditorGUILayout.Slider("회전 각도", currentAngle, -180f, 180f);
                if (!EditorGUI.EndChangeCheck()) return;

                Undo.RecordObject(rect, label + " 변경");
                rect.anchoredPosition = position;
                rect.sizeDelta = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
                rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
                EditorUtility.SetDirty(rect);

                Undo.RecordObject(layout, label + " 설정 저장");
                layout.CaptureAuthoringLayoutFromScene();
                EditorUtility.SetDirty(layout);
                MarkSceneDirty(layout);
            }
        }

        private void DrawProperty(string propertyName, string koreanLabel, bool includeChildren = false)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(koreanLabel), includeChildren);
        }

        private static void DrawTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private static void MarkSceneDirty(MahjongSceneLayout layout)
        {
            if (layout != null && layout.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
        }
    }
}
