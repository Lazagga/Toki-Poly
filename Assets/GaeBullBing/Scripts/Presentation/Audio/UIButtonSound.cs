using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.Audio
{
    [RequireComponent(typeof(Button))]
    public sealed class UIButtonSound : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(PlayClickSound);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(PlayClickSound);
        }

        private void PlayClickSound()
        {
            var manager = AudioManager.Instance;
            if (manager != null)
                manager.PlayUi(manager.Ui.ButtonClick, volume);
        }
    }
}
