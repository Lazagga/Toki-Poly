using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class GameSpeedToggleView : MonoBehaviour
    {
        [SerializeField] private Toggle speedToggle;
        [SerializeField] private Text speedLabel;
        [SerializeField, Min(0.1f)] private float normalSpeed = 1f;
        [SerializeField, Min(0.1f)] private float fastSpeed = 2f;

        private float displayedSpeed = -1f;

        private void Awake()
        {
            if (speedToggle == null) speedToggle = GetComponent<Toggle>();
            if (speedLabel == null) speedLabel = GetComponentInChildren<Text>(true);

            speedToggle.onValueChanged.RemoveListener(SetFastMode);
            speedToggle.onValueChanged.AddListener(SetFastMode);
            speedToggle.SetIsOnWithoutNotify(false);
            ApplySpeed(normalSpeed);
        }

        private void LateUpdate()
        {
            // 개발자 콘솔 등 다른 경로에서 배속이 변경돼도 표시를 현재 값과 맞춘다.
            if (!Mathf.Approximately(displayedSpeed, Time.timeScale))
                RefreshLabel(Time.timeScale);
        }

        private void OnDestroy()
        {
            if (speedToggle != null)
                speedToggle.onValueChanged.RemoveListener(SetFastMode);
        }

        private void SetFastMode(bool fast) =>
            ApplySpeed(fast ? fastSpeed : normalSpeed);

        private void ApplySpeed(float speed)
        {
            Time.timeScale = speed;
            RefreshLabel(speed);
        }

        private void RefreshLabel(float speed)
        {
            displayedSpeed = speed;
            if (speedLabel != null)
                speedLabel.text = $"{speed:0.#}×";
        }
    }
}
