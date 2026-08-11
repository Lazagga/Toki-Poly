using System;
using System.Collections;
using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using GaeBullBing.Presentation.Audio;

namespace GaeBullBing.Presentation.UI
{
    public sealed class DeveloperConsoleView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private InputField input;
        [SerializeField] private Text output;
        [SerializeField] private Button submitButton;
        [SerializeField] private GameController gameController;
        private bool submitPending;
        private ScrollRect outputScroll;

        public bool IsOpen => panel != null && panel.activeSelf;
        public bool GameplayInputEnabled => gameController != null && gameController.AcceptsGameplayInput;

        private void Awake()
        {
            panel.SetActive(false); submitButton.onClick.AddListener(Submit);
            var defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            output.font = defaultFont;
            input.textComponent.font = defaultFont;
            output.color = Color.white; output.fontSize = 22;
            input.textComponent.color = Color.white; input.textComponent.fontSize = 21;
            var inputBackground = input.GetComponent<Image>();
            if (inputBackground != null) inputBackground.color = new Color(.08f,.09f,.12f,1f);
            input.customCaretColor = true; input.caretColor = Color.white;
            input.selectionColor = new Color(.2f,.45f,.8f,.65f);
            if (input.placeholder is Text placeholder)
            {
                placeholder.font = defaultFont;
                placeholder.color = new Color(.7f,.7f,.7f,1f);
                placeholder.fontSize = 19;
            }
            ConfigureOutputScroll();
        }

        private void ConfigureOutputScroll()
        {
            if (panel == null || output == null) return;
            var viewport = panel.transform.Find("Console Output Viewport") as RectTransform;
            
            if (viewport == null) return;
            viewport.localScale = Vector3.one;
            output.rectTransform.SetParent(viewport, false);
            output.rectTransform.anchorMin = new Vector2(0f, 1f);
            output.rectTransform.anchorMax = new Vector2(1f, 1f);
            output.rectTransform.pivot = new Vector2(.5f, 1f);
            output.rectTransform.anchoredPosition = Vector2.zero;
            output.rectTransform.sizeDelta = Vector2.zero;
            output.horizontalOverflow = HorizontalWrapMode.Wrap;
            output.verticalOverflow = VerticalWrapMode.Overflow;
            var fitter = output.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = output.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            outputScroll = viewport.GetComponent<ScrollRect>();
            if (outputScroll == null) outputScroll = viewport.gameObject.AddComponent<ScrollRect>();
            outputScroll.viewport = viewport;
            outputScroll.content = output.rectTransform;
            outputScroll.horizontal = false;
            outputScroll.vertical = true;
            outputScroll.movementType = ScrollRect.MovementType.Clamped;
            outputScroll.inertia = true;
            outputScroll.scrollSensitivity = 28f;
        }


        private void Update()
        {
            if (!GameplayInputEnabled)
            {
                if (panel.activeSelf) SetOpen(false);
                return;
            }
            var keyboard = Keyboard.current; if (keyboard == null) return;
            if (keyboard.backquoteKey.wasPressedThisFrame) SetOpen(!panel.activeSelf);
            if (panel.activeSelf && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
            if (panel.activeSelf && input.isFocused &&
                !submitPending && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                StartCoroutine(SubmitAfterImeCommit());
        }

        private IEnumerator SubmitAfterImeCommit()
        {
            submitPending = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            Submit();
            submitPending = false;
        }

        public void SetOpen(bool open)
        {
            if (panel.activeSelf == open) return;
            panel.SetActive(open);
            if (open) AudioManager.Instance?.PlayPanelOpen();
            else AudioManager.Instance?.PlayPanelClose();
            if (open) { input.ActivateInputField(); input.Select(); }
        }

        public void Submit()
        {
            var command = input.text.Trim(); if (command.Length == 0) return;
            Write($"> {command}"); Execute(command); input.text = string.Empty; input.ActivateInputField();
        }

        private void Execute(string command)
        {
            
            var parts = command.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            if (gameController.HasPendingConsoleUpgrade && parts.Length == 1 &&
                int.TryParse(parts[0], out var upgradeChoice))
            {
                gameController.ApplyConsoleUpgradeChoice(upgradeChoice, out var upgradeMessage);
                Write(upgradeMessage);
                return;
            }
            if (parts.Length == 1 && parts[0].Equals("win", StringComparison.OrdinalIgnoreCase))
            {
                gameController.FinishGameFromConsole(true, out var message);
                Write(message);
                return;
            }
            if (parts.Length == 1 && parts[0].Equals("lose", StringComparison.OrdinalIgnoreCase))
            {
                gameController.FinishGameFromConsole(false, out var message);
                Write(message);
                return;
            }
            if (parts.Length == 1 && parts[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                SetOpen(false);
                gameController.ResetGameFromConsole();
                return;
            }
            if (parts.Length == 2 && parts[0].Equals("speed", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(parts[1], out var speed))
            {
                gameController.SetGameSpeedFromConsole(speed, out var message);
                Write(message);
                return;
            }
            if (parts.Length == 3 && parts[0].Equals("dice", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out var first) && int.TryParse(parts[2], out var second))
            { gameController.SetNextDiceResults(first, second, out var message); Write(message); return; }
            if (parts.Length >= 2 && parts[0].Equals("spawn", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length >= 3 && int.TryParse(parts[parts.Length - 1], out var spawnTileIndex))
                {
                    var name = string.Join(" ", parts, 1, parts.Length - 2);
                    gameController.SpawnMonsterFromConsole(name, spawnTileIndex, out var message);
                    Write(message);
                    return;
                }
                var nextName = string.Join(" ", parts, 1, parts.Length - 1);
                gameController.SetNextMonster(nextName, out var nextMessage);
                Write(nextMessage);
                return;
            }
            if (parts.Length == 2 && parts[0].Equals("build", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1], out var tileIndex))
            {
                gameController.BuildTowerFromConsole(tileIndex, out var message);
                Write(message);
                return;
            }
            if (parts.Length == 3 && parts[0].Equals("effect", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1], out var effectTileIndex))
            {
                gameController.SetTileEffectFromConsole(effectTileIndex, parts[2], out var message);
                Write(message);
                return;
            }
            if (parts.Length == 1 && parts[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            { Write("win | lose | reset | speed 1/2/4/8 | dice n m | spawn 몬스터이름 [타일번호] | build 타일번호 | effect 타일번호 frozen/ignite"); return; }
            Write("알 수 없는 명령어입니다. help를 입력하세요.");
        }

        private void Write(string message)
        {
            output.text = string.IsNullOrEmpty(output.text) ? message : output.text + "\n" + message;
            if (isActiveAndEnabled) StartCoroutine(ScrollOutputToBottom());
        }

        private IEnumerator ScrollOutputToBottom()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (outputScroll != null) outputScroll.verticalNormalizedPosition = 0f;
        }
    }
}
