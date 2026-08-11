using System;
using GaeBullBing.Core;
using UnityEngine;
using UnityEngine.UI;
using GaeBullBing.Presentation.Audio;
using UnityEngine.InputSystem;

namespace GaeBullBing.Presentation.UI
{
    public sealed class CornerActionMenu : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private GameObject elementRoot;
        [SerializeField] private Text title;
        [SerializeField] private Button fireButton;
        [SerializeField] private Button iceButton;
        [SerializeField] private Button physicsButton;
        [SerializeField] private Button electricButton;
        [SerializeField] private DeveloperConsoleView developerConsole;

        private void Awake()
        {
            if (developerConsole == null)
                developerConsole = FindFirstObjectByType<DeveloperConsoleView>(FindObjectsInactive.Include);
        }

private void Update()
        {
            if (root == null || !root.activeInHierarchy ||
                developerConsole != null && !developerConsole.GameplayInputEnabled ||
                developerConsole != null && developerConsole.IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) Invoke(fireButton);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) Invoke(iceButton);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) Invoke(physicsButton);
            else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) Invoke(electricButton);
        }

        private static void Invoke(Button button)
        {
            if (button != null && button.gameObject.activeInHierarchy && button.interactable)
            {
                UIButtonSound.PlayFor(button);
                button.onClick.Invoke();
            }
        }

        public void ShowElementSelection(Action<TowerElement> selected)
        {
            root.SetActive(true);
            elementRoot.SetActive(true);
            title.text = "강화할 속성 선택";
            title.color = Color.white;
            Bind(fireButton, TowerElement.Fire, selected); Bind(iceButton, TowerElement.Ice, selected);
            Bind(physicsButton, TowerElement.Physics, selected); Bind(electricButton, TowerElement.Electric, selected);
            SetLabel(fireButton, "불"); SetLabel(iceButton, "얼음");
            SetLabel(physicsButton, "물리"); SetLabel(electricButton, "전기");
            SetElementButtons(true);
        }



        public void Hide()
        {
            if (root == null || !root.activeSelf) return;
            root.SetActive(false);
        }
        private void Bind(Button button, TowerElement element, Action<TowerElement> selected)
        { button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => selected(element)); }
        private void SetElementButtons(bool active)
        { fireButton.gameObject.SetActive(active); iceButton.gameObject.SetActive(active); physicsButton.gameObject.SetActive(active); electricButton.gameObject.SetActive(active); }
        private static void SetLabel(Button button, string value)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label == null) return;
            label.text = value;
            label.fontSize = 28;
        }

    }
}
