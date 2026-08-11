using System.Collections;
using GaeBullBing.Presentation.Game;
using GaeBullBing.Presentation.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class TutorialView : MonoBehaviour
    {
        [SerializeField] private Button questionButton;
        [SerializeField] private GameObject tutorialRoot;
        [SerializeField] private CanvasGroup tutorialCanvasGroup;
        [SerializeField] private Button closeButton;
        [SerializeField, Min(.01f)] private float fadeInDuration = .2f;
        [SerializeField, Min(.01f)] private float fadeOutDuration = .16f;

        private GameController controller;
        private PauseMenuView pauseMenu;
        private Coroutine fadeRoutine;
        private float previousTimeScale = 1f;
        private bool isOpen;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            controller = FindFirstObjectByType<GameController>(FindObjectsInactive.Include);
            pauseMenu = FindFirstObjectByType<PauseMenuView>(FindObjectsInactive.Include);

            questionButton?.onClick.RemoveAllListeners();
            questionButton?.onClick.AddListener(Open);
            closeButton?.onClick.RemoveAllListeners();
            closeButton?.onClick.AddListener(Close);

            if (tutorialRoot != null)
                tutorialRoot.SetActive(false);
        }

        private void Update()
        {
            if (questionButton == null)
                return;
            var shouldShow = controller != null && controller.HasGameplayStarted &&
                             controller.State != null && !controller.State.IsFinished;
            if (questionButton.gameObject.activeSelf != shouldShow)
                questionButton.gameObject.SetActive(shouldShow);
        }

        private void Open()
        {
            if (isOpen || tutorialRoot == null || tutorialCanvasGroup == null ||
                pauseMenu != null && pauseMenu.IsOpen)
                return;

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            isOpen = true;
            tutorialRoot.SetActive(true);
            tutorialCanvasGroup.alpha = 0f;
            tutorialCanvasGroup.interactable = false;
            tutorialCanvasGroup.blocksRaycasts = true;
            StartFade(FadeIn());
        }

        private void Close()
        {
            if (!isOpen)
                return;
            tutorialCanvasGroup.interactable = false;
            StartFade(FadeOut());
        }

        private IEnumerator FadeIn()
        {
            for (var elapsed = 0f; elapsed < fadeInDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                tutorialCanvasGroup.alpha = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / fadeInDuration));
                yield return null;
            }
            tutorialCanvasGroup.alpha = 1f;
            tutorialCanvasGroup.interactable = true;
            fadeRoutine = null;
        }

        private IEnumerator FadeOut()
        {
            var startAlpha = tutorialCanvasGroup.alpha;
            for (var elapsed = 0f; elapsed < fadeOutDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                tutorialCanvasGroup.alpha = Mathf.Lerp(
                    startAlpha, 0f,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeOutDuration)));
                yield return null;
            }

            tutorialCanvasGroup.alpha = 0f;
            tutorialCanvasGroup.blocksRaycasts = false;
            tutorialRoot.SetActive(false);
            Time.timeScale = previousTimeScale;
            isOpen = false;
            fadeRoutine = null;
        }

        private void StartFade(IEnumerator routine)
        {
            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(routine);
        }
    }
}
