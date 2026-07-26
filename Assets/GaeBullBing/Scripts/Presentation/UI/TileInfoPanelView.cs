using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class TileInfoPanelView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text towerText;
        [SerializeField] private Text monsterText;
        [SerializeField] private ScrollRect informationScroll;

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
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
