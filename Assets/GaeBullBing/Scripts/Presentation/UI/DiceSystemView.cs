using System;
using System.Collections.Generic;
using GaeBullBing.Core.Dice;
using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class DiceSystemView : MonoBehaviour
    {
        [Header("Scene UI")]
        [SerializeField] private RectTransform loadoutRoot;
        [SerializeField] private Button[] slotButtons;
        [SerializeField] private DiceCardDisplayView[] slotDisplays;
        [SerializeField] private RectTransform dropdown;
        [SerializeField] private Button[] inventoryButtons;
        [SerializeField] private DiceCardDisplayView[] inventoryDisplays;
        [SerializeField] private Text emptyInventoryText;
        [SerializeField] private GameObject rewardOverlay;
        [SerializeField] private Text rewardText;
        [SerializeField] private DiceCardDisplayView rewardDisplay;
        [SerializeField] private Button acquireButton;
        [SerializeField] private Button towerBoostButton;
        [SerializeField] private Button[] replacementButtons;

        private GameController controller;
        private DiceHudView hud;
        private DeveloperConsoleView developerConsole;
        private DiceState pendingReward;
        private Action pendingRewardCompleted;
        private bool replacementOpen;
        private int selectedSlot = -1;

        public bool HasOpenModal =>
            rewardOverlay != null && rewardOverlay.activeInHierarchy ||
            dropdown != null && dropdown.gameObject.activeInHierarchy;

        public void Initialize(GameController gameController, DiceHudView diceHud)
        {
            controller = gameController;
            hud = diceHud;
            developerConsole = FindFirstObjectByType<DeveloperConsoleView>(FindObjectsInactive.Include);
            ValidateSceneReferences();
            ConfigureSlotButtons();
            Refresh();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null ||
                developerConsole != null && (!developerConsole.GameplayInputEnabled || developerConsole.IsOpen))
                return;

            if (rewardOverlay.activeInHierarchy)
            {
                HandleRewardKeyboard(keyboard);
                return;
            }
            if (!loadoutRoot.gameObject.activeInHierarchy) return;

            if (dropdown.gameObject.activeInHierarchy)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) CloseDropdown();
                else if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) InvokeButton(inventoryButtons, 0);
                else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) InvokeButton(inventoryButtons, 1);
                return;
            }

            if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) InvokeButton(slotButtons, 0);
            else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) InvokeButton(slotButtons, 1);
        }

        public void SetVisible(bool visible)
        {
            loadoutRoot.gameObject.SetActive(visible);
            if (visible) return;
            dropdown.gameObject.SetActive(false);
            SetSelectedSlot(-1);
            ClearEventSelection();
        }

        public void Refresh()
        {
            if (controller == null) return;
            for (var slot = 0; slot < slotButtons.Length; slot++)
            {
                var dice = controller.State.Dice[slot];
                slotButtons[slot].image.color = Color.white;
                slotDisplays[slot].Bind(dice);
            }
            if (dropdown.gameObject.activeSelf) ShowDropdown(selectedSlot);
        }

        public void ShowLapReward(DiceState reward, Action completed)
        {
            pendingReward = reward;
            pendingRewardCompleted = completed;
            replacementOpen = false;
            rewardOverlay.SetActive(true);
            rewardText.gameObject.SetActive(false);
            rewardDisplay.gameObject.SetActive(true);
            rewardDisplay.Bind(reward);
            SetRewardMainVisible(true);

            acquireButton.onClick.RemoveAllListeners();
            towerBoostButton.onClick.RemoveAllListeners();
            acquireButton.onClick.AddListener(() =>
            {
                if (controller.Session.StoreDiceReward(pendingReward)) CloseReward(completed);
                else ShowReplacement(completed);
            });
            towerBoostButton.onClick.AddListener(() =>
            {
                CloseReward(null);
                controller.ApplyDiceRewardTowerBoost(completed);
            });
        }

        private void ShowReplacement(Action completed)
        {
            replacementOpen = true;
            rewardDisplay.gameObject.SetActive(false);
            rewardText.gameObject.SetActive(true);
            rewardText.text = $"인벤토리가 가득 찼습니다.\n교체할 주사위를 선택하세요.\n\n{pendingReward.DisplayName}\n{FormatFaces(pendingReward)}";
            SetRewardMainVisible(false);
            var inventory = controller.State.DiceInventory.Dice;
            for (var index = 0; index < replacementButtons.Length; index++)
            {
                var button = replacementButtons[index];
                var active = index < inventory.Count;
                button.gameObject.SetActive(active);
                button.onClick.RemoveAllListeners();
                if (!active) continue;
                var inventoryIndex = index;
                var dice = inventory[index];
                var label = button.GetComponentInChildren<Text>(true);
                label.text = $"{dice.DisplayName}\n{FormatFaces(dice)}";
                label.color = dice.UsesBlackPips ? Color.black : Color.white;
                button.image.color = new Color(dice.Red, dice.Green, dice.Blue, 1f);
                button.onClick.AddListener(() =>
                {
                    controller.Session.ReplaceReserveDice(inventoryIndex, pendingReward);
                    CloseReward(completed);
                });
            }
        }

        private void ShowDropdown(int slot)
        {
            if (slot < 0 || slot >= slotButtons.Length) return;
            selectedSlot = slot;
            SetSelectedSlot(slot);
            dropdown.gameObject.SetActive(true);
            hud.SetDiceSelectionOpen(true);
            var inventory = controller.State.DiceInventory.Dice;
            var candidateIndex = 0;
            for (var inventoryIndex = 0; inventoryIndex < inventory.Count; inventoryIndex++)
            {
                var dice = inventory[inventoryIndex];
                if (IsEquipped(dice)) continue;
                if (candidateIndex >= inventoryButtons.Length) break;
                ConfigureInventoryButton(
                    inventoryButtons[candidateIndex],
                    inventoryDisplays[candidateIndex],
                    inventoryIndex,
                    dice);
                candidateIndex++;
            }
            for (var index = candidateIndex; index < inventoryButtons.Length; index++)
                inventoryButtons[index].gameObject.SetActive(false);
            emptyInventoryText.gameObject.SetActive(candidateIndex == 0);
        }

        private void ConfigureInventoryButton(
            Button button,
            DiceCardDisplayView display,
            int inventoryIndex,
            DiceState dice)
        {
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();
            display.Bind(dice);
            button.image.color = new Color(dice.Red, dice.Green, dice.Blue, 1f);
            button.onClick.AddListener(() =>
            {
                controller.Session.QueueDiceEquip(selectedSlot, inventoryIndex);
                CloseDropdown();
                hud.RefreshRollAvailability();
                hud.RefreshDiceFaces();
            });
        }

        private bool IsEquipped(DiceState dice)
        {
            foreach (var equipped in controller.State.Dice)
                if (ReferenceEquals(equipped, dice)) return true;
            return false;
        }

        private void ConfigureSlotButtons()
        {
            for (var slot = 0; slot < slotButtons.Length; slot++)
            {
                var captured = slot;
                var navigation = slotButtons[slot].navigation;
                navigation.mode = Navigation.Mode.None;
                slotButtons[slot].navigation = navigation;
                slotButtons[slot].onClick.RemoveAllListeners();
                slotButtons[slot].onClick.AddListener(() => ToggleDropdown(captured));
            }
        }

        private void ToggleDropdown(int slot)
        {
            if (dropdown.gameObject.activeSelf && selectedSlot == slot) CloseDropdown();
            else ShowDropdown(slot);
        }

        private void CloseDropdown()
        {
            dropdown.gameObject.SetActive(false);
            SetSelectedSlot(-1);
            hud.SetDiceSelectionOpen(false);
            ClearEventSelection();
        }

        private void CloseReward(Action completed)
        {
            rewardOverlay.SetActive(false);
            pendingReward = null;
            pendingRewardCompleted = null;
            replacementOpen = false;
            hud.RefreshDiceFaces();
            completed?.Invoke();
        }

        private void HandleRewardKeyboard(Keyboard keyboard)
        {
            if (replacementOpen)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    ShowLapReward(pendingReward, pendingRewardCompleted);
                    return;
                }
                if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) InvokeButton(replacementButtons, 0);
                else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) InvokeButton(replacementButtons, 1);
                else if (Pressed(keyboard.digit3Key, keyboard.numpad3Key)) InvokeButton(replacementButtons, 2);
                else if (Pressed(keyboard.digit4Key, keyboard.numpad4Key)) InvokeButton(replacementButtons, 3);
                return;
            }
            if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) InvokeButton(acquireButton);
            else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) InvokeButton(towerBoostButton);
        }

        private void SetRewardMainVisible(bool visible)
        {
            acquireButton.gameObject.SetActive(visible);
            towerBoostButton.gameObject.SetActive(visible);
            foreach (var button in replacementButtons) button.gameObject.SetActive(false);
        }

        private void SetSelectedSlot(int slot)
        {
            selectedSlot = slot;
            for (var index = 0; index < slotButtons.Length; index++)
            {
                var outline = slotButtons[index].GetComponent<Outline>();
                if (outline != null) outline.enabled = index == slot;
            }
        }

        private void ValidateSceneReferences()
        {
            if (loadoutRoot == null || dropdown == null || rewardOverlay == null || rewardText == null ||
                acquireButton == null || towerBoostButton == null || emptyInventoryText == null ||
                slotButtons == null || slotButtons.Length != 2 ||
                slotDisplays == null || slotDisplays.Length != slotButtons.Length ||
                inventoryButtons == null || inventoryButtons.Length < 2 ||
                inventoryDisplays == null || inventoryDisplays.Length != inventoryButtons.Length ||
                rewardDisplay == null ||
                replacementButtons == null || replacementButtons.Length != 4)
                throw new MissingReferenceException("DiceSystemView의 Scene UI 참조가 완전하지 않습니다.");
        }

        private static bool Pressed(KeyControl main, KeyControl numpad) =>
            main.wasPressedThisFrame || numpad.wasPressedThisFrame;

        private static void InvokeButton(IReadOnlyList<Button> buttons, int index)
        {
            if (buttons == null || index < 0 || index >= buttons.Count) return;
            InvokeButton(buttons[index]);
        }

        private static void InvokeButton(Button button)
        {
            if (button != null && button.gameObject.activeInHierarchy && button.interactable)
                button.onClick.Invoke();
        }

        private static void ClearEventSelection()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        private static string FormatFaces(DiceState dice)
        {
            var values = new List<int>(6);
            for (var face = 0; face < dice.Faces.Length; face++)
                for (var count = 0; count < dice.Weights[face] && values.Count < 6; count++)
                    values.Add(dice.Faces[face]);
            return string.Join(" ", values);
        }

    }
}
