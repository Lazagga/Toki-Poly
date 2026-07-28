using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class TileInfoPanelView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text towerText;
        [SerializeField] private Text monsterText;
        [SerializeField] private ScrollRect informationScroll;
        [SerializeField, Range(0f, 1f)] private float pointerHoverAlpha = .55f;

        private CanvasGroup panelCanvasGroup;

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
            }
        }

        public void Hide()
        {
            SetPanelAlpha(1f);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (IsVisible)
                SetPanelAlpha(pointerHoverAlpha);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetPanelAlpha(1f);
        }

        private void SetPanelAlpha(float alpha)
        {
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = alpha;
        }
    }
}
