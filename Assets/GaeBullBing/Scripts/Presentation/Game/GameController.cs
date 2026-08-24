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
using GaeBullBing.Presentation.Audio;
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
        private enum GameFlowState
        {
            Title,
            Gameplay,
            Boss,
            Victory,
            Defeat
        }

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
        [SerializeField] private TurnTransitionBannerView turnTransitionBanner;
        [SerializeField, Min(0f)] private float diceRevealDelay = 0.35f;
        [SerializeField, Range(0f, 1f)] private float cornerDamageRateBonus = .2f;
        [Header("Lap Completion Presentation")]
        [SerializeField, Min(0f)] private float lapEnhancementPause = .1f;

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
        private GameFlowState? currentFlowState;
        
        private int pendingConsoleUpgradeTile = -1;
        private readonly List<TowerUpgradeDefinition> pendingConsoleUpgrades = new();
        private TowerDefinition pendingConsoleBuildDefinition;
        private int pendingBonusBuildTile = -1;
        private TowerDefinition pendingBonusBuildDefinition;
        public bool HasPendingConsoleUpgrade => pendingConsoleUpgradeTile >= 0 && pendingConsoleUpgrades.Count > 0;
private bool finishRoutineStarted;

        public GameState State { get; private set; }
        public GameSession Session { get; private set; }
        public int TotalKills => State?.Difficulty?.KillCount ?? 0;
        public bool HasGameplayStarted { get; private set; }

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
            pendingConsoleBuildDefinition = null;
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
                    if (tile.IsBonusTile)
                    {
                        PopulateConsoleUpgradeChoices(definition, 2, null);
                        if (pendingConsoleUpgrades.Count == 0)
                        {
                            message = $"{definition.DisplayName}의 2티어 강화 데이터가 없습니다.";
                            return false;
                        }
                        pendingConsoleUpgradeTile = tileIndex;
                        pendingConsoleBuildDefinition = definition;
                        message = BuildConsoleUpgradePrompt();
                        return true;
                    }

                    Session.BuildTower(tileIndex, definition);
                    StartCoroutine(towerPresenter.PlayBuildAnimation(tileIndex, definition, 1));
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
            var upgradeTargetTier = GetUpgradeTargetTier(tile);
            PopulateConsoleUpgradeChoices(tile, towerDefinition);

            if (pendingConsoleUpgrades.Count == 0)
            {
                if (upgradeTargetTier < 0 && tile.Tower.UpgradeTier >= 3)
                {
                    Session.AddPermanentTowerDamageFlatBonus(towerDefinition.Element, MaxTowerElementDamageBonus);
                    StartCoroutine(PlayElementTowerEnhancementEffect(towerDefinition.Element));
                    message = $"{towerDefinition.DisplayName}은 이미 풀 강화 상태입니다. {towerDefinition.Element} 타워 공격력 +30을 적용했습니다.";
                    return true;
                }
                message = $"{towerDefinition.DisplayName}에 적용 가능한 다음 티어 강화가 없습니다.";
                return false;
            }

            pendingConsoleUpgradeTile = tileIndex;
            message = BuildConsoleUpgradePrompt();
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
            var definition = pendingConsoleBuildDefinition ??
                FindTowerDefinition(tile.Tower.DefinitionId);
            var previousPhase = State.CurrentPhase;
            try
            {
                var isNewTower = pendingConsoleBuildDefinition != null;
                if (isNewTower)
                    Session.BuildTower(tileIndex, pendingConsoleBuildDefinition);
                Session.UpgradeTower(tileIndex, upgrade);
                if (definition != null)
                {
                    StartCoroutine(isNewTower
                        ? towerPresenter.PlayBuildAnimation(
                            tileIndex,
                            definition,
                            tile.Tower.UpgradeTier)
                        : towerPresenter.PlayUpgradeAnimation(
                            tileIndex,
                            definition,
                            tile.Tower.UpgradeTier));
                }
                message = $"{tileIndex}번 타워에 {upgrade.Description} 강화를 적용했습니다.";
                return true;
            }
            finally
            {
                State.CurrentPhase = previousPhase;
                pendingConsoleUpgradeTile = -1;
                pendingConsoleUpgrades.Clear();
                pendingConsoleBuildDefinition = null;
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
            boardView.RefreshBuildElementOverlays(State.Board, towerDefinitions);
            boardView.RefreshBonusTileBorders(State.Board);
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
            attackEffectPresenter.Initialize(boardView, towerDefinitions);
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
            if (turnTransitionBanner == null)
                turnTransitionBanner =
                    FindFirstObjectByType<TurnTransitionBannerView>(FindObjectsInactive.Include);
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
                EnterFlowState(GameFlowState.Gameplay);
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
                EnterFlowState(GameFlowState.Title);
            }
        }

        public void StartGameFromTitle()
        {
            EnterFlowState(GameFlowState.Gameplay);
            HasGameplayStarted = true;
            gameFlowView?.HideAll();
            isBusy = true;
            AcceptsGameplayInput = false;
            diceHud.SetBusy();
            StartCoroutine(BeginFirstPlayerTurnRoutine());
        }

        private IEnumerator BeginFirstPlayerTurnRoutine()
        {
            if (turnTransitionBanner != null)
                yield return turnTransitionBanner.PlayPlayerTurn();
            diceHud.BeginPlayerTurn();
            AcceptsGameplayInput = true;
            isBusy = false;
        }

        public void ReturnToTitle()
        {
            HasGameplayStarted = false;
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

        public void RefreshDiceDestinationHighlights()
        {
            if (boardView == null || State?.Board == null || State.Dice == null ||
                State.Dice.Count != DiceInventoryState.EquippedCount)
                return;

            var destinations = new HashSet<int>();
            var first = State.Dice[0];
            var second = State.Dice[1];
            if (first == null || second == null) return;

            for (var firstFace = 0; firstFace < first.Faces.Length; firstFace++)
            {
                if (firstFace >= first.Weights.Length || first.Weights[firstFace] <= 0)
                    continue;
                for (var secondFace = 0; secondFace < second.Faces.Length; secondFace++)
                {
                    if (secondFace >= second.Weights.Length || second.Weights[secondFace] <= 0)
                        continue;
                    var distance = first.Faces[firstFace] + second.Faces[secondFace];
                    destinations.Add((State.Player.CurrentTileIndex + distance) % State.Board.TileCount);
                }
            }
            boardView.SetSelectionHighlights(destinations);
        }

        public void ClearTileSelectionHighlights() =>
            boardView?.ClearSelectionHighlights();

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
                builder.AppendLine(upgrade == null || string.IsNullOrWhiteSpace(upgrade.Description)
                    ? "• 설명 없음"
                    : $"• {upgrade.Description}");
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
            var completesLap = startTileIndex + distance >= State.Board.TileCount;
            Coroutine lapOverviewRoutine = null;
            yield return playerView.MoveSteps(
                startTileIndex,
                distance,
                tileIndex =>
                {
                    if (completesLap && tileIndex == 0)
                        lapOverviewRoutine = StartCoroutine(ReturnToOverviewAfterPress(tileIndex));
                },
                tileIndex => completesLap && tileIndex == 0
                    ? PlayLapCompletionPresentation(lapOverviewRoutine)
                    : null);
            BeginCurrentTileAction();
        }

        private IEnumerator ReturnToOverviewAfterPress(int tileIndex)
        {
            yield return boardView.WaitForPressCompletion(tileIndex);
            yield return cameraController.ReturnToOverview();
        }

        private IEnumerator PlayLapCompletionPresentation(Coroutine overviewRoutine)
        {
            if (overviewRoutine != null)
                yield return overviewRoutine;
            if (lapEnhancementPause > 0f)
                yield return new WaitForSeconds(lapEnhancementPause);
            var audio = AudioManager.Instance;
            audio?.PlaySfx(audio.GameFlow.LapReward);

            if (towerPresenter == null || State?.Board?.Tiles == null)
            {
                yield return cameraController.FocusOn(playerView);
                yield break;
            }

            var tileIndices = new List<int>();
            foreach (var tile in State.Board.Tiles)
                if (tile.HasTower)
                    tileIndices.Add(tile.Index);

            Coroutine focusRoutine = null;
            yield return towerPresenter.PlayAllTowerEnhancementAnimation(
                tileIndices,
                () => focusRoutine = StartCoroutine(cameraController.FocusOn(playerView)));

            if (focusRoutine == null)
                focusRoutine = StartCoroutine(cameraController.FocusOn(playerView));
            yield return focusRoutine;
        }

        public void ApplyDiceRewardTowerBoost(System.Action completed)
        {
            Session.AddPermanentAllTowerDamageRateBonus(.05f);
            StartCoroutine(PlayDiceRewardTowerBoostPresentation(completed));
        }

        private IEnumerator PlayDiceRewardTowerBoostPresentation(System.Action completed)
        {
            var audio = AudioManager.Instance;
            audio?.PlaySfx(audio.GameFlow.LapReward);
            if (towerPresenter != null && State?.Board?.Tiles != null)
            {
                var tileIndices = new List<int>();
                foreach (var tile in State.Board.Tiles)
                    if (tile.HasTower)
                        tileIndices.Add(tile.Index);

                if (tileIndices.Count > 0)
                    yield return towerPresenter.PlayAllTowerEnhancementAnimation(tileIndices);
            }

            completed?.Invoke();
        }

        private void BeginCurrentTileAction()
        {
            State.CurrentPhase = TurnPhase.TileAction;
            diceHud.SetBusy();
            var tile = State.Board.Tiles[State.Player.CurrentTileIndex];
            Session.ResolvePlayerBonusTile(tile.Index);
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
            var sourceTileIndex = State.Player.CurrentTileIndex;
            yield return boardView.WaitForPressCompletion(sourceTileIndex);
            var destinations = new List<int>(State.Board.TileCount - 1);
            for (var tileIndex = 0; tileIndex < State.Board.TileCount; tileIndex++)
                if (tileIndex != sourceTileIndex)
                    destinations.Add(tileIndex);
            boardView.SetSelectionHighlights(destinations);
            yield return cameraController.ReturnToOverview();
            ShowTileInformation(sourceTileIndex, false);
            tileSelectionView.BeginSelection(
                SelectTeleportDestination,
                tileIndex => ShowTileInformation(tileIndex, false),
                () => ShowTileInformation(sourceTileIndex, false),
                tileIndex => tileIndex != sourceTileIndex);
        }

        public void SelectCornerElement(TowerElement element)
        {
            if (State.CurrentPhase != TurnPhase.CornerSelection || element == TowerElement.None) return;
            Session.AddPermanentTowerDamageRateBonus(element, cornerDamageRateBonus);
            HideTileInformation();
            cornerActionMenu.Hide();
            StartCoroutine(CompleteFullUpgradeRewardRoutine(element));
        }

        public void SelectTeleportDestination(int tileIndex)
        {
            if (State.CurrentPhase != TurnPhase.CornerSelection || tileIndex < 0 ||
                tileIndex >= State.Board.TileCount || tileIndex == State.Player.CurrentTileIndex) return;
            ClearTileSelectionHighlights();
            HideTileInformation();
            StartCoroutine(MoveToSelectedTileRoutine(tileIndex));
        }

        private IEnumerator MoveToSelectedTileRoutine(int tileIndex)
        {
            var audio = AudioManager.Instance;
            audio?.PlaySfx(audio.Player.Teleport);
            var start = State.Player.CurrentTileIndex;
            var distance = (tileIndex - start + State.Board.TileCount) % State.Board.TileCount;
            var completesLap = distance > 0 && start + distance >= State.Board.TileCount;
            if (completesLap)
            {
                Session.CompletePlayerLap();
                pendingDiceTuning = true;
            }
            var focusRoutine = StartCoroutine(cameraController.FocusOn(playerView));
            Coroutine lapOverviewRoutine = null;
            yield return playerView.MoveSteps(
                start,
                distance,
                enteredTileIndex =>
                {
                    if (completesLap && enteredTileIndex == 0)
                        lapOverviewRoutine = StartCoroutine(
                            ReturnToOverviewAfterPress(enteredTileIndex));
                },
                enteredTileIndex => completesLap && enteredTileIndex == 0
                    ? PlayLapCompletionPresentation(lapOverviewRoutine)
                    : null);
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

            pendingBonusBuildTile = -1;
            pendingBonusBuildDefinition = null;
            State.CurrentPhase = TurnPhase.TowerSelection;
            var tile = State.Board.Tiles[State.Player.CurrentTileIndex];
            if (tile.HasTower)
            {
                var upgrades = GetUpgradeChoices(tile);
                if (upgrades.Count == 0)
                {
                    var definition = FindTowerDefinition(tile.Tower.DefinitionId);
                    if (definition != null && GetUpgradeTargetTier(tile) < 0)
                    {
                        Session.AddPermanentTowerDamageFlatBonus(definition.Element, MaxTowerElementDamageBonus);
                        radialMenu.Hide();
                        HideTileInformation();
                        StartCoroutine(CompleteFullUpgradeRewardRoutine(definition.Element));
                        return;
                    }
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
            if (tile.IsBonusTile)
            {
                var upgrades = GetUpgradeChoices(definition, 2, null);
                if (upgrades.Count > 0)
                {
                    pendingBonusBuildTile = tileIndex;
                    pendingBonusBuildDefinition = definition;
                    radialMenu.ShowUpgradeChoices(upgrades, SelectUpgrade);
                    return;
                }
                Debug.LogError(
                    $"Bonus tile {tileIndex} has no matching tier 2 upgrade data.");
            }

            Session.BuildTower(tileIndex, definition);
            radialMenu.Hide();
            HideTileInformation();
            StartCoroutine(CompleteTowerBuildRoutine(tileIndex, definition, 1));
        }

        public void SelectUpgrade(TowerUpgradeDefinition upgrade)
        {
            if (State.CurrentPhase != TurnPhase.TowerSelection || upgrade == null) return;
            var tileIndex = State.Player.CurrentTileIndex;
            if (pendingBonusBuildDefinition != null &&
                pendingBonusBuildTile == tileIndex)
            {
                Session.BuildTower(tileIndex, pendingBonusBuildDefinition);
                Session.UpgradeTower(tileIndex, upgrade);
                var builtDefinition = pendingBonusBuildDefinition;
                var builtTier = State.Board.Tiles[tileIndex].Tower.UpgradeTier;
                pendingBonusBuildTile = -1;
                pendingBonusBuildDefinition = null;
                radialMenu.Hide();
                HideTileInformation();
                StartCoroutine(CompleteTowerBuildRoutine(
                    tileIndex,
                    builtDefinition,
                    builtTier));
                return;
            }

            Session.UpgradeTower(tileIndex, upgrade);
            var tile = State.Board.Tiles[tileIndex];
            var definition = FindTowerDefinition(tile.Tower.DefinitionId);
            radialMenu.Hide();
            HideTileInformation();
            StartCoroutine(CompleteTowerUpgradeRoutine(
                tileIndex,
                definition,
                tile.Tower.UpgradeTier));
        }

        private IEnumerator CompleteTowerBuildRoutine(
            int tileIndex,
            TowerDefinition definition,
            int tier)
        {
            if (towerPresenter != null && definition != null)
                yield return towerPresenter.PlayBuildAnimation(tileIndex, definition, tier);
            yield return CompleteTileActionRoutine();
        }

        private IEnumerator CompleteTowerUpgradeRoutine(
            int tileIndex,
            TowerDefinition definition,
            int tier)
        {
            if (towerPresenter != null && definition != null)
                yield return towerPresenter.PlayUpgradeAnimation(tileIndex, definition, tier);
            yield return CompleteTileActionRoutine();
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
            var tower = FindTowerDefinition(tile.Tower.DefinitionId);
            if (tower == null)
                return new List<TowerUpgradeDefinition>();
            var targetTier = GetUpgradeTargetTier(tile);
            return GetUpgradeChoices(
                tower,
                targetTier,
                tile.Tower.AppliedUpgradeIds);
        }

        private List<TowerUpgradeDefinition> GetUpgradeChoices(
            TowerDefinition tower,
            int targetTier,
            ICollection<string> appliedUpgradeIds)
        {
            var result = new List<TowerUpgradeDefinition>();
            if (tower == null || targetTier < 0)
                return result;
            var pool = new List<TowerUpgradeDefinition>();
            foreach (var upgrade in towerUpgradeDefinitions)
                if (upgrade != null && upgrade.Element == tower.Element &&
                    upgrade.Tier == targetTier &&
                    (appliedUpgradeIds == null ||
                     !appliedUpgradeIds.Contains(upgrade.Id)))
                    pool.Add(upgrade);
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

        private static int GetUpgradeTargetTier(TileState tile)
        {
            if (tile == null || !tile.HasTower)
                return -1;
            if (tile.Tower.UpgradeTier < 3)
                return tile.Tower.UpgradeTier + 1;
            if (tile.IsBonusTile && tile.Tower.UpgradeTier == 3 &&
                !tile.Tower.BonusTier3UpgradeClaimed)
                return 3;
            return -1;
        }

        private void PopulateConsoleUpgradeChoices(
            TileState tile,
            TowerDefinition towerDefinition)
        {
            var targetTier = GetUpgradeTargetTier(tile);
            PopulateConsoleUpgradeChoices(
                towerDefinition,
                targetTier,
                tile?.Tower?.AppliedUpgradeIds);
        }

        private void PopulateConsoleUpgradeChoices(
            TowerDefinition towerDefinition,
            int targetTier,
            ICollection<string> appliedUpgradeIds)
        {
            pendingConsoleUpgrades.Clear();
            if (targetTier < 0 || towerDefinition == null)
                return;

            foreach (var upgrade in towerUpgradeDefinitions)
                if (upgrade != null &&
                    upgrade.Element == towerDefinition.Element &&
                    upgrade.Tier == targetTier &&
                    (appliedUpgradeIds == null ||
                     !appliedUpgradeIds.Contains(upgrade.Id)))
                    pendingConsoleUpgrades.Add(upgrade);
        }

        private string BuildConsoleUpgradePrompt()
        {
            var builder = new StringBuilder("적용할 강화를 숫자로 입력하세요.");
            for (var index = 0; index < pendingConsoleUpgrades.Count; index++)
                builder.Append($"\n{index} : {pendingConsoleUpgrades[index].Description}");
            return builder.ToString();
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
            yield return CompleteTileActionAfterOverviewRoutine();
        }

        private IEnumerator CompleteTileActionAfterOverviewRoutine()
        {
            if (pendingDiceTuning)
            {
                pendingDiceTuning = false;
                diceTuningComplete = false;
                State.CurrentPhase = TurnPhase.DiceTuning;
                var diceSystem = diceHud.GetComponent<DiceSystemView>();
                diceSystem.ShowLapReward(Session.CreateLapReward(), () => diceTuningComplete = true);
                yield return new WaitUntil(() => diceTuningComplete);
            }
            if (turnTransitionBanner != null)
                yield return turnTransitionBanner.PlayEnemyTurn();
            yield return ResolveEnemyTurnRoutine();
        }

        private IEnumerator CompleteFullUpgradeRewardRoutine(TowerElement element)
        {
            State.CurrentPhase = TurnPhase.CameraOverview;
            yield return cameraController.ReturnToOverview();
            yield return PlayElementTowerEnhancementEffect(element);
            yield return CompleteTileActionAfterOverviewRoutine();
        }

        private IEnumerator PlayElementTowerEnhancementEffect(TowerElement element)
        {
            if (towerPresenter == null || State?.Board?.Tiles == null)
                yield break;

            var tileIndices = new List<int>();
            foreach (var tile in State.Board.Tiles)
            {
                if (!tile.HasTower)
                    continue;

                var definition = FindTowerDefinition(tile.Tower.DefinitionId);
                if (definition == null || definition.Element != element)
                    continue;

                tileIndices.Add(tile.Index);
            }

            if (tileIndices.Count > 0)
                yield return towerPresenter.PlayOverUpgradeAnimation(tileIndices, element);
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
                CommitCapturedKills(ref killedCount);
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
                        EnterFlowState(GameFlowState.Boss);
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
                    yield return monsterPresenter.SpawnWithEntrance(spawnedMonster);
            }

            if (State.IsGameOver)
            {
                CommitCapturedKills(ref killedCount);
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
                CommitCapturedKills(ref killedCount);
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
                var areaResults = new List<TowerAttackResult>();
                while (attackIndex < attackResults.Count &&
                       attackResults[attackIndex].VisualKind == TowerAttackVisualKind.AreaTile &&
                       attackResults[attackIndex].TowerInstanceId == areaTowerId)
                {
                    var areaResult = attackResults[attackIndex];
                    areaTiles.Add(areaResult.TargetTileIndex);
                    areaResults.Add(areaResult);
                    attackIndex++;
                }
                attackIndex--;
                if (attackEffectPresenter != null)
                    yield return attackEffectPresenter.PlayAreaTiles(
                        State,
                        areaTowerId,
                        areaTiles,
                        () =>
                        {
                            foreach (var result in areaResults)
                                ApplyAttackTileVisualChanges(result);
                        });
                else
                    foreach (var result in areaResults)
                        ApplyAttackTileVisualChanges(result);
            }
            foreach (var attackResult in attackResults)
                if (attackResult.Killed) killedCount++;
            Session.CollectKillRewards(attackResults);

            if (State.IsVictory)
            {
                CommitCapturedKills(ref killedCount);
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
            foreach (var statusResult in statusResults)
                if (statusResult.Killed) killedCount++;
            Session.CollectKillRewards(statusResults);
            CommitCapturedKills(ref killedCount);

            if (State.IsVictory)
            {
                boardView.RefreshTileEffects(State.Board);
                FinishVictory();
                yield break;
            }

            Session.CompleteRound();
            diceHud.RefreshDiceFaces();

            RefreshOpenTileInformation();
            if (turnTransitionBanner != null)
                yield return turnTransitionBanner.PlayPlayerTurn();
            diceHud.BeginPlayerTurn();
            isBusy = false;
        }

        private void CommitCapturedKills(ref int killedCount)
        {
            if (killedCount > 0)
                difficultyService.AddKills(State.Difficulty, killedCount);
            killedCount = 0;
            diceHud.RefreshDifficulty();
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
            var audio = AudioManager.Instance;
            audio?.PlaySfx(audio.GameFlow.Victory);
            EnterFlowState(GameFlowState.Victory);
            if (gameFlowView != null)
                yield return gameFlowView.PlayOutro();
            diceHud.ShowGameClear();
            gameFlowView?.ShowVictory();
            isBusy = false;
        }

        private IEnumerator FinishDefeatRoutine()
        {
            var audio = AudioManager.Instance;
            audio?.PlaySfx(audio.GameFlow.Defeat);
            EnterFlowState(GameFlowState.Defeat);
            if (gameFlowView != null)
                yield return gameFlowView.PlayOutro();
            diceHud.ShowGameOver(State.EscapedMonsterCount, State.EscapeLimit);
            gameFlowView?.ShowDefeat();
            isBusy = false;
        }

        private void EnterFlowState(GameFlowState state)
        {
            if (currentFlowState == state) return;
            currentFlowState = state;

            var audio = AudioManager.Instance;
            if (audio == null) return;
            var clip = state switch
            {
                GameFlowState.Title => audio.Bgm.Title,
                GameFlowState.Gameplay => audio.Bgm.Gameplay,
                GameFlowState.Boss => audio.Bgm.Boss,
                GameFlowState.Victory => audio.Bgm.Victory,
                GameFlowState.Defeat => audio.Bgm.Defeat,
                _ => null
            };
            audio.PlayBgm(clip);
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
                    ApplyAttackTileVisualChanges(result);
                });
            if (!impactApplied)
            {
                monsterPresenter.ApplyAttackAtImpact(result);
                ApplyAttackTileVisualChanges(result);
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
                    {
                        monsterPresenter.ApplyAttackAtImpact(result);
                        ApplyAttackTileVisualChanges(result);
                    }
                });
            if (!impactApplied)
            {
                foreach (var result in results)
                {
                    monsterPresenter.ApplyAttackAtImpact(result);
                    ApplyAttackTileVisualChanges(result);
                }
            }
            foreach (var result in results)
                yield return monsterPresenter.CompleteAttack(result);
        }
    

private void ApplyAttackTileVisualChanges(TowerAttackResult result)
        {
            if (boardView == null || result.TileEffectVisualChanges == null)
                return;

            foreach (var change in result.TileEffectVisualChanges)
            {
                boardView.ApplyTileEffectVisual(change.TileIndex, change.Effect);
                var audio = AudioManager.Instance;
                if (audio == null) continue;
                var clip = change.Effect switch
                {
                    TileEffectVisualKind.Fire => audio.Status.FireTile,
                    TileEffectVisualKind.Ice => audio.Status.IceTile,
                    TileEffectVisualKind.Normal => audio.Status.TileCancel,
                    _ => null
                };
                audio.PlayAt(clip, boardView.GetWorldPosition(change.TileIndex));
            }
        }
}
}
