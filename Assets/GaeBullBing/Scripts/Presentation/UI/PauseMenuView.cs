using System.Collections;
using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class PauseMenuView : MonoBehaviour
    {
        [Header("Scene UI")]
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private RectTransform hangingPanel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button titleButton;

        [Header("Background Fade")]
        [SerializeField, Range(0f, 1f)] private float backgroundDimAlpha = .32f;
        [SerializeField, Min(.01f)] private float fadeInDuration = .28f;
        [SerializeField, Min(.01f)] private float fadeOutDuration = .22f;

        [Header("Animation")]
        [SerializeField, Min(1f)] private float hiddenDistance = 660f;
        [SerializeField, Min(.01f)] private float descendDuration = .72f;
        [SerializeField, Min(.01f)] private float ascendDuration = .55f;
        [SerializeField, Min(0f)] private float swingAngle = 2f;
        [SerializeField, Min(0f)] private float swingCycles = 1.5f;

        private GameController controller;
        private DiceSystemView diceSystem;
        private DeveloperConsoleView developerConsole;
        private Image backgroundDimmer;
        private Coroutine animationRoutine;
        private Vector2 visiblePosition;
        private float previousTimeScale = 1f;
        private bool isOpen;
        private bool isAnimating;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            controller = FindFirstObjectByType<GameController>(FindObjectsInactive.Include);
            diceSystem = FindFirstObjectByType<DiceSystemView>(FindObjectsInactive.Include);
            developerConsole =
                FindFirstObjectByType<DeveloperConsoleView>(FindObjectsInactive.Include);
            backgroundDimmer = menuRoot != null ? menuRoot.GetComponent<Image>() : null;

            if (hangingPanel != null)
                visiblePosition = hangingPanel.anchoredPosition;
            SetBackgroundDimAlpha(0f);
            if (menuRoot != null)
                menuRoot.SetActive(false);

            Bind(continueButton, Close);
            Bind(restartButton, Restart);
            Bind(settingsButton, OpenSettings);
            Bind(titleButton, ReturnToTitle);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;

            if (isOpen)
            {
                if (!isAnimating)
                    Close();
                return;
            }

            if (controller == null || !controller.HasGameplayStarted ||
                controller.State == null || controller.State.IsFinished ||
                developerConsole != null && developerConsole.IsOpen ||
                diceSystem != null && diceSystem.HasOpenModal)
                return;

            Open();
        }

        private void Open()
        {
            if (isOpen || menuRoot == null || hangingPanel == null)
                return;

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            isOpen = true;
            menuRoot.SetActive(true);
            SetBackgroundDimAlpha(0f);
            SetButtonsInteractable(false);
            StartAnimation(AnimateOpen());
        }

        private void Close()
        {
            if (!isOpen || isAnimating)
                return;
            SetButtonsInteractable(false);
            StartAnimation(AnimateClose());
        }

        private IEnumerator AnimateOpen()
        {
            isAnimating = true;
            var hiddenPosition = visiblePosition + Vector2.up * hiddenDistance;
            hangingPanel.anchoredPosition = hiddenPosition;
            hangingPanel.localRotation = Quaternion.identity;

            var totalDuration = Mathf.Max(descendDuration, fadeInDuration);
            for (var elapsed = 0f; elapsed < totalDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / descendDuration);
                var eased = EaseOutBack(progress);
                hangingPanel.anchoredPosition =
                    Vector2.LerpUnclamped(hiddenPosition, visiblePosition, eased);
                ApplySwing(progress, 1f - progress * .45f);
                SetBackgroundDimAlpha(
                    backgroundDimAlpha * Mathf.SmoothStep(
                        0f, 1f, Mathf.Clamp01(elapsed / fadeInDuration)));
                yield return null;
            }

            hangingPanel.anchoredPosition = visiblePosition;
            hangingPanel.localRotation = Quaternion.identity;
            SetBackgroundDimAlpha(backgroundDimAlpha);
            isAnimating = false;
            SetButtonsInteractable(true);
        }

        private IEnumerator AnimateClose()
        {
            isAnimating = true;
            var hiddenPosition = visiblePosition + Vector2.up * hiddenDistance;
            hangingPanel.localRotation = Quaternion.identity;

            var totalDuration = Mathf.Max(ascendDuration, fadeOutDuration);
            for (var elapsed = 0f; elapsed < totalDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / ascendDuration);
                hangingPanel.anchoredPosition =
                    Vector2.Lerp(
                        visiblePosition, hiddenPosition, Mathf.SmoothStep(0f, 1f, progress));
                SetBackgroundDimAlpha(
                    backgroundDimAlpha * (1f - Mathf.SmoothStep(
                        0f, 1f, Mathf.Clamp01(elapsed / fadeOutDuration))));
                yield return null;
            }

            FinishClosing();
        }

        private void FinishClosing()
        {
            hangingPanel.anchoredPosition = visiblePosition;
            hangingPanel.localRotation = Quaternion.identity;
            SetBackgroundDimAlpha(0f);
            menuRoot.SetActive(false);
            Time.timeScale = previousTimeScale;
            isOpen = false;
            isAnimating = false;
            animationRoutine = null;
        }

        private void Restart()
        {
            RestoreTimeScale();
            controller?.RestartGame();
        }

        private void ReturnToTitle()
        {
            RestoreTimeScale();
            controller?.ReturnToTitle();
        }

        private void OpenSettings()
        {
            // 설정 화면의 세부 내용이 정해지면 이 버튼에서 전환합니다.
        }

        private void RestoreTimeScale()
        {
            if (animationRoutine != null)
                StopCoroutine(animationRoutine);
            Time.timeScale = previousTimeScale;
            isOpen = false;
            isAnimating = false;
        }

        private void StartAnimation(IEnumerator routine)
        {
            if (animationRoutine != null)
                StopCoroutine(animationRoutine);
            animationRoutine = StartCoroutine(routine);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (continueButton != null) continueButton.interactable = interactable;
            if (restartButton != null) restartButton.interactable = interactable;
            if (settingsButton != null) settingsButton.interactable = interactable;
            if (titleButton != null) titleButton.interactable = interactable;
        }

        private void SetBackgroundDimAlpha(float alpha)
        {
            if (backgroundDimmer == null)
                return;
            var color = backgroundDimmer.color;
            color.a = alpha;
            backgroundDimmer.color = color;
        }

        private void ApplySwing(float progress, float strength)
        {
            var angle = Mathf.Sin(progress * Mathf.PI * 2f * swingCycles) *
                        swingAngle * Mathf.Clamp01(strength);
            hangingPanel.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static float EaseOutBack(float progress)
        {
            const float overshoot = 1.70158f;
            var shifted = progress - 1f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted +
                   overshoot * shifted * shifted;
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
