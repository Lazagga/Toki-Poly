using System;
using System.Collections.Generic;
using GaeBullBing.Core;
using GaeBullBing.Core.Data;
using UnityEngine;
using UnityEngine.UI;
using GaeBullBing.Presentation.Audio;
using UnityEngine.InputSystem;

namespace GaeBullBing.Presentation.UI
{
    public sealed class RadialActionMenu : MonoBehaviour
    {
        [SerializeField] private RectTransform menuRoot;
        [SerializeField] private Button primaryButton;
        [SerializeField] private Text primaryText;
        [SerializeField] private Sprite buildButtonSprite;
        [SerializeField] private Sprite enhanceButtonSprite;
        [SerializeField] private Sprite fireUpgradeSprite;
        [SerializeField] private Sprite iceUpgradeSprite;
        [SerializeField] private Sprite physicsUpgradeSprite;
        [SerializeField] private Sprite electricUpgradeSprite;
        [SerializeField] private RectTransform upgradeChoicesRoot;
        [SerializeField] private Button[] upgradeChoiceButtons = Array.Empty<Button>();
        [SerializeField] private Vector2 screenOffset = new(0f, 145f);
        [SerializeField] private DeveloperConsoleView developerConsole;

        private RectTransform canvasRect;
        private Transform worldTarget;
        private UnityEngine.Camera worldCamera;

private void Awake()
        {
            canvasRect = GetComponentInParent<Canvas>().transform as RectTransform;
            Hide();
        }

        private void LateUpdate()
        {
            if (worldTarget == null || !menuRoot.gameObject.activeSelf)
                return;

            var screenPoint = worldCamera.WorldToScreenPoint(worldTarget.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out var localPoint);
            var menuPosition = localPoint + screenOffset;
            menuRoot.anchoredPosition = menuPosition;
        }

private void Update()
        {
            if (!menuRoot.gameObject.activeInHierarchy ||
                developerConsole != null && !developerConsole.GameplayInputEnabled ||
                developerConsole != null && developerConsole.IsOpen)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (primaryButton.gameObject.activeInHierarchy && primaryButton.interactable &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                UIButtonSound.PlayFor(primaryButton);
                primaryButton.onClick.Invoke();
                return;
            }

            if (upgradeChoicesRoot == null || !upgradeChoicesRoot.gameObject.activeInHierarchy)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                InvokeChoice(0);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                InvokeChoice(1);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                InvokeChoice(2);
        }

private void InvokeChoice(int index)
        {
            if (index < 0 || index >= upgradeChoiceButtons.Length)
                return;
            var button = upgradeChoiceButtons[index];
            if (button != null && button.gameObject.activeInHierarchy && button.interactable)
            {
                UIButtonSound.PlayFor(button);
                button.onClick.Invoke();
            }
        }

        public void ShowPrimary(
            Transform target,
            UnityEngine.Camera camera,
            bool hasTower,
            Action onPrimarySelected)
        {
            var wasVisible = menuRoot.gameObject.activeSelf;
            ClearChoices();
            worldTarget = target;
            worldCamera = camera;
            menuRoot.gameObject.SetActive(true);
            if (!wasVisible) AudioManager.Instance?.PlayPanelOpen();
            primaryButton.gameObject.SetActive(true);
            var actionSprite = hasTower ? enhanceButtonSprite : buildButtonSprite;
            var image = primaryButton.image;
            image.sprite = actionSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            primaryText.gameObject.SetActive(actionSprite == null);
            primaryText.text = hasTower ? "ENFORCE" : "BUILD";
            primaryButton.onClick.RemoveAllListeners();
            primaryButton.onClick.AddListener(() => onPrimarySelected());
        }



        public void ShowUpgradeChoices(IReadOnlyList<TowerUpgradeDefinition> definitions, Action<TowerUpgradeDefinition> onSelected)
        {
            ClearChoices();
            primaryButton.gameObject.SetActive(false);
            upgradeChoicesRoot.gameObject.SetActive(true);
            var visibleCount = Mathf.Min(definitions.Count, upgradeChoiceButtons.Length);
            for (var index = 0; index < visibleCount; index++)
            {
                var definition = definitions[index]; var button = upgradeChoiceButtons[index];
                button.gameObject.SetActive(true);
                ApplyUpgradeStyle(button, definition.Element);
                var label = button.GetComponentInChildren<Text>();
                label.text = definition.Description;
                label.alignment = TextAnchor.MiddleCenter;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 18;
                button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => onSelected(definition));
            }
        }





        private void ApplyUpgradeStyle(Button button, GaeBullBing.Core.TowerElement element)
        {
            var sprite = element switch
            {
                GaeBullBing.Core.TowerElement.Fire => fireUpgradeSprite,
                GaeBullBing.Core.TowerElement.Ice => iceUpgradeSprite,
                GaeBullBing.Core.TowerElement.Physics => physicsUpgradeSprite,
                GaeBullBing.Core.TowerElement.Electric => electricUpgradeSprite,
                _ => null
            };
            if (sprite == null) return;

            var image = button.image;
            image.sprite = sprite;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = new Color(.12f, .08f, .05f, 1f);
        }

        public void Hide()
        {
            var wasVisible = menuRoot.gameObject.activeSelf;
            ClearChoices();
            worldTarget = null;
            menuRoot.gameObject.SetActive(false);
            if (wasVisible) AudioManager.Instance?.PlayPanelClose();
        }

private void ClearChoices()
        {
            if (upgradeChoicesRoot != null)
                upgradeChoicesRoot.gameObject.SetActive(false);
            foreach (var button in upgradeChoiceButtons)
            {
                if (button == null)
                    continue;
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(false);
            }
        }
    }
}
