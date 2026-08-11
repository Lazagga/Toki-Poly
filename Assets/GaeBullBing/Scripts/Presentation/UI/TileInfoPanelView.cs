using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using GaeBullBing.Presentation.Audio;

namespace GaeBullBing.Presentation.UI
{
    public sealed class TileInfoPanelView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text towerText;
        [SerializeField] private Text monsterText;
        [SerializeField] private ScrollRect informationScroll;
        [SerializeField, Range(0f, 1f)] private float pointerHoverAlpha = .55f;
        [SerializeField, Min(0f)] private float pointerHoverFadeDuration = .1f;

        private CanvasGroup panelCanvasGroup;
        private RectTransform panelRect;
        private Canvas panelCanvas;
        private float targetPanelAlpha = 1f;

        public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

        public void Show(string title, string towerDescription, string monsterDescription)
        {
            if (titleText != null) titleText.text = title;
            if (towerText != null) towerText.text = towerDescription;
            if (monsterText != null)
            {
                monsterText.text = monsterDescription;
            }
            if (panelRoot != null) panelRoot.SetActive(true);
            targetPanelAlpha = 1f;
            SetPanelAlpha(1f);
            Canvas.ForceUpdateCanvases();
            if (informationScroll != null)
                informationScroll.verticalNormalizedPosition = 1f;
        }

        private void Awake()
        {
            if (informationScroll == null)
                informationScroll = panelRoot != null
                    ? panelRoot.transform.Find("Information Scroll View")?.GetComponent<ScrollRect>()
                    : null;
            if (panelRoot != null)
            {
                panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
                if (panelCanvasGroup == null)
                    panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();
                panelRect = panelRoot.GetComponent<RectTransform>();
                panelCanvas = panelRoot.GetComponentInParent<Canvas>();
            }
        }

        private void Update()
        {
            if (panelCanvasGroup == null)
                return;

            var pointerInside = false;
            if (IsVisible && panelRect != null && Mouse.current != null)
            {
                UnityEngine.Camera eventCamera = null;
                if (panelCanvas != null && panelCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    eventCamera = panelCanvas.worldCamera;
                pointerInside = RectTransformUtility.RectangleContainsScreenPoint(
                    panelRect,
                    Mouse.current.position.ReadValue(),
                    eventCamera);
            }

            targetPanelAlpha = pointerInside ? pointerHoverAlpha : 1f;
            if (pointerHoverFadeDuration <= 0f)
            {
                SetPanelAlpha(targetPanelAlpha);
                return;
            }

            panelCanvasGroup.alpha = Mathf.MoveTowards(
                panelCanvasGroup.alpha,
                targetPanelAlpha,
                Time.unscaledDeltaTime / pointerHoverFadeDuration);
        }

        public void Hide()
        {
            targetPanelAlpha = 1f;
            SetPanelAlpha(1f);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void SetPanelAlpha(float alpha)
        {
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = alpha;
        }
    }
}
