using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UpgradeMahjong.Mahjong
{
    public sealed class RiichiHudController : MonoBehaviour
    {
        [SerializeField] private RiichiGameManager gameManager;
        [SerializeField] private RectTransform actionRoot;
        [SerializeField] private Button actionButtonPrefab;
        [SerializeField] private Slider countdownSlider;
        [SerializeField] private Text promptText;
        [SerializeField] private GameObject resultOverlay;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultDetail;
        [SerializeField] private Button nextRoundButton;

        private readonly List<Button> actionButtons = new List<Button>();

        private void OnEnable()
        {
            if (gameManager == null) gameManager = FindObjectOfType<RiichiGameManager>();
            if (gameManager != null)
            {
                gameManager.ActionPromptChanged += RefreshActionPrompt;
                gameManager.PhaseChanged += OnPhaseChanged;
                gameManager.HandWon += ShowHandResult;
            }
            if (resultOverlay != null) resultOverlay.SetActive(false);
            if (nextRoundButton != null) nextRoundButton.onClick.AddListener(StartNextRound);
            RefreshActionPrompt(gameManager == null ? null : gameManager.CurrentPrompt);
        }

        private void OnDisable()
        {
            if (gameManager != null)
            {
                gameManager.ActionPromptChanged -= RefreshActionPrompt;
                gameManager.PhaseChanged -= OnPhaseChanged;
                gameManager.HandWon -= ShowHandResult;
            }
            if (nextRoundButton != null) nextRoundButton.onClick.RemoveListener(StartNextRound);
        }

        private void OnPhaseChanged(RiichiPhase phase)
        {
            RefreshActionPrompt(gameManager == null ? null : gameManager.CurrentPrompt);
        }

        private void StartNextRound()
        {
            if (resultOverlay != null) resultOverlay.SetActive(false);
            if (gameManager != null) gameManager.StartRound();
        }

        private void ShowHandResult(string method, ScoreBreakdown score)
        {
            if (resultOverlay == null) return;
            if (resultTitle != null) resultTitle.text = method + " 화료";
            if (resultDetail != null)
            {
                if (score == null)
                {
                    resultDetail.text = "화료는 확인했지만 점수 계산 결과가 없습니다.";
                }
                else
                {
                    string yakuNames = score.Yaku == null || score.Yaku.Yaku.Count == 0
                        ? "역 없음"
                        : string.Join(", ", score.Yaku.Yaku.ConvertAll(yaku => yaku.Name).ToArray());
                    string points = score.RonPoints > 0
                        ? score.RonPoints.ToString("N0") + "점"
                        : "친 " + score.TsumoDealerPayment.ToString("N0") + "점 / 자 " + score.TsumoNonDealerPayment.ToString("N0") + "점";
                    resultDetail.text = "역: " + yakuNames + "\n" + score.Han + "판 " + score.Fu + "부  |  " + score.LimitName + "\n" + points;
                }
            }
            resultOverlay.SetActive(true);
        }

        private void Update()
        {
            ActionPrompt prompt = gameManager == null ? null : gameManager.CurrentPrompt;
            if (countdownSlider != null)
                countdownSlider.value = prompt == null ? 0f : Mathf.Clamp01((prompt.ExpiresAt - Time.time) / 7f);
        }

        private void RefreshActionPrompt(ActionPrompt prompt)
        {
            foreach (Button button in actionButtons)
                if (button != null) Destroy(button.gameObject);
            actionButtons.Clear();
            if (promptText != null) promptText.text = prompt == null ? string.Empty : "행동을 선택하세요";
            if (actionRoot == null || actionButtonPrefab == null || prompt == null) return;

            foreach (RiichiAction action in prompt.Actions)
            {
                Button button = Instantiate(actionButtonPrefab, actionRoot);
                button.name = "Action_" + action;
                Text label = button.GetComponentInChildren<Text>();
                if (label != null) label.text = GetLabel(action);
                RiichiAction captured = action;
                button.onClick.AddListener(() => gameManager.ResolveAction(captured));
                actionButtons.Add(button);
            }
        }

        private static string GetLabel(RiichiAction action)
        {
            switch (action)
            {
                case RiichiAction.Chi: return "치";
                case RiichiAction.Pon: return "퐁";
                case RiichiAction.OpenKan: return "깡";
                case RiichiAction.ClosedKan: return "안깡";
                case RiichiAction.AddedKan: return "대깡";
                case RiichiAction.Riichi: return "리치";
                case RiichiAction.Ron: return "론";
                case RiichiAction.Tsumo: return "쯔모";
                default: return "패스";
            }
        }
    }
}
