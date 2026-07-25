using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Dice;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.Camera;
using GaeBullBing.Presentation.Dice;
using GaeBullBing.Presentation.Monsters;
using GaeBullBing.Presentation.Towers;
using GaeBullBing.Presentation.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text;
using System.Globalization;

namespace GaeBullBing.Presentation.Game
{
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] private BoardTilemapView boardView;
        [SerializeField] private PlayerBoardView playerView;
        [SerializeField] private DiceHudView diceHud;
        [SerializeField] private MonsterPresenter monsterPresenter;
        [SerializeField] private BoardDefinition boardDefinition;
        [SerializeField] private BoardCameraController cameraController;
        [SerializeField] private RadialActionMenu radialMenu;
        [SerializeField] private CornerActionMenu cornerActionMenu;
        [SerializeField] private TowerPresenter towerPresenter;
        [SerializeField] private TileInfoPanelView tileInfoPanel;
        [SerializeField] private GameFlowView gameFlowView;
        [SerializeField, Min(0f)] private float diceRevealDelay = 0.35f;
        [SerializeField, Range(0f, 1f)] private float cornerDamageRateBonus = .2f;

        private bool isBusy;
        private MonsterDatabase monsterDatabase;
        private MonsterDefinition[] monsterDefinitions;
        private TowerDefinition[] towerDefinitions;
        private TowerUpgradeDefinition[] towerUpgradeDefinitions;
        private DifficultyService difficultyService;
        private int killsPerDifficultyLevel = 10;
        private float healthMultiplierPerDifficultyLevel = 1.15f;
        private float defensePerDifficultyLevel;
        private Dice3DPresenter dice3DPresenter;
        private StonePresenter stonePresenter;
        private TowerAttackEffectPresenter attackEffectPresenter;
        private string nextMonsterOverrideId;
        private BoardTileSelectionView tileSelectionView;
        private bool pendingDiceTuning;
        private bool diceTuningComplete;
        private Coroutine tileInfoCameraRoutine;
        private bool tileInfoOpen;
        private int inspectedTileIndex = -1;
        private bool tileInfoReturnsToPlayerFocus;
        private const int MaxTowerElementDamageBonus = 30;
        private const int BossResultWave = 8;
        private static bool startImmediatelyAfterReload;
        private static bool fadeTitleAfterReload;
        
        private int pendingConsoleUpgradeTile = -1;
        private readonly List<TowerUpgradeDefinition> pendingConsoleUpgrades = new();
        public bool HasPendingConsoleUpgrade => pendingConsoleUpgradeTile >= 0 && pendingConsoleUpgrades.Count > 0;
private bool finishRoutineStarted;

        public GameState State { get; private set; }
        public GameSession Session { get; private set; }
        public int TotalKills => State?.Difficulty?.KillCount ?? 0;

        public void BuildResultStatistics(out string left, out string right)
        {
            left = string.Empty;
            right = string.Empty;
            if (State == null) return;

            var towerCount = 0;
            var highestDamage = 0;
            var highestDamageElement = TowerElement.None;
            foreach (var tile in State.Board.Tiles)
            {
                if (!tile.HasTower) continue;
                towerCount++;
                if (tile.Tower.LastResolvedDamage <= highestDamage) continue;
                highestDamage = tile.Tower.LastResolvedDamage;
                var definition = FindTowerDefinition(tile.Tower.DefinitionId);
                highestDamageElement = definition != null ? definition.Element : TowerElement.None;
            }

            var remainingLife = State.BossEscaped
                ? 0
                : Mathf.Max(0, State.EscapeLimit - State.EscapedMonsterCount);
            var reachedWave = State.BossSpawned ? BossResultWave : State.Difficulty.Level;
            var damageColor = highestDamageElement switch
            {
                TowerElement.Fire => "#FF4B4B",
                TowerElement.Ice => "#40BFFF",
                TowerElement.Physics => "#63D471",
                TowerElement.Electric => "#B56CFF",
                _ => "#FFFFFF"
            };

            left =
                $"도달 웨이브: {reachedWave}웨이브\n" +
                $"총 턴 수: {State.Round}턴\n" +
                $"총 포획: {TotalKills}\n" +
                $"남은 라이프: {remainingLife}";
            right =
                $"완주 수: {State.CompletedLaps}바퀴\n" +
                $"건설한 타워: {towerCount}개\n" +
                $"최고 피해량: <color={damageColor}>{highestDamage}</color>";
        }
        public int RemainingKills => difficultyService == null ? 0 : difficultyService.GetRemainingKills(State.Difficulty);
        public bool IsFinalPattern => difficultyService != null && difficultyService.IsFinalPattern(State.Difficulty);
        public bool IsBossLevel => difficultyService != null && difficultyService.IsBossLevel(State.Difficulty);
        public bool AcceptsGameplayInput { get; private set; }

        public bool FinishGameFromConsole(bool victory, out string message)
        {
            if (finishRoutineStarted)
            {
                message = "이미 게임 종료 처리가 진행 중입니다.";
                return false;
            }

            message = victory ? "즉시 승리 처리합니다." : "즉시 패배 처리합니다.";
            if (victory) FinishVictory();
            else FinishDefeat();
            return true;
        }

        public bool SetNextDiceResults(int first, int second, out string message)
        {
            if (first < 1 || first > 6 || second < 1 || second > 6) { message = "주사위 값은 1~6이어야 합니다."; return false; }
            Session.SetNextDiceResults(first, second); message = $"다음 주사위: {first}, {second}"; return true;
        }

        public bool SetNextMonster(string query, out string message)
        {
            var normalizedQuery = NormalizeConsoleToken(query);
            foreach (var definition in monsterDefinitions)
                if (definition != null && (NormalizeConsoleToken(definition.Id) == normalizedQuery || NormalizeConsoleToken(definition.DisplayName) == normalizedQuery))
                { nextMonsterOverrideId = definition.Id; message = $"다음 몬스터: {definition.DisplayName} ({definition.Id})"; return true; }
            message = $"몬스터를 찾을 수 없습니다: {query}"; return false;
        }

        public bool SpawnMonsterFromConsole(string query, int tileIndex, out string message)
        {
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount)
            {
                message = $"타일 번호는 0~{State.Board.TileCount - 1} 범위여야 합니다.";
                return false;
            }

            var normalizedQuery = NormalizeConsoleToken(query);
            MonsterDefinition definition = null;
            foreach (var candidate in monsterDefinitions)
                if (candidate != null && (NormalizeConsoleToken(candidate.Id) == normalizedQuery ||
                    NormalizeConsoleToken(candidate.DisplayName) == normalizedQuery))
                {
                    definition = candidate;
                    break;
                }

            if (definition == null)
            {
                message = $"몬스터를 찾을 수 없습니다: {query}";
                return false;
            }

            var previousPhase = State.CurrentPhase;
            var monster = Session.SpawnMonster(definition, difficultyService.GetHealthMultiplier(State.Difficulty));
            monster.CurrentTileIndex = tileIndex;
            monster.DistanceTravelled = tileIndex;
            State.CurrentPhase = previousPhase;
            monsterPresenter.Spawn(monster);
            monsterPresenter.RefreshLayout();
            if (monster.IsBoss)
                StartCoroutine(monsterPresenter.PlayBossSpawnEntrance(monster.InstanceId));
            message = $"{definition.DisplayName} ({definition.Id})을 {tileIndex}번 타일에 소환했습니다.";
            return true;
        }

public bool BuildTowerFromConsole(int tileIndex, out string message)
        {
            pendingConsoleUpgradeTile = -1;
            pendingConsoleUpgrades.Clear();
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount)
            {
                message = $"타일 번호는 0~{State.Board.TileCount - 1} 범위여야 합니다.";
                return false;
            }

            var tile = State.Board.Tiles[tileIndex];
            if (!tile.HasTower)
            {
                if (!tile.CanBuildTower)
                {
                    message = $"{tileIndex}번 타일에는 지정된 타워가 없습니다.";
                    return false;
                }
                var definition = FindTowerDefinition(tile.BuildTowerDefinitionId);
                if (definition == null)
                {
                    message = $"타워 데이터를 찾을 수 없습니다: {tile.BuildTowerDefinitionId}";
                    return false;
                }
                var previousPhase = State.CurrentPhase;
                try
                {
                    Session.BuildTower(tileIndex, definition);
                    towerPresenter.SetTower(tileIndex, definition, 1);
                    message = $"{tileIndex}번 타일에 {definition.DisplayName} 1티어를 설치했습니다.";
                    return true;
                }
                finally { State.CurrentPhase = previousPhase; }
            }

            var towerDefinition = FindTowerDefinition(tile.Tower.DefinitionId);
            if (towerDefinition == null)
            {
                message = $"타워 데이터를 찾을 수 없습니다: {tile.Tower.DefinitionId}";
                return false;
            }
            foreach (var upgrade in towerUpgradeDefinitions)
                if (upgrade != null && upgrade.Element == towerDefinition.Element &&
                    upgrade.Tier == tile.Tower.UpgradeTier + 1 &&
                    !tile.Tower.AppliedUpgradeIds.Contains(upgrade.Id))
                    pendingConsoleUpgrades.Add(upgrade);

            if (pendingConsoleUpgrades.Count == 0)
            {
                if (tile.Tower.UpgradeTier >= 3)
                {
                    Session.AddPermanentTowerDamageFlatBonus(towerDefinition.Element, MaxTowerElementDamageBonus);
                    message = $"{towerDefinition.DisplayName}은 이미 풀 강화 상태입니다. {towerDefinition.Element} 타워 공격력 +30을 적용했습니다.";
                    return true;
                }
                message = $"{towerDefinition.DisplayName}에 적용 가능한 다음 티어 강화가 없습니다.";
                return false;
            }

            pendingConsoleUpgradeTile = tileIndex;
            var builder = new StringBuilder("적용할 강화를 숫자로 입력하세요.");
            for (var index = 0; index < pendingConsoleUpgrades.Count; index++)
                builder.Append($"\n{index} : {pendingConsoleUpgrades[index].Description}");
            message = builder.ToString();
            return true;
        }

public bool ApplyConsoleUpgradeChoice(int choiceIndex, out string message)
        {
            if (!HasPendingConsoleUpgrade)
            {
                message = "선택 대기 중인 타워 강화가 없습니다.";
                return false;
            }
            if (choiceIndex < 0 || choiceIndex >= pendingConsoleUpgrades.Count)
            {
                message = $"강화 번호는 0~{pendingConsoleUpgrades.Count - 1} 범위여야 합니다.";
                return false;
            }

            var tileIndex = pendingConsoleUpgradeTile;
            var upgrade = pendingConsoleUpgrades[choiceIndex];
            var tile = State.Board.Tiles[tileIndex];
            var definition = FindTowerDefinition(tile.Tower.DefinitionId);
            var previousPhase = State.CurrentPhase;
            try
            {
                Session.UpgradeTower(tileIndex, upgrade);
                if (definition != null)
                    towerPresenter.SetTower(tileIndex, definition, tile.Tower.UpgradeTier);
                message = $"{tileIndex}번 타워에 {upgrade.Description} 강화를 적용했습니다.";
                return true;
            }
            finally
            {
                State.CurrentPhase = previousPhase;
                pendingConsoleUpgradeTile = -1;
                pendingConsoleUpgrades.Clear();
            }
        }


        public bool SetTileEffectFromConsole(int tileIndex, string effectName, out string message)
        {
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount)
            {
                message = $"타일 번호는 0~{State.Board.TileCount - 1} 범위여야 합니다.";
                return false;
            }

            var tile = State.Board.Tiles[tileIndex];
            IReadOnlyList<TowerAttackResult> results;
            var exploded = false;
            if (effectName.Equals("frozen", System.StringComparison.OrdinalIgnoreCase))
            {
                exploded = tile.FireTurnsRemaining > 0;
                results = Session.PlaceIceField(tileIndex);
                message = exploded
                    ? $"{tileIndex}번 타일의 불/얼음 장판이 폭발했습니다. 피해: {15 + State.Difficulty.Level * 14}"
                    : $"{tileIndex}번 타일에 얼음 장판을 1턴 동안 설치했습니다.";
            }
            else if (effectName.Equals("ignite", System.StringComparison.OrdinalIgnoreCase))
            {
                exploded = tile.IceTurnsRemaining > 0;
                results = Session.PlaceFireField(tileIndex);
                message = exploded
                    ? $"{tileIndex}번 타일의 불/얼음 장판이 폭발했습니다. 피해: {15 + State.Difficulty.Level * 14}"
                    : $"{tileIndex}번 타일에 불 장판을 1턴 동안 설치했습니다.";
            }
            else
            {
                message = "장판 종류는 frozen 또는 ignite여야 합니다.";
                return false;
            }

            boardView.RefreshTileEffects(State.Board);
            if (results.Count > 0) StartCoroutine(ApplyConsoleEffectResults(results));
            return true;
        }

        private IEnumerator ApplyConsoleEffectResults(IReadOnlyList<TowerAttackResult> results)
        {
            var killedCount = 0;
            foreach (var result in results)
            {
                yield return monsterPresenter.ApplyAttack(result);
                if (result.Killed) killedCount++;
            }
            difficultyService.AddKills(State.Difficulty, killedCount);
            diceHud.RefreshDifficulty();
            monsterPresenter.RefreshLayout();
            if (State.IsVictory) FinishVictory();
        }

        private TowerUpgradeDefinition FindConsoleUpgrade(TowerElement element, int tier)
        {
            foreach (var upgrade in towerUpgradeDefinitions)
                if (upgrade != null && upgrade.Element == element && upgrade.Tier == tier &&
                    upgrade.Id.EndsWith("_00", System.StringComparison.OrdinalIgnoreCase))
                    return upgrade;
            return null;
        }

        private static string NormalizeConsoleToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var normalized = value.Trim().Normalize(NormalizationForm.FormKC);
            var builder = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.Format && category != UnicodeCategory.Control)
                    builder.Append(char.ToUpperInvariant(character));
            }
            return builder.ToString();
        }

        private void Awake()
        {
            Time.timeScale = 1f;
            var monsterData = Resources.Load<MonsterDatabaseDefinition>(
                "GaeBullBing/MonsterDatabase");
            var towerData = Resources.Load<TowerDatabaseDefinition>(
                "GaeBullBing/TowerDatabase");
            var runtimeUpgradeDatabase = Resources.Load<TowerUpgradeDatabaseDefinition>(
                "GaeBullBing/TowerUpgradeDatabase");
            var difficultyData = Resources.Load<DifficultyDatabaseDefinition>(
                "GaeBullBing/DifficultyDatabase");
            if (!TryLoadRequiredGameData(
                    monsterData, towerData, runtimeUpgradeDatabase, difficultyData,
                    out var dataError))
            {
                Debug.LogError($"게임 데이터 초기화 실패: {dataError}\n" +
                    "JSON 임포트를 완료하기 전에는 게임을 실행할 수 없습니다.", this);
                enabled = false;
                return;
            }

            monsterDefinitions = monsterData.Monsters;
            towerDefinitions = towerData.Towers;
            towerUpgradeDefinitions = runtimeUpgradeDatabase.Upgrades;
            killsPerDifficultyLevel = difficultyData.KillsPerLevel;
            healthMultiplierPerDifficultyLevel = difficultyData.HealthMultiplierPerLevel;
            defensePerDifficultyLevel = difficultyData.DefensePerLevel;
            State = new GameState();
            Session = new GameSession(
                State,
                new BoardService(),
                new PlayerMovementService(),
                new WeightedDiceRoller(new SystemDiceRandom()),
                new MonsterService(),
                new TowerService(),
                new TowerCombatService());
            Session.StartNewGame(boardDefinition: boardDefinition);
            monsterDatabase = new MonsterDatabase(monsterDefinitions);
            var bossAppearanceLevel = FindBossDefinition()?.AppearanceWave ?? DifficultyService.FinalBossLevel;
            difficultyService = new DifficultyService(
                difficultyData.Patterns,
                killsPerDifficultyLevel,
                healthMultiplierPerDifficultyLevel,
                defensePerDifficultyLevel,
                bossAppearanceLevel);
            difficultyService.Reset(State.Difficulty);
            dice3DPresenter = GetComponent<Dice3DPresenter>();
            if (dice3DPresenter == null)
                dice3DPresenter = gameObject.AddComponent<Dice3DPresenter>();
            dice3DPresenter.Initialize(boardView);
            attackEffectPresenter = GetComponent<TowerAttackEffectPresenter>();
            if (attackEffectPresenter == null)
                attackEffectPresenter = gameObject.AddComponent<TowerAttackEffectPresenter>();
            attackEffectPresenter.Initialize(boardView);
            stonePresenter = GetComponent<StonePresenter>();
            if (stonePresenter == null)
                stonePresenter = gameObject.AddComponent<StonePresenter>();
            stonePresenter.Initialize(boardView, attackEffectPresenter.PhysicsAttackSprite);
            tileSelectionView = boardView.GetComponent<BoardTileSelectionView>();
            if (tileSelectionView == null) tileSelectionView = boardView.gameObject.AddComponent<BoardTileSelectionView>();
            tileSelectionView.Initialize(boardView, () => AcceptsGameplayInput);
            tileSelectionView.EnableInspection(ShowTileInformation, CloseTileInformation);
            if (tileInfoPanel == null)
                tileInfoPanel = FindFirstObjectByType<TileInfoPanelView>(FindObjectsInactive.Include);
        }

        private bool TryLoadRequiredGameData(
            MonsterDatabaseDefinition monsters,
            TowerDatabaseDefinition towers,
            TowerUpgradeDatabaseDefinition upgrades,
            DifficultyDatabaseDefinition difficulty,
            out string error)
        {
#if UNITY_EDITOR
            var requiredJsonPaths = new[]
            {
                "Assets/GaeBullBing/Data/Json/Dice.json",
                "Assets/GaeBullBing/Data/Json/Monster.json",
                "Assets/GaeBullBing/Data/Json/Pattern.json",
                "Assets/GaeBullBing/Data/Json/Tile.json",
                "Assets/GaeBullBing/Data/Json/Tower.json",
                "Assets/GaeBullBing/Data/Json/Upgrade.json"
            };
            foreach (var jsonPath in requiredJsonPaths)
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath) == null)
                {
                    error = $"필수 JSON 파일이 없습니다: {jsonPath}";
                    return false;
                }
#endif
            if (boardDefinition == null || boardDefinition.Tiles == null ||
                boardDefinition.Tiles.Length != BoardState.DefaultTileCount)
            {
                error = "Board.asset이 없거나 Tile.json의 36칸 보드 데이터가 유효하지 않습니다.";
                return false;
            }
            if (!DiceCatalog.TryInitialize(out error)) return false;
            if (monsters == null || monsters.Monsters == null || monsters.Monsters.Length == 0)
            {
                error = "MonsterDatabase가 없습니다. Monster.json을 임포트하세요.";
                return false;
            }
            if (towers == null || towers.Towers == null || towers.Towers.Length == 0)
            {
                error = "TowerDatabase가 없습니다. Tower.json을 임포트하세요.";
                return false;
            }
            if (upgrades == null || upgrades.Upgrades == null || upgrades.Upgrades.Length == 0)
            {
                error = "TowerUpgradeDatabase가 없습니다. Upgrade.json을 임포트하세요.";
                return false;
            }
            if (difficulty == null || difficulty.Patterns == null ||
                difficulty.Patterns.Length == 0)
            {
                error = "DifficultyDatabase가 없습니다. Pattern.json을 임포트하세요.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private void Start()
        {
            playerView.Initialize(boardView, State.Player.CurrentTileIndex);
            playerView.TileMoveStarted += monsterPresenter.SetPlayerTile;
            playerView.TileEntered += OnPlayerTileEntered;
            monsterPresenter.SetPlayerTile(State.Player.CurrentTileIndex);
            stonePresenter.Refresh(State);
            diceHud.Bind(this);
            if (gameFlowView == null)
                gameFlowView = FindFirstObjectByType<GameFlowView>(FindObjectsInactive.Include);
            gameFlowView?.Bind(this);
            if (startImmediatelyAfterReload)
            {
                startImmediatelyAfterReload = false;
                isBusy = true;
                diceHud.SetBusy();
                gameFlowView?.BeginRestart();
            }
            else
            {
                isBusy = true;
                diceHud.SetBusy();
                var fadePortraits = fadeTitleAfterReload;
                fadeTitleAfterReload = false;
                gameFlowView?.ShowTitle(fadePortraits);
            }
        }

        public void StartGameFromTitle()
        {
            gameFlowView?.HideAll();
            isBusy = false;
            AcceptsGameplayInput = true;
            diceHud.BeginPlayerTurn();
        }

        public void ReturnToTitle()
        {
            AcceptsGameplayInput = false;
            startImmediatelyAfterReload = false;
            fadeTitleAfterReload = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void ResetGameFromConsole()
        {
            // Reloading the scene reconstructs GameState, presenters and transient effects,
            // while the title flags ensure the fresh scene opens at the title screen.
            ReturnToTitle();
        }

        public bool SetGameSpeedFromConsole(float speed, out string message)
        {
            if (speed != 1f && speed != 2f && speed != 4f && speed != 8f)
            {
                message = "배속은 1, 2, 4, 8 중 하나여야 합니다.";
                return false;
            }
            Time.timeScale = speed;
            message = $"게임 배속을 {speed:0}배로 변경했습니다.";
            return true;
        }

        public void RestartGame()
        {
            AcceptsGameplayInput = false;
            startImmediatelyAfterReload = true;
            fadeTitleAfterReload = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnPlayerTileEntered(int tileIndex)
        {
            monsterPresenter.SetPlayerTile(tileIndex);
        }

        public void RollDiceAndMovePlayer()
        {
            if (!AcceptsGameplayInput || isBusy) return;
            if (tileInfoOpen) StartCoroutine(CloseTileInformationThenRoll());
            else StartCoroutine(RollAndMoveRoutine());
        }

        private IEnumerator CloseTileInformationThenRoll()
        {
            isBusy = true;
            HideTileInformation();
            yield return cameraController.ReturnToOverview();
            yield return RollAndMoveRoutine();
        }

        private void ShowTileInformation(int tileIndex)
        {
            var returnToPlayerFocus = tileInfoOpen
                ? tileInfoReturnsToPlayerFocus
                : State.CurrentPhase == TurnPhase.TileAction || State.CurrentPhase == TurnPhase.TowerSelection;
            ShowTileInformation(tileIndex, true, returnToPlayerFocus);
        }

        private void ShowTileInformation(int tileIndex, bool focusCamera, bool returnToPlayerFocus = false)
        {
            if (tileInfoPanel == null || tileIndex < 0 || tileIndex >= State.Board.TileCount)
                return;

            tileInfoOpen = true;
            inspectedTileIndex = tileIndex;
            tileInfoReturnsToPlayerFocus = returnToPlayerFocus;
            tileInfoPanel.Show(
                $"타일 {tileIndex}",
                BuildTileDescription(tileIndex),
                BuildMonsterDescription(tileIndex));

            if (!focusCamera) return;
            if (tileInfoCameraRoutine != null) StopCoroutine(tileInfoCameraRoutine);
            tileInfoCameraRoutine = StartCoroutine(FocusTileInformation(tileIndex));
        }

        private IEnumerator FocusTileInformation(int tileIndex)
        {
            yield return cameraController.FocusOnTile(tileIndex);
            tileInfoCameraRoutine = null;
        }

        private void CloseTileInformation()
        {
            if (!tileInfoOpen) return;
            var returnToPlayerFocus = tileInfoReturnsToPlayerFocus;
            HideTileInformation();
            tileInfoCameraRoutine = StartCoroutine(ReturnFromTileInformation(returnToPlayerFocus));
        }

        private void HideTileInformation()
        {
            tileInfoOpen = false;
            inspectedTileIndex = -1;
            tileInfoReturnsToPlayerFocus = false;
            tileInfoPanel?.Hide();
            if (tileInfoCameraRoutine != null)
            {
                StopCoroutine(tileInfoCameraRoutine);
                tileInfoCameraRoutine = null;
            }
        }

        private void RefreshOpenTileInformation()
        {
            if (!tileInfoOpen || inspectedTileIndex < 0 || inspectedTileIndex >= State.Board.TileCount)
                return;
            tileInfoPanel?.Show(
                $"타일 {inspectedTileIndex}",
                BuildTileDescription(inspectedTileIndex),
                BuildMonsterDescription(inspectedTileIndex));
        }

        private IEnumerator ReturnFromTileInformation(bool returnToPlayerFocus)
        {
            if (returnToPlayerFocus)
                yield return cameraController.FocusOn(playerView);
            else
                yield return cameraController.ReturnToOverview();
            tileInfoCameraRoutine = null;
        }

        private string BuildTileDescription(int tileIndex)
        {
            var tile = State.Board.Tiles[tileIndex];
            var featherDescription = tile.HasBossFeather
                ? "[타일 상태: 깃털]\n이 타일의 타워는 까마귀가 깃털을 회수할 때까지 공격할 수 없습니다.\n\n"
                : string.Empty;

            if (tileIndex == 0 || tileIndex == 18)
                return featherDescription + "[모서리 능력: 이동]\n원하는 타일을 선택하고 해당 타일까지 시계 방향으로 이동합니다.\n이동 중 출발지를 통과하면 주사위 강화가 발생합니다.";
            if (tileIndex == 9 || tileIndex == 27)
                return featherDescription + $"[모서리 능력: 전체 강화]\n불·얼음·물리·전기 중 하나를 선택해 해당 속성 타워의 공격력을 영구적으로 {cornerDamageRateBonus * 100f:0}% 증가시킵니다.\n이후 건설되는 타워에도 적용됩니다.";

            if (!tile.HasTower)
            {
                var buildable = FindTowerDefinition(tile.BuildTowerDefinitionId);
                return featherDescription + (buildable == null
                    ? "[타워]\n설치된 타워가 없습니다."
                    : $"[타워]\n설치된 타워가 없습니다.\n건설 가능: {buildable.DisplayName} ({GetElementName(buildable.Element)})");
            }

            var definition = FindTowerDefinition(tile.Tower.DefinitionId);
            if (definition == null) return featherDescription + $"[타워]\n정의 없음: {tile.Tower.DefinitionId}";
            var calculation = CalculateDisplayStats(definition, tile);
            var stats = calculation.CombatStats;
            var damageFormula = calculation.DamageFormula;
            var bonusAttacks = Mathf.Max(
                tile.Tower.BonusAttackCount, tile.Tower.PendingBonusAttackCount);
            var attackFormula = bonusAttacks > 0
                ? $"{stats.AttackCount} + {bonusAttacks}"
                : stats.AttackCount.ToString();
            var builder = new StringBuilder();
            builder.AppendLine($"[타워] {definition.DisplayName}  T{tile.Tower.UpgradeTier}");
            builder.AppendLine($"속성: {GetElementName(definition.Element)}");
            builder.AppendLine($"\uACF5\uACA9\uB825: {stats.Damage}  |  \uC0AC\uAC70\uB9AC: {stats.Range}");
            builder.AppendLine($"\uB300\uC0C1: {stats.TargetCount}  |  \uACF5\uACA9 \uD69F\uC218: {attackFormula}");
            builder.AppendLine($"\uACF5\uACA9\uB825 \uACC4\uC0B0: {damageFormula}");
            builder.AppendLine();
            builder.AppendLine("[적용된 업그레이드]");
            if (tile.Tower.AppliedUpgradeIds.Count == 0) builder.Append("없음");
            else foreach (var id in tile.Tower.AppliedUpgradeIds)
            {
                var upgrade = FindUpgradeDefinition(id);
                builder.AppendLine(upgrade == null ? $"• {id}" : $"• T{upgrade.Tier} {upgrade.DisplayName}\n  {upgrade.Description}");
            }
            return featherDescription + builder.ToString().TrimEnd();
        }

        private string BuildMonsterDescription(int tileIndex)
        {
            var builder = new StringBuilder("[몬스터]\n");
            var count = 0;
            foreach (var monster in State.Monsters)
            {
                if (monster.IsDead || monster.CurrentTileIndex != tileIndex) continue;
                count++;
                var definition = FindMonsterDefinition(monster.DefinitionId);
                builder.Append($"{count}. {(definition == null ? monster.DefinitionId : definition.DisplayName)}  HP {Mathf.Max(0, Mathf.FloorToInt(monster.CurrentHealth))}/{Mathf.FloorToInt(monster.MaxHealth)}");
                var statuses = BuildMonsterStatuses(monster);
                if (!string.IsNullOrEmpty(statuses)) builder.Append($"\n   {statuses}");
                builder.AppendLine();
            }
            if (count == 0) builder.Append("없음");
            return builder.ToString().TrimEnd();
        }

        private static string BuildMonsterStatuses(MonsterState monster)
        {
            var values = new List<string>();
            if (monster.BurnStacks > 0) values.Add($"화상 {monster.BurnStacks}중첩");
            if (monster.Shocked) values.Add("감전");
            if (monster.FrozenMovesRemaining > 0) values.Add("빙결");
            if (monster.StunnedMovesRemaining > 0) values.Add("이동 불가");
            if (monster.KnockbackConsumed) values.Add("넉백 면역");
            return string.Join(", ", values);
        }

        private TowerStatCalculation CalculateDisplayStats(
            TowerDefinition definition,
            TileState tile)
        {
            var rateBonus = State.PermanentAllTowerDamageRateBonus +
                State.GetPermanentTowerDamageRateBonus(definition.Element) +
                State.GetPermanentLineTowerDamageRateBonus(MonsterService.GetLine(tile.Index)) +
                GetLineAuraDamageRateBonus(tile);
            return TowerStatCalculator.Calculate(
                definition,
                tile.Tower,
                towerUpgradeDefinitions,
                rateBonus,
                State.GetPermanentTowerDamageFlatBonus(definition.Element));
        }

        private float GetLineAuraDamageRateBonus(TileState targetTile)
        {
            var line = MonsterService.GetLine(targetTile.Index);
            var rate = 0f;
            foreach (var tile in State.Board.Tiles)
                if (tile.HasTower && tile.Tower.InstanceId != targetTile.Tower.InstanceId &&
                    MonsterService.GetLine(tile.Index) == line &&
                    tile.Tower.HasEffect(TowerEffectCatalog.LineTowerBuff))
                    rate += .2f;
            return rate;
        }

        private TowerUpgradeDefinition FindUpgradeDefinition(string id)
        {
            foreach (var definition in towerUpgradeDefinitions) if (definition != null && definition.Id == id) return definition;
            return null;
        }

        private MonsterDefinition FindMonsterDefinition(string id)
        {
            foreach (var definition in monsterDefinitions) if (definition != null && definition.Id == id) return definition;
            return null;
        }

        private static string GetElementName(TowerElement element) => element switch
        {
            TowerElement.Fire => "불",
            TowerElement.Ice => "얼음",
            TowerElement.Physics => "물리",
            TowerElement.Electric => "전기",
            _ => "없음"
        };

        private IEnumerator RollAndMoveRoutine()
        {
            isBusy = true;
            diceHud.SetRolling(true);
            yield return new WaitForSeconds(diceRevealDelay);

            var startTileIndex = State.Player.CurrentTileIndex;
            var distance = Session.RollDiceAndMovePlayer();
            pendingDiceTuning |= startTileIndex + distance >= State.Board.TileCount;
            diceHud.SetResults(State.LastDiceResults[0], State.LastDiceResults[1]);
            yield return dice3DPresenter.Roll(State.Dice, State.LastDiceResults[0], State.LastDiceResults[1]);
            yield return cameraController.FocusOn(playerView);
            yield return playerView.MoveSteps(startTileIndex, distance);
            BeginCurrentTileAction();
        }

        private void BeginCurrentTileAction()
        {
            State.CurrentPhase = TurnPhase.TileAction;
            diceHud.SetBusy();
            var tile = State.Board.Tiles[State.Player.CurrentTileIndex];
            ApplyArrivalTowerEffects(tile);
            ShowTileInformation(tile.Index, false, tile.HasTower || tile.CanBuildTower);
            if (TryOpenCornerAction(State.Player.CurrentTileIndex))
                return;
            if (!tile.HasTower && !tile.CanBuildTower)
            {
                StartCoroutine(CompleteTileActionRoutine());
                return;
            }
            radialMenu.ShowPrimary(
                playerView.transform,
                UnityEngine.Camera.main,
                tile.HasTower,
                OpenTowerChoices);
        }

        private void ApplyArrivalTowerEffects(TileState tile)
        {
            if (tile.HasTower && tile.Tower.HasEffect(TowerEffectCatalog.TileStepLineBuff))
                Session.AddPermanentLineTowerDamageRateBonus(MonsterService.GetLine(tile.Index),
                    tile.Tower.GetEffectValue(TowerEffectCatalog.TileStepLineBuff, 10f) / 100f);
        }

        private bool TryOpenCornerAction(int tileIndex)
        {
            if (cornerActionMenu == null) return false;
            if (tileIndex == 9 || tileIndex == 27)
            {
                State.CurrentPhase = TurnPhase.CornerSelection;
                cornerActionMenu.ShowElementSelection(SelectCornerElement);
                return true;
            }
            if (tileIndex == 0 || tileIndex == 18)
            {
                State.CurrentPhase = TurnPhase.CornerSelection;
                cornerActionMenu.Hide();
                StartCoroutine(PrepareTileSelectionRoutine());
                return true;
            }
            return false;
        }

        private IEnumerator PrepareTileSelectionRoutine()
        {
            yield return cameraController.ReturnToOverview();
            var sourceTileIndex = State.Player.CurrentTileIndex;
            tileSelectionView.BeginSelection(
                SelectTeleportDestination,
                tileIndex => ShowTileInformation(tileIndex, false),
                HideTileInformation,
                tileIndex => tileIndex != sourceTileIndex);
        }

        public void SelectCornerElement(TowerElement element)
        {
            if (State.CurrentPhase != TurnPhase.CornerSelection || element == TowerElement.None) return;
            Session.AddPermanentTowerDamageRateBonus(element, cornerDamageRateBonus);
            HideTileInformation();
            cornerActionMenu.Hide(); StartCoroutine(CompleteTileActionRoutine());
        }

        public void SelectTeleportDestination(int tileIndex)
        {
            if (State.CurrentPhase != TurnPhase.CornerSelection || tileIndex < 0 ||
                tileIndex >= State.Board.TileCount || tileIndex == State.Player.CurrentTileIndex) return;
            HideTileInformation();
            StartCoroutine(MoveToSelectedTileRoutine(tileIndex));
        }

        private IEnumerator MoveToSelectedTileRoutine(int tileIndex)
        {
            var start = State.Player.CurrentTileIndex;
            var distance = (tileIndex - start + State.Board.TileCount) % State.Board.TileCount;
            if (start == 0 || distance > 0 && start + distance >= State.Board.TileCount)
                pendingDiceTuning = true;
            var focusRoutine = StartCoroutine(cameraController.FocusOn(playerView));
            yield return playerView.MoveSteps(start, distance);
            yield return focusRoutine;
            Session.TeleportPlayer(tileIndex);
            if (tileIndex == 0 || tileIndex == 18)
            {
                yield return CompleteTileActionRoutine();
                yield break;
            }
            BeginCurrentTileAction();
        }

        public void OpenTowerChoices()
        {
            if (State.CurrentPhase != TurnPhase.TileAction)
                return;

            State.CurrentPhase = TurnPhase.TowerSelection;
            var tile = State.Board.Tiles[State.Player.CurrentTileIndex];
            if (tile.HasTower)
            {
                var upgrades = GetUpgradeChoices(tile);
                if (upgrades.Count == 0)
                {
                    var definition = FindTowerDefinition(tile.Tower.DefinitionId);
                    if (definition != null)
                        Session.AddPermanentTowerDamageFlatBonus(definition.Element, MaxTowerElementDamageBonus);
                    radialMenu.Hide();
                    HideTileInformation();
                    StartCoroutine(CompleteTileActionRoutine());
                    return;
                }
                radialMenu.ShowUpgradeChoices(upgrades, SelectUpgrade);
                return;
            }
            var choices = GetTowerChoices(tile);
            if (choices.Count == 0)
            {
                radialMenu.Hide();
                StartCoroutine(CompleteTileActionRoutine());
                return;
            }

            SelectTower(choices[0]);
        }

        public void SelectTower(TowerDefinition definition)
        {
            if (State.CurrentPhase != TurnPhase.TowerSelection || definition == null)
                return;

            var tileIndex = State.Player.CurrentTileIndex;
            var tile = State.Board.Tiles[tileIndex];
            Session.BuildTower(tileIndex, definition);

            towerPresenter.SetTower(tileIndex, definition);
            radialMenu.Hide();
            HideTileInformation();
            StartCoroutine(CompleteTileActionRoutine());
        }

        public void SelectUpgrade(TowerUpgradeDefinition upgrade)
        {
            if (State.CurrentPhase != TurnPhase.TowerSelection || upgrade == null) return;
            var tileIndex = State.Player.CurrentTileIndex;
            Session.UpgradeTower(tileIndex, upgrade);
            var tile = State.Board.Tiles[tileIndex];
            var definition = FindTowerDefinition(tile.Tower.DefinitionId);
            if (definition != null)
                towerPresenter.SetTower(tileIndex, definition, tile.Tower.UpgradeTier);
            radialMenu.Hide();
            HideTileInformation();
            StartCoroutine(CompleteTileActionRoutine());
        }

        private List<TowerDefinition> GetTowerChoices(TileState tile)
        {
            var choices = new List<TowerDefinition>();
            if (!tile.HasTower)
            {
                var buildDefinition = FindTowerDefinition(tile.BuildTowerDefinitionId);
                if (buildDefinition != null)
                    choices.Add(buildDefinition);
                return choices;
            }

            return choices;
        }

        private List<TowerUpgradeDefinition> GetUpgradeChoices(TileState tile)
        {
            var result = new List<TowerUpgradeDefinition>();
            var tower = FindTowerDefinition(tile.Tower.DefinitionId); if (tower == null) return result;
            var pool = new List<TowerUpgradeDefinition>();
            foreach (var upgrade in towerUpgradeDefinitions)
                if (upgrade != null && upgrade.Element == tower.Element && upgrade.Tier == tile.Tower.UpgradeTier + 1 && !tile.Tower.AppliedUpgradeIds.Contains(upgrade.Id)) pool.Add(upgrade);
            while (pool.Count > 0 && result.Count < 3)
            {
                var total = 0; foreach (var item in pool) total += Mathf.Max(0, item.Weight);
                var roll = total > 0 ? Random.Range(0, total) : Random.Range(0, pool.Count);
                var selected = 0;
                if (total > 0) { for (var i = 0; i < pool.Count; i++) { roll -= Mathf.Max(0, pool[i].Weight); if (roll < 0) { selected = i; break; } } }
                else selected = roll;
                result.Add(pool[selected]); pool.RemoveAt(selected);
            }
            return result;
        }

        private TowerDefinition FindTowerDefinition(string id)
        {
            foreach (var definition in towerDefinitions)
                if (definition != null && definition.Id == id)
                    return definition;
            return null;
        }

        private IEnumerator CompleteTileActionRoutine()
        {
            State.CurrentPhase = TurnPhase.CameraOverview;
            yield return cameraController.ReturnToOverview();
            if (pendingDiceTuning)
            {
                pendingDiceTuning = false;
                diceTuningComplete = false;
                State.CurrentPhase = TurnPhase.DiceTuning;
                var diceSystem = diceHud.GetComponent<DiceSystemView>();
                diceSystem.ShowLapReward(Session.CreateLapReward(), () => diceTuningComplete = true);
                yield return new WaitUntil(() => diceTuningComplete);
            }
            yield return ResolveEnemyTurnRoutine();
        }





        private IEnumerator ResolveEnemyTurnRoutine()
        {
            isBusy = true;
            diceHud.SetBusy();
            var killedCount = 0;

            var standbyResults = Session.ResolveMonsterStandbyEffects();
            Session.CollectKillRewards(standbyResults);
            foreach (var result in standbyResults)
            {
                if (result.Killed) killedCount++;
                yield return PlayAttackResult(result, new HashSet<int>());
            }
            if (State.IsVictory)
            {
                FinishVictory();
                yield break;
            }

            if (difficultyService.IsBossLevel(State.Difficulty))
            {
                nextMonsterOverrideId = null;
                if (!State.BossSpawned)
                {
                    var bossDefinition = FindBossDefinition();
                    if (bossDefinition == null)
                    {
                        Debug.LogError("BOSS_001 MonsterDefinition을 찾을 수 없습니다.", this);
                        State.CurrentPhase = TurnPhase.Defeat;
                    }
                    else
                    {
                        var boss = Session.SpawnMonster(bossDefinition, 1f);
                        yield return monsterPresenter.SpawnWithEntrance(boss);
                    }
                }
            }
            else
            {
                var scheduledMonsterId = difficultyService.GetNextMonsterId(State.Difficulty);
                var monsterId = string.IsNullOrEmpty(nextMonsterOverrideId)
                    ? scheduledMonsterId
                    : nextMonsterOverrideId;
                nextMonsterOverrideId = null;
                var definition = monsterDatabase.Get(monsterId);
                var spawnedMonster = Session.SpawnMonster(definition,
                    difficultyService.GetHealthMultiplier(State.Difficulty));
                if (spawnedMonster.IsBoss)
                    yield return monsterPresenter.SpawnWithEntrance(spawnedMonster);
                else
                    monsterPresenter.Spawn(spawnedMonster);
            }

            if (State.IsGameOver)
            {
                FinishDefeat();
                yield break;
            }

            var moveResults = Session.MoveMonsters(towerDefinitions, towerUpgradeDefinitions);
            diceHud.RefreshPlayerHealth();
            foreach (var result in moveResults)
            {
                if (result.IsBoss &&
                    monsterPresenter.TryGetViewTransform(result.InstanceId, out var bossTransform))
                {
                    yield return cameraController.FocusOn(bossTransform);
                    yield return monsterPresenter.Move(result);
                    yield return cameraController.ReturnToOverview();
                }
                else
                    yield return monsterPresenter.Move(result);
                Session.CollectKillRewards(result.TileEffectResults);
                foreach (var tileEffect in result.TileEffectResults)
                {
                    if (tileEffect.Killed) killedCount++;
                    yield return PlayAttackResult(tileEffect, new HashSet<int>());
                }
            }
            monsterPresenter.RefreshLayout();
            boardView.RefreshTileEffects(State.Board);

            if (State.IsGameOver)
            {
                FinishDefeat();
                yield break;
            }

            var attackResults = Session.ResolveTowerCombat(towerDefinitions, towerUpgradeDefinitions);
            var illuminatedLineTowerIds = new HashSet<int>();
            var consumedStoneAttackResults = new HashSet<int>();
            for (var attackIndex = 0; attackIndex < attackResults.Count; attackIndex++)
            {
                var attackResult = attackResults[attackIndex];
                if (attackResult.VisualKind == TowerAttackVisualKind.RollingStone)
                {
                    if (consumedStoneAttackResults.Contains(attackIndex)) continue;
                    stonePresenter.Refresh(State);
                    yield return stonePresenter.PlayResolvedMovement(
                        State,
                        attackResults,
                        consumedStoneAttackResults,
                        result => PlayAttackResult(result, illuminatedLineTowerIds),
                        attackResult.TowerInstanceId);
                    stonePresenter.Refresh(State);
                    continue;
                }
                if (attackResult.VisualKind == TowerAttackVisualKind.ChainLine)
                {
                    var chainTowerId = attackResult.TowerInstanceId;
                    var chainResults = new List<TowerAttackResult>();
                    while (attackIndex < attackResults.Count &&
                           attackResults[attackIndex].VisualKind == TowerAttackVisualKind.ChainLine &&
                           attackResults[attackIndex].TowerInstanceId == chainTowerId)
                    {
                        chainResults.Add(attackResults[attackIndex]);
                        attackIndex++;
                    }
                    attackIndex--;
                    yield return PlayAttackResultsTogether(chainResults, illuminatedLineTowerIds);
                    continue;
                }
                if (attackResult.VisualKind == TowerAttackVisualKind.ChainTile)
                {
                    var chainTowerId = attackResult.TowerInstanceId;
                    var chainTileIndex = attackResult.TargetTileIndex;
                    var chainTileResults = new List<TowerAttackResult>();
                    while (attackIndex < attackResults.Count &&
                           attackResults[attackIndex].VisualKind == TowerAttackVisualKind.ChainTile &&
                           attackResults[attackIndex].TowerInstanceId == chainTowerId &&
                           attackResults[attackIndex].TargetTileIndex == chainTileIndex)
                    {
                        chainTileResults.Add(attackResults[attackIndex]);
                        attackIndex++;
                    }
                    attackIndex--;
                    yield return PlayAttackResultsTogether(chainTileResults, illuminatedLineTowerIds);
                    continue;
                }

                
if (attackResult.VisualKind != TowerAttackVisualKind.AreaTile)
                {
                    yield return PlayAttackResult(attackResult, illuminatedLineTowerIds);
                    continue;
                }

                var areaTowerId = attackResult.TowerInstanceId;
                var areaTiles = new List<int>();
                while (attackIndex < attackResults.Count &&
                       attackResults[attackIndex].VisualKind == TowerAttackVisualKind.AreaTile &&
                       attackResults[attackIndex].TowerInstanceId == areaTowerId)
                {
                    areaTiles.Add(attackResults[attackIndex].TargetTileIndex);
                    attackIndex++;
                }
                attackIndex--;
                if (attackEffectPresenter != null)
                    yield return attackEffectPresenter.PlayAreaTiles(State, areaTowerId, areaTiles);
            }
            if (State.IsVictory)
            {
                boardView.RefreshTileEffects(State.Board);
                FinishVictory();
                yield break;
            }
            var statusResults = Session.ResolveMonsterTurnEndEffects();
            for (var resultIndex = 0; resultIndex < statusResults.Count; resultIndex++)
            {
                yield return PlayAttackResult(statusResults[resultIndex], illuminatedLineTowerIds);
            }
            boardView.RefreshTileEffects(State.Board);
            foreach (var attackResult in attackResults)
                if (attackResult.Killed) killedCount++;
            foreach (var statusResult in statusResults)
                if (statusResult.Killed) killedCount++;
            Session.CollectKillRewards(attackResults);
            Session.CollectKillRewards(statusResults);
            difficultyService.AddKills(State.Difficulty, killedCount);
            diceHud.RefreshDifficulty();

            if (State.IsVictory)
            {
                boardView.RefreshTileEffects(State.Board);
                FinishVictory();
                yield break;
            }

            Session.CompleteRound();
            diceHud.RefreshDiceFaces();

            RefreshOpenTileInformation();
            diceHud.BeginPlayerTurn();
            isBusy = false;
        }

        private MonsterDefinition FindBossDefinition()
        {
            foreach (var definition in monsterDefinitions)
                if (definition != null &&
                    (definition.Tier == MonsterTier.Boss || definition.Id == "BOSS_001"))
                    return definition;
            return null;
        }

        private void FinishVictory()
        {
            if (finishRoutineStarted) return;
            finishRoutineStarted = true;
            AcceptsGameplayInput = false;
            isBusy = true;
            diceHud.SetBusy();
            radialMenu.Hide();
            cornerActionMenu?.Hide();
            HideTileInformation();
            StartCoroutine(FinishVictoryRoutine());
        }

        private void FinishDefeat()
        {
            if (finishRoutineStarted) return;
            finishRoutineStarted = true;
            AcceptsGameplayInput = false;
            isBusy = true;
            diceHud.SetBusy();
            radialMenu.Hide();
            cornerActionMenu?.Hide();
            HideTileInformation();
            StartCoroutine(FinishDefeatRoutine());
        }

        private IEnumerator FinishVictoryRoutine()
        {
            if (gameFlowView != null)
                yield return gameFlowView.PlayOutro();
            diceHud.ShowGameClear();
            gameFlowView?.ShowVictory();
            isBusy = false;
        }

        private IEnumerator FinishDefeatRoutine()
        {
            if (gameFlowView != null)
                yield return gameFlowView.PlayOutro();
            diceHud.ShowGameOver(State.EscapedMonsterCount, State.EscapeLimit);
            gameFlowView?.ShowDefeat();
            isBusy = false;
        }

private IEnumerator PlayAttackResult(
            TowerAttackResult result,
            ISet<int> illuminatedLineTowerIds)
        {
            var impactApplied = false;
            if (attackEffectPresenter != null)
                yield return attackEffectPresenter.Play(State, result, illuminatedLineTowerIds, () =>
                {
                    impactApplied = true;
                    monsterPresenter.ApplyAttackAtImpact(result);
                    RefreshAttackTileEffects(result);
                });
            if (!impactApplied)
            {
                monsterPresenter.ApplyAttackAtImpact(result);
                RefreshAttackTileEffects(result);
            }
            yield return monsterPresenter.CompleteAttack(result);
        }

private IEnumerator PlayAttackResultsTogether(
            IReadOnlyList<TowerAttackResult> results,
            ISet<int> illuminatedLineTowerIds)
        {
            if (results == null || results.Count == 0) yield break;
            var impactApplied = false;
            if (attackEffectPresenter != null)
                yield return attackEffectPresenter.Play(State, results[0], illuminatedLineTowerIds, () =>
                {
                    impactApplied = true;
                    foreach (var result in results)
                        monsterPresenter.ApplyAttackAtImpact(result);
                    RefreshAttackTileEffects(results[0]);
                });
            if (!impactApplied)
            {
                foreach (var result in results)
                    monsterPresenter.ApplyAttackAtImpact(result);
                RefreshAttackTileEffects(results[0]);
            }
            foreach (var result in results)
                yield return monsterPresenter.CompleteAttack(result);
        }
    

private void RefreshAttackTileEffects(TowerAttackResult result)
        {
            if (boardView == null || State?.Board == null) return;
            if (result.VisualKind == TowerAttackVisualKind.ChainLine)
            {
                boardView.RefreshTileEffects(State.Board);
                return;
            }
            if (result.TargetTileIndex < 0) return;

            var radius = 0;
            foreach (var tile in State.Board.Tiles)
                if (tile.HasTower && tile.Tower.InstanceId == result.TowerInstanceId &&
                    tile.Tower.DefinitionId == "TOW_04")
                {
                    radius = 1 + Mathf.Max(0, Mathf.RoundToInt(
                        tile.Tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f)));
                    break;
                }
            for (var offset = -radius; offset <= radius; offset++)
            {
                var tileIndex = (result.TargetTileIndex + offset + State.Board.TileCount) % State.Board.TileCount;
                boardView.RefreshTileEffect(State.Board, tileIndex);
            }
        }
}
}
