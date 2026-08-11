using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.Audio
{
    [RequireComponent(typeof(Button))]
    public sealed class UIButtonSound : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private bool playGenericClick = true;
        private Button button;

        private void Awake() => button = GetComponent<Button>();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left &&
                button != null && button.IsActive() && button.interactable)
                Play();
        }

        public void Play()
        {
            if (!playGenericClick) return;
            var manager = AudioManager.Instance;
            if (manager != null)
                manager.PlayUi(manager.Ui.ButtonClick, volume);
        }

        public void SetGenericClickEnabled(bool enabled) => playGenericClick = enabled;

        public static void SetGenericClickEnabled(Button target, bool enabled)
        {
            if (target == null) return;
            var sound = target.GetComponent<UIButtonSound>();
            if (sound == null) sound = target.gameObject.AddComponent<UIButtonSound>();
            sound.SetGenericClickEnabled(enabled);
        }

        public static void PlayFor(Button target)
        {
            if (target == null || !target.IsActive() || !target.interactable) return;
            target.GetComponent<UIButtonSound>()?.Play();
        }
    }
}
