using System;
using System.Collections.Generic;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Presentation.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GaeBullBing.Presentation.TestLab
{
    public sealed class TestLabController : MonoBehaviour
    {
        private GameController game;
        private TowerDefinition[] towers = Array.Empty<TowerDefinition>();
        private TowerUpgradeDefinition[] upgrades = Array.Empty<TowerUpgradeDefinition>();
        private MonsterDefinition[] monsters = Array.Empty<MonsterDefinition>();
        private DiceDefinition[] dice = Array.Empty<DiceDefinition>();
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
        private string health = "100";
        private string burn = "0";
        private string frostbite = "0";
        private string freeze = "0";
        private bool shocked;
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
                AddLog("GameController not found.");
                return;
            }
            // GameFlowView가 타이틀 상태를 준비하면서 보드를 화면 위로 숨긴다.
            // TestLab은 일반 등장 연출을 실행하지 않으므로 최종 위치를 직접 확정한다.
            FindFirstObjectByType<GameBoardTransition>()?.ShowImmediately();
            game.StartTestLabMode();
            towers = ToArray(game.TestTowerDefinitions);
            upgrades = ToArray(game.TestTowerUpgradeDefinitions);
            monsters = ToArray(game.TestMonsterDefinitions);
            var database = Resources.Load<DiceDatabaseDefinition>("GaeBullBing/DiceDatabase");
            dice = database != null && database.Dice != null
                ? database.Dice
                : Array.Empty<DiceDefinition>();
            selectedUpgrades = new bool[upgrades.Length];
            firstDiceIndex = Mathf.Clamp(firstDiceIndex, 0, Mathf.Max(0, dice.Length - 1));
            secondDiceIndex = Mathf.Clamp(secondDiceIndex, 0, Mathf.Max(0, dice.Length - 1));
            AddLog("Test Lab ready. All actions use the live game state and JSON assets.");
        }

        private void OnGUI()
        {
            if (collapsed)
            {
                if (GUI.Button(new Rect(8, 8, 120, 32), "Open Test Lab")) collapsed = false;
                return;
            }
            // 긴 강화 설명과 주사위 선택지가 가로 스크롤을 만들지 않도록 충분한 폭을 확보한다.
            var width = Mathf.Min(660f, Screen.width * .48f);
            GUI.Box(new Rect(0, 0, width, Screen.height), "");
            if (GUI.Button(new Rect(width - 142, 6, 64, 26),
                    inspectorVisible ? "State On" : "State Off"))
                inspectorVisible = !inspectorVisible;
            if (GUI.Button(new Rect(width - 72, 6, 64, 26), "Map View")) collapsed = true;
            GUILayout.BeginArea(new Rect(10, 8, width - 20, Screen.height - 16));
            GUILayout.Label("DYNAMIC TEST LAB", HeaderStyle());
            leftScroll = GUILayout.BeginScrollView(
                leftScroll,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.Height(Screen.height * .72f));
            DrawTowerSection();
            DrawMonsterSection();
            DrawTileSection();
            DrawDiceSection();
            GUILayout.EndScrollView();
            GUILayout.Space(4);
            GUILayout.Label("Execution Log", SectionStyle());
            logScroll = GUILayout.BeginScrollView(logScroll, GUI.skin.box,
                GUILayout.Height(Screen.height * .2f));
            foreach (var entry in log) GUILayout.Label(entry, WrapStyle());
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (inspectorVisible) DrawInspector(width);
        }

        private void DrawTowerSection()
        {
            GUILayout.Label("Tower", SectionStyle());
            if (towers.Length == 0) { GUILayout.Label("No tower data."); return; }
            DrawIntField("Tile", ref towerTile, ref towerTileText, 0, 35);
            if (!SyncTowerSelectionToTile())
            {
                GUILayout.Label("This tile has no buildable tower property.", WrapStyle());
                return;
            }
            GUILayout.Label(
                $"Build tower: {towers[towerIndex].DisplayName} ({towers[towerIndex].Element})",
                AutoTowerStyle());
            bonusTile = GUILayout.Toggle(bonusTile, "Bonus tile (allows two tier-3 upgrades)");
            if (!bonusTile) TrimUpgradeSelections(3, 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Upgrades", SectionStyle());
            var selectedCount = 0;
            for (var index = 0; index < selectedUpgrades.Length; index++)
                if (selectedUpgrades[index]) selectedCount++;
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Selected: {selectedCount}", GUILayout.Width(82));
            if (GUILayout.Button("Clear", GUILayout.Width(58)))
                Array.Clear(selectedUpgrades, 0, selectedUpgrades.Length);
            GUILayout.EndHorizontal();

            DrawUpgradeTier(2, 1);
            DrawUpgradeTier(3, bonusTile ? 2 : 1);

            if (GUILayout.Button("Create / Replace Tower", GUILayout.Height(28)))
            {
                var ids = new List<string>();
                for (var index = 0; index < selectedUpgrades.Length; index++)
                    if (selectedUpgrades[index]) ids.Add(upgrades[index].Id);
                AddLog(game.TestLabConfigureTower(
                    towerTile, towers[towerIndex].Id, ids, bonusTile, out var message)
                    ? message : $"ERROR: {message}");
            }
            GUILayout.BeginHorizontal();
            DrawIntField("Attacks", ref attackCount, ref attackCountText, 1, 99);
            ignoreRange = GUILayout.Toggle(ignoreRange, "Ignore range");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Attack Now", GUILayout.Height(30)))
            {
                StartCoroutine(game.TestLabAttack(
                    towerTile, attackCount, selectedMonsterId, ignoreRange));
                AddLog($"Tower {towerTile}: requested {attackCount} attack(s)." );
            }
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

                if (!hasChoice)
                {
                    GUILayout.Label($"Tier {tier}", UpgradeTierStyle());
                    hasChoice = true;
                }

                var selected = selectedUpgrades[index];
                var label = selected
                    ? $"✓ SELECTED  {upgrade.Description}"
                    : $"○  {upgrade.Description}";
                if (!GUILayout.Button(label, selected ? SelectedUpgradeStyle() : UpgradeStyle(),
                        GUILayout.MinHeight(42))) continue;

                if (selected)
                {
                    selectedUpgrades[index] = false;
                    selectedInTier--;
                    continue;
                }

                if (selectedInTier >= selectionLimit)
                {
                    AddLog($"Tier-{tier} selection limit ({selectionLimit}) reached.");
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
            GUILayout.Label("Monster / Boss", SectionStyle());
            if (monsters.Length == 0) { GUILayout.Label("No monster data."); return; }
            monsterIndex = GUILayout.SelectionGrid(monsterIndex, Names(monsters), 2);
            DrawIntField("Spawn tile", ref monsterTile, ref monsterTileText, 0, 35);
            stationary = GUILayout.Toggle(stationary, "Stationary after spawn");
            if (GUILayout.Button("Spawn Monster", GUILayout.Height(28)))
            {
                if (game.TestLabSpawnMonster(monsters[monsterIndex].Id, monsterTile,
                        stationary, out var id, out var message))
                {
                    selectedMonsterId = id;
                    AddLog(message);
                }
                else AddLog($"ERROR: {message}");
            }
            DrawMonsterSelector();
            GUILayout.BeginHorizontal();
            GUILayout.Label("HP", GUILayout.Width(28)); health = GUILayout.TextField(health);
            GUILayout.Label("Burn", GUILayout.Width(34)); burn = GUILayout.TextField(burn);
            GUILayout.Label("Frost", GUILayout.Width(34)); frostbite = GUILayout.TextField(frostbite);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Freeze", GUILayout.Width(45)); freeze = GUILayout.TextField(freeze);
            shocked = GUILayout.Toggle(shocked, "Shock");
            if (GUILayout.Button("Apply State")) ApplyMonsterState();
            GUILayout.EndHorizontal();
            DrawIntField("Move", ref moveDistance, ref moveDistanceText, 1, 35);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Move 1")) MoveSelected(1);
            if (GUILayout.Button("Move N")) MoveSelected(moveDistance);
            if (GUILayout.Button("Boss Next Pattern")) MoveSelected(1);
            GUILayout.EndHorizontal();
        }

        private void DrawTileSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("Tile Effects", SectionStyle());
            DrawIntField("Tile", ref tileEffectTile, ref tileEffectTileText, 0, 35);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Place Fire")) SetTileEffect("ignite");
            if (GUILayout.Button("Place Ice")) SetTileEffect("frozen");
            GUILayout.EndHorizontal();
            GUILayout.Label("Use Move 1 / Move N to test tile entry, traversal and arrival with real movement logic.", WrapStyle());
        }

        private void DrawDiceSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("Dice", SectionStyle());
            if (dice.Length == 0) { GUILayout.Label("No dice data."); return; }
            var names = Names(dice);
            GUILayout.Label("Equipped A", UpgradeTierStyle());
            firstDiceIndex = GUILayout.SelectionGrid(
                firstDiceIndex, names, Mathf.Min(3, names.Length));
            GUILayout.Label("Equipped B", UpgradeTierStyle());
            secondDiceIndex = GUILayout.SelectionGrid(
                secondDiceIndex, names, Mathf.Min(3, names.Length));
            if (GUILayout.Button("Apply Equipped Dice", GUILayout.Height(30)))
            {
                AddLog(game.TestLabSetDice(
                    dice[firstDiceIndex].Id, dice[secondDiceIndex].Id, out var message)
                    ? message : $"ERROR: {message}");
            }
#if UNITY_EDITOR
            if (GUILayout.Button("Reload JSON Assets"))
            {
                UnityEditor.EditorApplication.ExecuteMenuItem(
                    "GaeBullBing/Data/Import Tiles and Towers JSON");
                UnityEditor.EditorApplication.ExecuteMenuItem(
                    "GaeBullBing/Data/Import Monsters JSON");
                UnityEditor.EditorApplication.ExecuteMenuItem(
                    "GaeBullBing/Data/Import Dice JSON");
                AddLog("Requested JSON reimport. Restart TestLab play mode to reload definitions.");
            }
#endif
        }

        private void DrawInspector(float leftWidth)
        {
            var width = Mathf.Min(370f, Screen.width * .28f);
            var x = Screen.width - width;
            GUI.Box(new Rect(x, 0, width, Screen.height), "");
            GUILayout.BeginArea(new Rect(x + 10, 10, width - 20, Screen.height - 20));
            GUILayout.Label("LIVE STATE", HeaderStyle());
            rightScroll = GUILayout.BeginScrollView(rightScroll);
            if (game?.State != null)
            {
                if (towerTile >= 0 && towerTile < game.State.Board.TileCount)
                {
                    var tile = game.State.Board.Tiles[towerTile];
                    GUILayout.Label($"Tile {towerTile}", SectionStyle());
                    GUILayout.Label($"Fire: {tile.FireTurnsRemaining} / Ice: {tile.IceTurnsRemaining}");
                    if (tile.HasTower)
                    {
                        var tower = tile.Tower;
                        GUILayout.Label($"Tower: {tower.DefinitionId}  Tier {tower.UpgradeTier}");
                        GUILayout.Label($"Last damage: {tower.LastResolvedDamage}");
                        GUILayout.Label("Upgrades: " + string.Join(", ", tower.AppliedUpgradeIds), WrapStyle());
                        GUILayout.Label("Effects: " + string.Join(", ", tower.AppliedEffectIds), WrapStyle());
                    }
                }
                var selected = FindMonster(selectedMonsterId);
                if (selected != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label($"Monster #{selected.InstanceId}", SectionStyle());
                    GUILayout.Label($"{selected.DefinitionId} / Tile {selected.CurrentTileIndex}");
                    GUILayout.Label($"HP {selected.CurrentHealth:0.##} / {selected.MaxHealth:0.##}");
                    GUILayout.Label($"Move {selected.MoveDistance} / Distance {selected.DistanceTravelled}");
                    GUILayout.Label($"Burn {selected.BurnStacks} / Frostbite {selected.FrostbiteStacks}");
                    GUILayout.Label($"Freeze {selected.FrozenMovesRemaining} / Shock {selected.Shocked}");
                    GUILayout.Label($"Boss {selected.IsBoss} / Immunities {string.Join(", ", selected.StatusImmunities)}", WrapStyle());
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMonsterSelector()
        {
            if (game?.State == null || game.State.Monsters.Count == 0)
            {
                GUILayout.Label("No active monsters.");
                selectedMonsterId = 0;
                return;
            }
            var labels = new string[game.State.Monsters.Count];
            var selected = 0;
            for (var index = 0; index < labels.Length; index++)
            {
                var monster = game.State.Monsters[index];
                labels[index] = $"#{monster.InstanceId} {monster.DefinitionId}@{monster.CurrentTileIndex}";
                if (monster.InstanceId == selectedMonsterId) selected = index;
            }
            selected = GUILayout.SelectionGrid(selected, labels, 1);
            selectedMonsterId = game.State.Monsters[selected].InstanceId;
        }

        private void ApplyMonsterState()
        {
            var monster = FindMonster(selectedMonsterId);
            if (monster == null) return;
            if (float.TryParse(health, out var hp)) monster.CurrentHealth = Mathf.Clamp(hp, 1f, monster.MaxHealth);
            if (int.TryParse(burn, out var burnValue)) monster.BurnStacks = burnValue;
            if (int.TryParse(frostbite, out var frostValue)) monster.FrostbiteStacks = Mathf.Max(0, frostValue);
            if (int.TryParse(freeze, out var freezeValue)) monster.FrozenMovesRemaining = Mathf.Max(0, freezeValue);
            monster.Shocked = shocked;
            game.TestLabRefreshViews();
            AddLog($"Updated monster #{monster.InstanceId} state.");
        }

        private void MoveSelected(int distance)
        {
            if (selectedMonsterId <= 0) return;
            StartCoroutine(game.TestLabMoveMonster(selectedMonsterId, distance));
            AddLog($"Monster #{selectedMonsterId}: requested movement {distance}.");
        }

        private void SetTileEffect(string effect)
        {
            AddLog(game.SetTileEffectFromConsole(tileEffectTile, effect, out var message)
                ? message : $"ERROR: {message}");
        }

        private MonsterState FindMonster(int id) =>
            game?.State?.Monsters.Find(monster => monster.InstanceId == id);

        private void AddLog(string value)
        {
            log.Add($"[{DateTime.Now:HH:mm:ss}] {value}");
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
                result[index] = values[index] != null ? values[index].name : "(null)";
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
