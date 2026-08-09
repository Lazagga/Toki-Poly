using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.Audio
{
    public sealed class AudioSettingsView : MonoBehaviour
    {
        [SerializeField] private GameObject settingsRoot;
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider uiSlider;
        [SerializeField] private Text masterValueText;
        [SerializeField] private Text bgmValueText;
        [SerializeField] private Text sfxValueText;
        [SerializeField] private Text uiValueText;
        [SerializeField] private Button backButton;

        public bool IsOpen => settingsRoot != null && settingsRoot.activeSelf;

        private void Awake()
        {
            Bind(masterSlider, SetMasterVolume);
            Bind(bgmSlider, SetBgmVolume);
            Bind(sfxSlider, SetSfxVolume);
            Bind(uiSlider, SetUiVolume);
            if (backButton != null) backButton.onClick.AddListener(Hide);
        }

        private void OnDestroy()
        {
            if (backButton != null) backButton.onClick.RemoveListener(Hide);
        }

        public void Show()
        {
            if (settingsRoot == null) return;
            settingsRoot.SetActive(true);
            RefreshFromManager();
        }

        public void Hide() { if (settingsRoot != null) settingsRoot.SetActive(false); }

        private void RefreshFromManager()
        {
            var manager = AudioManager.Instance;
            if (manager == null) return;
            SetWithoutNotify(masterSlider, manager.MasterVolume);
            SetWithoutNotify(bgmSlider, manager.BgmVolume);
            SetWithoutNotify(sfxSlider, manager.SfxVolume);
            SetWithoutNotify(uiSlider, manager.UiVolume);
            RefreshLabels();
        }

        private void SetMasterVolume(float value) { AudioManager.Instance?.SetMasterVolume(value); SetPercent(masterValueText, value); }
        private void SetBgmVolume(float value) { AudioManager.Instance?.SetBgmVolume(value); SetPercent(bgmValueText, value); }
        private void SetSfxVolume(float value) { AudioManager.Instance?.SetSfxVolume(value); SetPercent(sfxValueText, value); }
        private void SetUiVolume(float value) { AudioManager.Instance?.SetUiVolume(value); SetPercent(uiValueText, value); }

        private void RefreshLabels()
        {
            if (masterSlider != null) SetPercent(masterValueText, masterSlider.value);
            if (bgmSlider != null) SetPercent(bgmValueText, bgmSlider.value);
            if (sfxSlider != null) SetPercent(sfxValueText, sfxSlider.value);
            if (uiSlider != null) SetPercent(uiValueText, uiSlider.value);
        }

        private static void Bind(Slider slider, UnityEngine.Events.UnityAction<float> action)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(action);
        }

        private static void SetWithoutNotify(Slider slider, float value)
        {
            if (slider != null) slider.SetValueWithoutNotify(value);
        }

        private static void SetPercent(Text label, float value)
        {
            if (label != null) label.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }
}
