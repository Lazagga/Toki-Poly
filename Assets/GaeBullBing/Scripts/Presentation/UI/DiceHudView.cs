using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using GaeBullBing.Presentation.Audio;

namespace GaeBullBing.Presentation.UI
{
    public sealed class DiceHudView : MonoBehaviour
    {
        [SerializeField] private Button rollButton;
        [SerializeField] private DeveloperConsoleView developerConsole;
        [SerializeField] private Text remainingKillsText;
        [SerializeField] private DiceFaceListView diceFaceListView;
        [SerializeField] private Image[] playerHealthHearts;

        private GameController controller;
        private DiceSystemView diceSystemView;


        public void Bind(GameController gameController)
        {
            controller = gameController;
            diceSystemView = GetComponent<DiceSystemView>();
            if (diceSystemView == null)
                throw new MissingReferenceException("Dice Controller에 DiceSystemView가 연결되어 있지 않습니다.");
            diceSystemView.Initialize(gameController, this);

            if (diceFaceListView == null)
                diceFaceListView = FindFirstObjectByType<DiceFaceListView>(FindObjectsInactive.Include);
            diceFaceListView?.Bind(gameController.State.Dice);
            rollButton.onClick.RemoveListener(OnRollClicked);
            rollButton.onClick.AddListener(OnRollClicked);
            BeginPlayerTurn();
            RefreshDifficulty();
            RefreshPlayerHealth();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || controller == null || !controller.AcceptsGameplayInput ||
                developerConsole != null && developerConsole.IsOpen)
                return;
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) &&
                rollButton.gameObject.activeInHierarchy && rollButton.interactable)
            {
                UIButtonSound.PlayFor(rollButton);
                OnRollClicked();
            }
        }

        public void SetRolling(bool rolling)
        {
            if (rolling) controller?.ClearTileSelectionHighlights();
            if (rolling) diceSystemView?.SetVisible(false);
            rollButton.gameObject.SetActive(!rolling);
            rollButton.interactable = !rolling && controller != null && controller.Session.CanRollDice;
        }

        public void SetDiceSelectionOpen(bool open)
        {
            rollButton.gameObject.SetActive(true);
            rollButton.interactable = !open && controller != null && controller.Session.CanRollDice;
        }

        public void SetBusy()
        {
            controller?.ClearTileSelectionHighlights();
            diceSystemView?.SetVisible(false);

            rollButton.gameObject.SetActive(false);
            rollButton.interactable = false;
        }

        public void BeginPlayerTurn()
        {
            diceSystemView?.SetVisible(true);
            diceSystemView?.Refresh();

            rollButton.gameObject.SetActive(true);
            rollButton.interactable = controller != null && controller.Session.CanRollDice;
            controller?.RefreshDiceDestinationHighlights();
        }

        public void RefreshRollAvailability()
        {
            if (rollButton != null)
                rollButton.interactable = controller != null && controller.Session.CanRollDice;
        }

        public void ShowGameOver(int escapedCount, int escapeLimit)
        {
            rollButton.gameObject.SetActive(false);
            rollButton.interactable = false;
            if (remainingKillsText != null) remainingKillsText.text = "게임 오버";
        }

        public void ShowGameClear()
        {
            rollButton.gameObject.SetActive(false);
            rollButton.interactable = false;
            if (remainingKillsText != null) remainingKillsText.text = "게임 클리어!";
        }

        public void SetResults(int first, int second) { }

        public void RefreshDifficulty()
        {
            if (remainingKillsText == null || controller == null) return;
            remainingKillsText.text = $"총 포획 {controller.TotalKills}마리";
        }

        public void RefreshDiceFaces()
        {
            diceFaceListView?.Refresh();
            diceSystemView?.Refresh();
        }

        public void RefreshPlayerHealth()
        {
            if (controller == null || playerHealthHearts == null) return;
            var remainingHealth = Mathf.Clamp(
                controller.State.EscapeLimit - controller.State.EscapedMonsterCount,
                0,
                playerHealthHearts.Length);
            for (var index = 0; index < playerHealthHearts.Length; index++)
                if (playerHealthHearts[index] != null)
                    playerHealthHearts[index].gameObject.SetActive(index < remainingHealth);
        }

        private void OnRollClicked()
        {
            if (controller == null || !controller.AcceptsGameplayInput || !controller.Session.CanRollDice)
                return;

            SetRolling(true);
            controller.RollDiceAndMovePlayer();
        }
    }
}
