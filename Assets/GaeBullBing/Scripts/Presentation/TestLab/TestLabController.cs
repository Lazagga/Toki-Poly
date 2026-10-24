using System;
using System.Collections.Generic;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GaeBullBing.Presentation.TestLab
{
    public sealed partial class TestLabController : MonoBehaviour
    {
        private GameController game;
        private TowerDefinition[] towers = Array.Empty<TowerDefinition>();
        private TowerUpgradeDefinition[] upgrades = Array.Empty<TowerUpgradeDefinition>();
        private MonsterDefinition[] monsters = Array.Empty<MonsterDefinition>();
        private DiceDefinition[] dice = Array.Empty<DiceDefinition>();
        private RegionDefinition[] regions = Array.Empty<RegionDefinition>();
        private int regionIndex;
        private int oasisSourceIndex;
        private readonly List<string> log = new();
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private Vector2 logScroll;
        private bool[] selectedUpgrades = Array.Empty<bool>();
        private int towerIndex;
        private int towerTile = 4;
        private string towerTileText = "4";
        private bool bonusTile;
        private int attackCount = 1;
        private string attackCountText = "1";
        private bool ignoreRange = true;
        private int monsterIndex;
        private int monsterTile = 6;
        private string monsterTileText = "6";
        private bool stationary = true;
        private int selectedMonsterId;
        private int moveDistance = 1;
        private string moveDistanceText = "1";
        private int tileEffectTile = 6;
        private string tileEffectTileText = "6";
        private int firstDiceIndex;
        private int secondDiceIndex = 1;
        // TestLab은 맵 확인이 우선이므로 처음에는 조작 패널을 접어 둔다.
        private bool collapsed = true;
        private bool inspectorVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!SceneManager.GetActiveScene().name.Equals(
                    "TestLab", StringComparison.OrdinalIgnoreCase)) return;
            if (FindFirstObjectByType<TestLabController>() != null) return;
            new GameObject("Test Lab Controller").AddComponent<TestLabController>();
        }

        private void Start()
        {
            game = FindFirstObjectByType<GameController>();
            if (game == null)
            {
                AddLog("게임 제어기를 찾을 수 없습니다.");
                return;
            }
            // GameFlowView가 타이틀 상태를 준비하면서 보드를 화면 위로 숨긴다.
            // TestLab은 일반 등장 연출을 실행하지 않으므로 최종 위치를 직접 확정한다.
            FindFirstObjectByType<GameBoardTransition>()?.ShowImmediately();
            game.StartTestLabMode();
            towers = ToArray(game.TestTowerDefinitions);
            upgrades = ToArray(game.TestTowerUpgradeDefinitions);
            monsters = ToArray(game.TestMonsterDefinitions);
            regions = ToArray(game.TestRegionDefinitions);
            var database = Resources.Load<DiceDatabaseDefinition>("GaeBullBing/DiceDatabase");
            dice = database != null && database.Dice != null
                ? database.Dice
                : Array.Empty<DiceDefinition>();
            selectedUpgrades = new bool[upgrades.Length];
            firstDiceIndex = Mathf.Clamp(firstDiceIndex, 0, Mathf.Max(0, dice.Length - 1));
            secondDiceIndex = Mathf.Clamp(secondDiceIndex, 0, Mathf.Max(0, dice.Length - 1));
            AddLog("테스트랩 준비 완료. 실제 게임 상태와 JSON 데이터를 사용합니다.");
        }

        private void DrawLabGui()
        {
            if (game?.State == null)
            {
                GUI.Box(new Rect(8, 8, 440, 60), "테스트랩을 사용할 수 없습니다. 필수 JSON 데이터와 콘솔을 확인하세요.");
                return;
            }
            if (collapsed)
            {
                if (GUI.Button(new Rect(8, 8, 180, 32), executing ? "테스트랩 실행 중..." : "테스트랩 열기")) collapsed = false;
                return;
            }
            // 긴 강화 설명과 주사위 선택지가 가로 스크롤을 만들지 않도록 충분한 폭을 확보한다.
            var width = Mathf.Min(660f, Screen.width * .48f);
            DrawPanelBackground(new Rect(0, 0, width, Screen.height));
            if (GUI.Button(new Rect(width - 142, 6, 64, 26),
                    inspectorVisible ? "상태 닫기" : "상태 보기"))
                inspectorVisible = !inspectorVisible;
            if (GUI.Button(new Rect(width - 72, 6, 64, 26), "맵만 보기")) collapsed = true;
            GUILayout.BeginArea(new Rect(10, 8, width - 20, Screen.height - 16));
            GUILayout.Label("동적 테스트랩", HeaderStyle());
            GUILayout.Label(TestLabKoreanText.Translate(actionStatus), WrapStyle());
            GUI.enabled = !executing && game?.State != null && !showTrace;
            var newTab = GUILayout.SelectionGrid(labTab, new[] { "타워 설정", "공격 / 효과", "몬스터", "타일", "주사위", "지역 / 초기화" }, 3);
            if (newTab != labTab) { labTab = newTab; leftScroll = Vector2.zero; }
            leftScroll = GUILayout.BeginScrollView(
                leftScroll,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.Height(Screen.height * .63f));
            switch (labTab)
            {
                case 0: DrawTowerSection(); break;
                case 1: DrawAttackSection(); break;
                case 2: DrawMonsterSection(); break;
                case 3: DrawTileSection(); break;
                case 4: DrawDiceSection(); break;
                case 5: DrawRegionSection(); break;
            }
            GUILayout.EndScrollView();
            GUI.enabled = true;
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("실행 기록", SectionStyle());
            if (GUILayout.Button("상세 전투 기록", GUILayout.Width(128))) showTrace = true;
            if (GUILayout.Button("기록 지우기", GUILayout.Width(80)) && !executing) log.Clear();
            GUILayout.EndHorizontal();
            logScroll = GUILayout.BeginScrollView(logScroll, GUI.skin.box,
                GUILayout.Height(Screen.height * .2f));
            foreach (var entry in log) GUILayout.Label(entry, WrapStyle());
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (inspectorVisible) DrawInspector(width);
            if (showTrace) DrawTraceWindow();
        }

        private void DrawRegionSection()
        {
            GUILayout.Label("스테이지 / 지역", SectionStyle());
            if (regions.Length == 0) { GUILayout.Label("지역 데이터가 없습니다."); return; }
            var names = new string[regions.Length];
            for (var i = 0; i < regions.Length; i++) names[i] = $"{regions[i].Order}: {regions[i].DisplayName}";
            regionIndex = GUILayout.SelectionGrid(regionIndex, names, 2);
            GUILayout.Label(regions[regionIndex].Description, WrapStyle());
            GUILayout.Label($"현재 지역: {game.State.CurrentRegionId} / 수풀 {game.State.Board.Tiles.FindAll(t => t.HasOvergrowth).Count}개", WrapStyle());
            if (GUILayout.Button("지역 적용 / 테스트랩 초기화", GUILayout.Height(28)))
                RunSync("지역 적용 / 테스트랩 초기화", () => {
                    if (!game.TestLabSetRegion(regions[regionIndex].Id, out var message)) throw new InvalidOperationException(message);
                    monsterEdit = null; towerEdit = null; AddLog(message);
                });
            DrawRegionGimmickTests();
        }

        private void DrawTowerSection()
        {
            GUILayout.Label("타워", SectionStyle());
            if (towers.Length == 0) { GUILayout.Label("타워 데이터가 없습니다."); return; }
            DrawIntField("타일 번호", ref towerTile, ref towerTileText, 0, 35);
            DrawMapPicking();
            if (!SyncTowerSelectionToTile())
            {
                GUILayout.Label("이 타일에는 타워를 건설할 수 없습니다.", WrapStyle());
                return;
            }
            GUILayout.Label(
                $"건설할 타워: {towers[towerIndex].DisplayName} ({TestLabKoreanText.Element(towers[towerIndex].Element)})",
                AutoTowerStyle());
            bonusTile = GUILayout.Toggle(bonusTile, "보너스 타일 (3티어 강화 2개 선택 가능)");
            var tier3Limit = (bonusTile ? 2 : 1) +
                (towers[towerIndex].Element == GaeBullBing.Core.TowerElement.Electric &&
                 game.State.Dice.Exists(d => d != null && d.HasEffect("electric_maxtier_extra_upgrade_choice")) ? 1 : 0);
            TrimUpgradeSelections(3, tier3Limit);
            GUILayout.BeginHorizontal();
            GUILayout.Label("강화 선택", SectionStyle());
            var selectedCount = 0;
            for (var index = 0; index < selectedUpgrades.Length; index++)
                if (selectedUpgrades[index]) selectedCount++;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"선택: {selectedCount}개", GUILayout.Width(82));
            if (GUILayout.Button("선택 해제", GUILayout.Width(58)))
                Array.Clear(selectedUpgrades, 0, selectedUpgrades.Length);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("검색", GUILayout.Width(60));
            upgradeSearch = GUILayout.TextField(upgradeSearch);
            if (GUILayout.Button("X", GUILayout.Width(30))) upgradeSearch = "";
            GUILayout.EndHorizontal();
            DrawSelectedUpgradeSummary();

            DrawUpgradeTier(2, 1);
            DrawUpgradeTier(3, tier3Limit);

            if (GUILayout.Button("타워 생성 / 교체", GUILayout.Height(28)))
            {
                var ids = new List<string>();
                for (var index = 0; index < selectedUpgrades.Length; index++)
                    if (selectedUpgrades[index]) ids.Add(upgrades[index].Id);
                RunSync("타워 생성 / 교체", () => {
                    RequireTileInput(towerTileText);
                    if (!game.TestLabConfigureTower(towerTile, towers[towerIndex].Id, ids, bonusTile, out var message))
                        throw new InvalidOperationException(message);
                    towerEdit = null; AddLog(message);
                });
            }
            DrawTowerStateEditor();
        }

        private void DrawUpgradeTier(int tier, int selectionLimit)
        {
            var hasChoice = false;
            var selectedInTier = 0;
            for (var index = 0; index < upgrades.Length; index++)
                if (selectedUpgrades[index] && upgrades[index] != null &&
                    upgrades[index].Tier == tier) selectedInTier++;

            for (var index = 0; index < upgrades.Length; index++)
            {
                var upgrade = upgrades[index];
                if (upgrade == null || upgrade.Tier != tier ||
                    upgrade.Element != towers[towerIndex].Element || upgrade.Weight <= 0) continue;
                if (!MatchesUpgradeSearch(upgrade)) continue;

                if (!hasChoice)
                {
                    GUILayout.Label($"{tier}티어", UpgradeTierStyle());
                    hasChoice = true;
                }

                var selected = selectedUpgrades[index];
                var warning = UpgradeWarning(upgrade);
                var label = (selected ? "[선택됨] " : "[ ] ") + upgrade.DisplayName + "\n" + upgrade.Description +
                    (warning.Length == 0 ? "" : "\n안내: " + warning);
                var style = selected ? SelectedUpgradeStyle() : UpgradeStyle();
                var contentWidth = Mathf.Min(660f, Screen.width * .48f) - 62f;
                var height = Mathf.Max(52f, style.CalcHeight(new GUIContent(label), contentWidth) + 12f);
                if (!GUILayout.Button(label, style, GUILayout.Height(height))) continue;

                if (selected)
                {
                    selectedUpgrades[index] = false;
                    selectedInTier--;
                    continue;
                }

                if (selectedInTier >= selectionLimit)
                {
                    AddLog($"{tier}티어는 최대 {selectionLimit}개까지 선택할 수 있습니다.");
                    continue;
                }

                selectedUpgrades[index] = true;
                selectedInTier++;
            }
        }

        private void TrimUpgradeSelections(int tier, int limit)
        {
            var kept = 0;
            for (var index = 0; index < selectedUpgrades.Length; index++)
            {
                if (!selectedUpgrades[index] || upgrades[index] == null ||
                    upgrades[index].Tier != tier) continue;
                if (kept++ < limit) continue;
                selectedUpgrades[index] = false;
            }
        }

        private bool SyncTowerSelectionToTile()
        {
            if (game?.State?.Board == null || towerTile < 0 ||
                towerTile >= game.State.Board.TileCount) return false;

            var definitionId = game.State.Board.Tiles[towerTile].BuildTowerDefinitionId;
            if (string.IsNullOrEmpty(definitionId)) return false;
            for (var index = 0; index < towers.Length; index++)
            {
                if (towers[index] == null || !string.Equals(
                        towers[index].Id, definitionId, StringComparison.Ordinal)) continue;
                if (towerIndex != index)
                {
                    towerIndex = index;
                    Array.Clear(selectedUpgrades, 0, selectedUpgrades.Length);
                }
                return true;
            }
            return false;
        }

        private void DrawMonsterSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("몬스터 / 보스", SectionStyle());
            if (monsters.Length == 0) { GUILayout.Label("몬스터 데이터가 없습니다."); return; }
            monsterIndex = GUILayout.SelectionGrid(monsterIndex, Names(monsters), 2);
            DrawIntField("소환 타일", ref monsterTile, ref monsterTileText, 0, 35);
            stationary = GUILayout.Toggle(stationary, "소환 후 정지 상태 유지");
            if (GUILayout.Button("몬스터 소환", GUILayout.Height(28)))
            {
                RunAction("몬스터 소환", () => {
                    RequireTileInput(monsterTileText);
                    return game.TestLabSpawnWithPresentation(monsters[monsterIndex].Id, monsterTile, stationary,
                        id => { selectedMonsterId = id; monsterEdit = null; });
                });
            }
            DrawMonsterSelector();
            DrawMonsterStateEditor();
            DrawIntField("이동 칸 수", ref moveDistance, ref moveDistanceText, 1, 35);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1칸 이동")) MoveSelected(1);
            if (GUILayout.Button("지정 칸 이동"))
            {
                if (int.TryParse(moveDistanceText, out var distance) && distance >= 1 && distance <= 35) MoveSelected(distance);
                else ReportFailure("이동 거리를 1~35칸으로 입력하세요.");
            }
            if (GUILayout.Button("보스 다음 패턴")) MoveSelected(1);
            GUILayout.EndHorizontal();
        }

        private void DrawTileSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("타일 효과", SectionStyle());
            DrawIntField("타일 번호", ref tileEffectTile, ref tileEffectTileText, 0, 35);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("불 장판 설치")) SetTileEffect("ignite");
            if (GUILayout.Button("얼음 장판 설치")) SetTileEffect("frozen");
            GUILayout.EndHorizontal();
            GUILayout.Label("몬스터의 ‘1칸 이동 / 지정 칸 이동’으로 진입·통과·도착 효과를 시험할 수 있습니다.", WrapStyle());
        }

        private void DrawDiceSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("주사위", SectionStyle());
            if (dice.Length == 0) { GUILayout.Label("주사위 데이터가 없습니다."); return; }
            var names = Names(dice);
            GUILayout.Label("장착 주사위 1", UpgradeTierStyle());
            firstDiceIndex = GUILayout.SelectionGrid(
                firstDiceIndex, names, Mathf.Min(3, names.Length));
            GUILayout.Label("장착 주사위 2", UpgradeTierStyle());
            secondDiceIndex = GUILayout.SelectionGrid(
                secondDiceIndex, names, Mathf.Min(3, names.Length));
            if (GUILayout.Button("선택한 주사위 장착", GUILayout.Height(30)))
            {
                RunSync("선택한 주사위 장착", () => {
                    if (!game.TestLabSetDice(dice[firstDiceIndex].Id, dice[secondDiceIndex].Id, out var message))
                        throw new InvalidOperationException(message);
                    AddLog(message);
                });
            }
#if UNITY_EDITOR
            if (GUILayout.Button("JSON 데이터 다시 불러오기"))
            {
                RunSync("JSON 데이터 다시 불러오기", () => {
                    UnityEditor.EditorApplication.ExecuteMenuItem("GaeBullBing/Data/Import Tiles and Towers JSON");
                    UnityEditor.EditorApplication.ExecuteMenuItem("GaeBullBing/Data/Import Monsters JSON");
                    UnityEditor.EditorApplication.ExecuteMenuItem("GaeBullBing/Data/Import Dice JSON");
                    AddLog("JSON 재가져오기를 요청했습니다. 새 데이터를 반영하려면 테스트랩 실행을 다시 시작하세요.");
                });
            }
#endif
        }

        private void DrawInspector(float leftWidth)
        {
            var width = Mathf.Min(370f, Screen.width * .28f);
            var x = Screen.width - width;
            DrawPanelBackground(new Rect(x, 0, width, Screen.height));
            GUILayout.BeginArea(new Rect(x + 10, 10, width - 20, Screen.height - 20));
            GUILayout.Label("실시간 상태", HeaderStyle());
            rightScroll = GUILayout.BeginScrollView(rightScroll);
            if (game?.State != null)
            {
                if (towerTile >= 0 && towerTile < game.State.Board.TileCount)
                {
                    var tile = game.State.Board.Tiles[towerTile];
                    GUILayout.Label($"타일 {towerTile}", SectionStyle());
                    GUILayout.Label($"불 장판: {tile.FireTurnsRemaining} / 얼음 장판: {tile.IceTurnsRemaining}");
                    GUILayout.Label($"수풀 {tile.OvergrowthTurnsRemaining}턴 / 건설 제한 {TestLabKoreanText.YesNo(tile.IsBuildRestricted)} / 강화 제한 {TestLabKoreanText.YesNo(tile.IsUpgradeRestricted)}", WrapStyle());
                    if (tile.HasTower)
                    {
                        var tower = tile.Tower;
                        GUILayout.Label($"타워: {tower.DefinitionId}  {tower.UpgradeTier}티어");
                        var calculation = game.TestLabTowerStats(towerTile);
                        if (calculation.HasValue)
                            GUILayout.Label($"공격력: {calculation.Value.DamageFormula} = {calculation.Value.CombatStats.Damage}", WrapStyle());
                        GUILayout.Label($"쿨다운 {tower.AttackCooldownRounds}, 충전 {tower.ChargeCount}, 과열 {tower.OverheatAttackTurns}, 주기 {tower.PeriodicTurnCount}", WrapStyle());
                        GUILayout.Label("적용된 강화: " + string.Join(", ", tower.AppliedUpgradeIds), WrapStyle());
                        GUILayout.Label("적용된 효과: " + string.Join(", ", tower.AppliedEffectIds), WrapStyle());
                    }
                }
                var selected = FindMonster(selectedMonsterId);
                if (selected != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label($"몬스터 #{selected.InstanceId}", SectionStyle());
                    GUILayout.Label($"{MonsterName(selected.DefinitionId)} / 타일 {selected.CurrentTileIndex}");
                    GUILayout.Label($"체력 {selected.CurrentHealth:0.##} / {selected.MaxHealth:0.##}");
                    GUILayout.Label($"이동력 {selected.MoveDistance} / 누적 이동거리 {selected.DistanceTravelled}");
                    GUILayout.Label($"화상 {selected.BurnStacks} / 동상 {selected.FrostbiteStacks}");
                    GUILayout.Label($"빙결 {selected.FrozenMovesRemaining} / 감전 {TestLabKoreanText.YesNo(selected.Shocked)}");
                    GUILayout.Label($"방어력 {selected.GetDefense(game.State.Difficulty):0.##}, 감속 {selected.NextMoveReduction}, 기절 {selected.StunnedMovesRemaining}");
                    GUILayout.Label($"피해 저장 {TestLabKoreanText.YesNo(selected.DamageStoreMarked)}: {selected.StoredDamage:0.##}");
                    GUILayout.Label($"빙결 면역 현재/대기: {TestLabKoreanText.YesNo(selected.FreezeImmuneThisTurn)}/{TestLabKoreanText.YesNo(selected.FreezeImmunityPending)}", WrapStyle());
                    GUILayout.Label($"넉백 면역 현재/대기: {TestLabKoreanText.YesNo(selected.KnockbackConsumed)}/{TestLabKoreanText.YesNo(selected.KnockbackImmunityPending)}", WrapStyle());
                    GUILayout.Label($"보스 {TestLabKoreanText.YesNo(selected.IsBoss)} / 면역 {TestLabKoreanText.StatusNames(selected.StatusImmunities)}", WrapStyle());
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private string MonsterName(string id)
        {
            foreach (var definition in monsters)
                if (definition != null && definition.Id == id) return definition.DisplayName;
            return id;
        }

        private void DrawMonsterSelector()
        {
            if (game?.State == null || game.State.Monsters.Count == 0)
            {
                GUILayout.Label("소환된 몬스터가 없습니다.");
                selectedMonsterId = 0;
                return;
            }
            var labels = new string[game.State.Monsters.Count];
            var selected = 0;
            for (var index = 0; index < labels.Length; index++)
            {
                var monster = game.State.Monsters[index];
                labels[index] = $"#{monster.InstanceId} {MonsterName(monster.DefinitionId)} / 타일 {monster.CurrentTileIndex}";
                if (monster.InstanceId == selectedMonsterId) selected = index;
            }
            selected = GUILayout.SelectionGrid(selected, labels, 1);
            selectedMonsterId = game.State.Monsters[selected].InstanceId;
        }

        private void MoveSelected(int distance)
        {
            if (selectedMonsterId <= 0) return;
            RunAction($"몬스터 #{selectedMonsterId}: {distance}칸 이동",
                () => game.TestLabMoveMonster(selectedMonsterId, distance));
        }

        private void SetTileEffect(string effect)
        {
            RunAction("타일 효과 설치: " + (effect == "ignite" ? "불" : "얼음"), () => {
                RequireTileInput(tileEffectTileText);
                return game.TestLabPlaceField(tileEffectTile, effect);
            });
        }

        private MonsterState FindMonster(int id) =>
            game?.State?.Monsters.Find(monster => monster.InstanceId == id);

        private void AddLog(string value)
        {
            log.Add($"[{DateTime.Now:HH:mm:ss}] {TestLabKoreanText.Translate(value)}");
            if (log.Count > 100) log.RemoveAt(0);
            logScroll.y = float.MaxValue;
        }

        private static void DrawIntField(
            string label,
            ref int value,
            ref string editText,
            int min,
            int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(72));
            editText = GUILayout.TextField(editText, GUILayout.Width(52));
            if (int.TryParse(editText, out var parsed))
            {
                value = Mathf.Clamp(parsed, min, max);
                if (value != parsed) editText = value.ToString();
            }
            GUILayout.EndHorizontal();
        }

        private static string[] Names<T>(IReadOnlyList<T> values) where T : UnityEngine.Object
        {
            var result = new string[values.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = values[index] switch
                {
                    MonsterDefinition monster => monster.DisplayName,
                    DiceDefinition die => die.DisplayName,
                    TowerDefinition tower => tower.DisplayName,
                    RegionDefinition region => region.DisplayName,
                    null => "(없음)",
                    _ => values[index].name
                };
            return result;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> source)
        {
            if (source == null) return Array.Empty<T>();
            var result = new T[source.Count];
            for (var index = 0; index < result.Length; index++) result[index] = source[index];
            return result;
        }

        private static GUIStyle HeaderStyle() => new(GUI.skin.label)
        {
            fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };

        private static GUIStyle SectionStyle() => new(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Bold
        };

        private static GUIStyle WrapStyle() => new(GUI.skin.label) { wordWrap = true };

        private static GUIStyle UpgradeTierStyle() => new(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            margin = new RectOffset(4, 4, 6, 2)
        };

        private static GUIStyle UpgradeStyle() => new(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            padding = new RectOffset(12, 10, 10, 10)
        };

        private static GUIStyle AutoTowerStyle() => new(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(10, 10, 7, 7),
            margin = new RectOffset(0, 0, 3, 5)
        };

        private static GUIStyle SelectedUpgradeStyle()
        {
            var style = UpgradeStyle();
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(.35f, 1f, .45f);
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            return style;
        }
    }
}
