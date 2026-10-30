using UnityEngine;
using UnityEngine.UI;

namespace UpgradeMahjong.Mahjong
{
    public sealed class RiichiTableView : MonoBehaviour
    {
        [Header("Perspective camera")]
        [SerializeField] private Camera tableCamera;
        [SerializeField] private Transform tableCenter;
        [SerializeField] private float cameraHeight = 10.5f;
        [SerializeField] private float cameraDistance = 10.5f;
        [SerializeField] private float cameraPitch = 48f;
        [SerializeField] private float fieldOfView = 48f;

        [Header("World-space center board")]
        [SerializeField] private Text roundText;
        [SerializeField] private Text turnText;
        [SerializeField] private Text riichiSticksText;
        [SerializeField] private Text honbaText;
        [SerializeField] private Text wallText;
        [SerializeField] private Text[] doraSlots = new Text[5];

        private void Awake()
        {
            ConfigureCamera();
        }

        public void ConfigureCamera()
        {
            if (tableCamera == null) tableCamera = Camera.main;
            if (tableCamera == null) return;
            Vector3 center = tableCenter == null ? Vector3.zero : tableCenter.position;
            tableCamera.transform.position = center + new Vector3(0f, cameraHeight, -cameraDistance);
            Quaternion lookRotation = Quaternion.LookRotation(center - tableCamera.transform.position, Vector3.up);
            tableCamera.transform.rotation = Quaternion.Euler(cameraPitch, lookRotation.eulerAngles.y, 0f);
            tableCamera.fieldOfView = fieldOfView;
        }

        public void RefreshCenterBoard(string round, string turn, int riichiSticks, int honba, int remainingWall, System.Collections.Generic.IList<int> indicators)
        {
            if (roundText != null) roundText.text = round;
            if (turnText != null) turnText.text = turn;
            if (riichiSticksText != null) riichiSticksText.text = "리치봉 " + riichiSticks;
            if (honbaText != null) honbaText.text = "본장 " + honba;
            if (wallText != null) wallText.text = "패산 " + remainingWall;
            for (int i = 0; i < doraSlots.Length; i++)
                if (doraSlots[i] != null) doraSlots[i].text = indicators != null && i < indicators.Count ? indicators[i].ToString() : "-";
        }
    }
}
