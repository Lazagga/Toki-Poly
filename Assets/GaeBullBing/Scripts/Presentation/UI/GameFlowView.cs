using System;
using System.Collections;
using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class GameFlowView : MonoBehaviour
    {
        [SerializeField] private GameObject titleRoot;
        [SerializeField] private Button startButton;
        [SerializeField] private GameObject defeatRoot;
        [SerializeField] private CanvasGroup defeatCanvasGroup;
        [SerializeField] private Button defeatTitleButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private GameObject victoryRoot;
        [SerializeField] private CanvasGroup victoryCanvasGroup;
        [SerializeField] private Button victoryTitleButton;
        [SerializeField] private Text defeatStatisticsLeftText;
        [SerializeField] private Text defeatStatisticsRightText;
        [SerializeField] private Text victoryStatisticsLeftText;
        [SerializeField] private Text victoryStatisticsRightText;
        [SerializeField] private CanvasGroup gameplayUi;
        [SerializeField] private GameBoardTransition boardTransition;
        [SerializeField] private GameObject transitionBlocker;
        [SerializeField] private CanvasGroup titleCanvasGroup;
        [SerializeField] private RectTransform leftPortrait;
        [SerializeField] private RectTransform rightPortrait;
        [SerializeField] private CanvasGroup leftPortraitGroup;
        [SerializeField] private CanvasGroup rightPortraitGroup;
        [SerializeField, Min(.05f)] private float titleFadeDuration = .55f;
        [SerializeField, Min(.05f)] private float resultFadeDuration = .45f;
        [SerializeField, Min(0f)] private float portraitTravelDistance = 220f;

        private GameController controller;
        private Coroutine transitionRoutine;
        private Coroutine portraitFadeRoutine;
        private Vector2 leftPortraitStart;
        private Vector2 rightPortraitStart;

        public void Bind(GameController gameController)
        {
            controller = gameController;
            leftPortraitStart = leftPortrait.anchoredPosition;
            rightPortraitStart = rightPortrait.anchoredPosition;
            ResetTitleVisuals();
            Bind(startButton, BeginStartTransition);
            Bind(defeatTitleButton, () => BeginResultExit(defeatCanvasGroup, controller.ReturnToTitle));
            Bind(restartButton, () => BeginResultExit(defeatCanvasGroup, controller.RestartGame));
            Bind(victoryTitleButton, () => BeginResultExit(victoryCanvasGroup, controller.ReturnToTitle));
        }

        public void ShowTitle(bool fadePortraits = false)
        {
            StopTransition();
            ResetTitleVisuals();
            SetGameplayVisible(false);
            boardTransition.PrepareHidden();
            ShowOnly(titleRoot);
            if (fadePortraits)
            {
                SetPortraitAlpha(0f);
                startButton.interactable = false;
                portraitFadeRoutine = StartCoroutine(FadePortraitsIn());
            }
        }
        public void ShowDefeat()
        {
            controller.BuildResultStatistics(out var left, out var right);
            if (defeatStatisticsLeftText != null) defeatStatisticsLeftText.text = left;
            if (defeatStatisticsRightText != null) defeatStatisticsRightText.text = right;
            ShowResult(defeatRoot, defeatCanvasGroup);
        }

        public void ShowVictory()
        {
            controller.BuildResultStatistics(out var left, out var right);
            if (victoryStatisticsLeftText != null) victoryStatisticsLeftText.text = left;
            if (victoryStatisticsRightText != null) victoryStatisticsRightText.text = right;
            ShowResult(victoryRoot, victoryCanvasGroup);
        }

        public void BeginRestart()
        {
            StopTransition();
            HideAll();
            SetGameplayVisible(false);
            boardTransition.PrepareHidden();
            transitionRoutine = StartCoroutine(PlayIntro());
        }

        public IEnumerator PlayOutro()
        {
            StopTransition();
            HideAll();
            transitionBlocker.SetActive(true);
            SetGameplayVisible(false);
            yield return boardTransition.PlayOutro();
            transitionBlocker.SetActive(false);
        }

        public void HideAll()
        {
            titleRoot.SetActive(false);
            defeatRoot.SetActive(false);
            victoryRoot.SetActive(false);
            transitionBlocker.SetActive(false);
        }

        private void ShowOnly(GameObject target)
        {
            HideAll();
            target.SetActive(true);
        }

        private void BeginStartTransition()
        {
            if (transitionRoutine != null) return;
            transitionRoutine = StartCoroutine(PlayTitleExitThenIntro());
        }

        private void ShowResult(GameObject root, CanvasGroup canvasGroup)
        {
            StopTransition();
            ShowOnly(root);
            if (canvasGroup == null)
            {
                Debug.LogError($"{root.name}의 CanvasGroup이 연결되지 않았습니다.");
                return;
            }
            SetCanvasGroup(canvasGroup, 0f, false);
            transitionRoutine = StartCoroutine(FadeResultIn(canvasGroup));
        }

        private IEnumerator FadeResultIn(CanvasGroup canvasGroup)
        {
            yield return FadeCanvas(canvasGroup, 0f, 1f);
            SetCanvasGroup(canvasGroup, 1f, true);
            transitionRoutine = null;
        }

        private void BeginResultExit(CanvasGroup canvasGroup, Action onComplete)
        {
            if (transitionRoutine != null || canvasGroup == null) return;
            transitionRoutine = StartCoroutine(FadeResultOut(canvasGroup, onComplete));
        }

        private IEnumerator FadeResultOut(CanvasGroup canvasGroup, Action onComplete)
        {
            SetCanvasGroup(canvasGroup, canvasGroup.alpha, false);
            transitionBlocker.SetActive(true);
            yield return FadeCanvas(canvasGroup, canvasGroup.alpha, 0f);
            transitionRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator FadeCanvas(CanvasGroup canvasGroup, float from, float to)
        {
            for (var elapsed = 0f; elapsed < resultFadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / resultFadeDuration));
                canvasGroup.alpha = Mathf.Lerp(from, to, progress);
                yield return null;
            }
            canvasGroup.alpha = to;
        }

        private static void SetCanvasGroup(CanvasGroup canvasGroup, float alpha, bool interactive)
        {
            canvasGroup.alpha = alpha;
            canvasGroup.interactable = interactive;
            canvasGroup.blocksRaycasts = interactive;
        }

        private IEnumerator PlayTitleExitThenIntro()
        {
            transitionBlocker.SetActive(true);
            startButton.interactable = false;
            for (var elapsed = 0f; elapsed < titleFadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / titleFadeDuration));
                titleCanvasGroup.alpha = 1f - progress;
                leftPortrait.anchoredPosition = Vector2.Lerp(
                    leftPortraitStart,
                    leftPortraitStart + Vector2.left * portraitTravelDistance,
                    progress);
                rightPortrait.anchoredPosition = Vector2.Lerp(
                    rightPortraitStart,
                    rightPortraitStart + Vector2.right * portraitTravelDistance,
                    progress);
                yield return null;
            }
            titleCanvasGroup.alpha = 0f;
            yield return PlayIntro();
        }

        private IEnumerator PlayIntro()
        {
            HideAll();
            transitionBlocker.SetActive(true);
            SetGameplayVisible(false);
            yield return boardTransition.PlayIntro();
            SetGameplayVisible(true);
            transitionBlocker.SetActive(false);
            transitionRoutine = null;
            controller.StartGameFromTitle();
        }

        private void SetGameplayVisible(bool visible)
        {
            gameplayUi.alpha = visible ? 1f : 0f;
            gameplayUi.interactable = visible;
            gameplayUi.blocksRaycasts = visible;
        }

        private void StopTransition()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }
            if (portraitFadeRoutine != null)
            {
                StopCoroutine(portraitFadeRoutine);
                portraitFadeRoutine = null;
            }
        }

        private void ResetTitleVisuals()
        {
            titleCanvasGroup.alpha = 1f;
            leftPortrait.anchoredPosition = leftPortraitStart;
            rightPortrait.anchoredPosition = rightPortraitStart;
            SetPortraitAlpha(1f);
            startButton.interactable = true;
        }

        private IEnumerator FadePortraitsIn()
        {
            leftPortrait.anchoredPosition = leftPortraitStart;
            rightPortrait.anchoredPosition = rightPortraitStart;
            for (var elapsed = 0f; elapsed < titleFadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                SetPortraitAlpha(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / titleFadeDuration)));
                yield return null;
            }
            SetPortraitAlpha(1f);
            startButton.interactable = true;
            portraitFadeRoutine = null;
        }

        private void SetPortraitAlpha(float alpha)
        {
            if (leftPortraitGroup != null) leftPortraitGroup.alpha = alpha;
            if (rightPortraitGroup != null) rightPortraitGroup.alpha = alpha;
        }

        private static void Bind(Button button, Action action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
        }
    }
}
