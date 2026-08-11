using System;
using System.Collections.Generic;
using GaeBullBing.Core.Dice;
using GaeBullBing.Presentation.Dice;
using UnityEngine;
using UnityEngine.UI;
using GaeBullBing.Presentation.Audio;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GaeBullBing.Presentation.UI
{
    public sealed class DiceTuningView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text title;
        [SerializeField] private Button[] diceButtons;
        [SerializeField] private Button[] faceButtons;
        [SerializeField] private Button decrementButton;
        [SerializeField] private Button incrementButton;
        [SerializeField] private Button backButton;
        [SerializeField] private DiceTuning3DDisplay display3D;
        [SerializeField] private DeveloperConsoleView developerConsole;

        private IReadOnlyList<DiceState> dice;
        private Func<int, int, int, bool> selected;
        private Action allTowerBoostSelected;
        private int selectedDice;
        private Step currentStep;

        private enum Step { Reward, Dice, Face }

        private void Awake()
        {
            if (developerConsole == null)
                developerConsole = FindFirstObjectByType<DeveloperConsoleView>(FindObjectsInactive.Include);
            if (display3D == null) display3D = GetComponent<DiceTuning3DDisplay>();
            if (display3D == null) throw new MissingReferenceException("DiceTuning3DDisplay is not connected.");
            display3D.Initialize((RectTransform)transform);
        }

        private void Update()
        {
            if (root == null || !root.activeInHierarchy ||
                developerConsole != null && (!developerConsole.GameplayInputEnabled || developerConsole.IsOpen)) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (currentStep == Step.Reward)
            {
                if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) Invoke(diceButtons, 0);
                else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) Invoke(diceButtons, 1);
            }
            else if (currentStep == Step.Dice)
            {
                if (Pressed(keyboard.digit1Key, keyboard.numpad1Key))
                {
                    AudioManager.Instance?.PlayButtonClick();
                    SelectDice(0);
                }
                else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key))
                {
                    AudioManager.Instance?.PlayButtonClick();
                    SelectDice(1);
                }
            }
            else if (currentStep == Step.Face)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame) { AudioManager.Instance?.PlayButtonClick(); display3D.RotateUp(); }
                else if (keyboard.downArrowKey.wasPressedThisFrame) { AudioManager.Instance?.PlayButtonClick(); display3D.RotateDown(); }
                else if (keyboard.leftArrowKey.wasPressedThisFrame) { AudioManager.Instance?.PlayButtonClick(); display3D.RotateLeft(); }
                else if (keyboard.rightArrowKey.wasPressedThisFrame) { AudioManager.Instance?.PlayButtonClick(); display3D.RotateRight(); }
                else if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) Invoke(decrementButton);
                else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) Invoke(incrementButton);
            }
        }

        private static bool Pressed(KeyControl main, KeyControl numpad) => main.wasPressedThisFrame || numpad.wasPressedThisFrame;
        private static void Invoke(IReadOnlyList<Button> buttons, int index)
        {
            if (index >= 0 && index < buttons.Count) Invoke(buttons[index]);
        }
        private static void Invoke(Button button)
        {
            if (button != null && button.gameObject.activeInHierarchy && button.interactable)
            {
                UIButtonSound.PlayFor(button);
                button.onClick.Invoke();
            }
        }

        public void Show(IReadOnlyList<DiceState> diceStates, Func<int, int, int, bool> onSelected,
            Action onAllTowerBoostSelected)
        {
            dice = diceStates;
            selected = onSelected;
            allTowerBoostSelected = onAllTowerBoostSelected;
            root.SetActive(true);
            ShowRewardStep();
        }

        public void Hide()
        {
            display3D?.HideDisplay();
            root.SetActive(false);
        }

        private void ShowRewardStep()
        {
            currentStep = Step.Reward;
            title.text = "출발지 통과 보상을 선택하세요";
            display3D.HideDisplay();
            SetButtons(diceButtons, true);
            SetButtons(faceButtons, false);
            SetDeltaButtons(false);
            SetBack(false);

            for (var index = 0; index < diceButtons.Length; index++)
            {
                var button = diceButtons[index];
                button.onClick.RemoveAllListeners();
                if (index == 0)
                {
                    SetButtonText(button, "주사위 편집");
                    button.onClick.AddListener(ShowDiceStep);
                }
                else if (index == 1)
                {
                    SetButtonText(button, "계속 진행");
                    button.onClick.AddListener(() => { allTowerBoostSelected?.Invoke(); Hide(); });
                }
                else button.gameObject.SetActive(false);
            }
        }

        private void ShowDiceStep()
        {
            currentStep = Step.Dice;
            title.text = string.Empty;
            SetButtons(diceButtons, false);
            SetButtons(faceButtons, false);
            SetDeltaButtons(false);
            SetBack(true);
            display3D.ShowSelection(BuildPhysicalFaces(), SelectDice);
        }

        private void SelectDice(int index)
        {
            selectedDice = index;
            currentStep = Step.Face;
            SetButtons(diceButtons, false);
            SetButtons(faceButtons, false);
            SetDeltaButtons(true);
            SetBack(true);
            display3D.ShowFaceSelection(index, RefreshSelectedFaceTitle);
        }

        private void RefreshSelectedFaceTitle()
        {
            title.text = string.Empty;
        }

        private int[][] BuildPhysicalFaces()
        {
            var result = new int[dice.Count][];
            for (var diceIndex = 0; diceIndex < dice.Count; diceIndex++)
            {
                var values = new List<int>(6);
                for (var faceIndex = 0; faceIndex < dice[diceIndex].Faces.Length; faceIndex++)
                    for (var count = 0; count < dice[diceIndex].Weights[faceIndex]; count++)
                        values.Add(dice[diceIndex].Faces[faceIndex]);
                while (values.Count < 6) values.Add(dice[diceIndex].Faces[0]);
                if (values.Count > 6) values.RemoveRange(6, values.Count - 6);
                result[diceIndex] = values.ToArray();
            }
            return result;
        }

        private void BindDelta(Button button, int delta)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (selected != null && selected(selectedDice, display3D.VisibleFaceValue, delta)) Hide();
            });
        }

        private void SetDeltaButtons(bool active)
        {
            decrementButton.gameObject.SetActive(active);
            incrementButton.gameObject.SetActive(active);
            if (!active) return;
            SetButtonText(decrementButton, "-1");
            SetButtonText(incrementButton, "+1");
            BindDelta(decrementButton, -1);
            BindDelta(incrementButton, 1);
        }

        private void SetBack(bool active)
        {
            if (backButton == null) return;
            backButton.gameObject.SetActive(active);
            if (!active) return;
            SetButtonText(backButton, "뒤로가기");
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(() =>
            {
                if (currentStep == Step.Face) ShowDiceStep();
                else ShowRewardStep();
            });
        }

        private static void SetButtons(IEnumerable<Button> buttons, bool active)
        {
            foreach (var button in buttons) button.gameObject.SetActive(active);
        }

        private static void SetButtonText(Button button, string value)
        {
            var text = button.GetComponentInChildren<Text>();
            if (text == null) return;
            text.text = value;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = 22;
        }
    }
}
