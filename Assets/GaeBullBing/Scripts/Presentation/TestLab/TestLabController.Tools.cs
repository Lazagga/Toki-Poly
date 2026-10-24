using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Towers;
using GaeBullBing.Presentation.Board;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.TestLab
{
    public sealed partial class TestLabController
    {
        private bool executing, showTrace, forceAttack, preferSelectedTarget, mapPicking = true;
        private bool showMonsterEditor, showTowerEditor;
        private string actionStatus = "준비 완료 — 맵의 타일을 클릭해 대상을 선택하세요.";
        private string upgradeSearch = "", traceSearch = "";
        private int repetitions = 1, pathDistance = 1, tracePhase;
        private string repetitionsText = "1", pathDistanceText = "1";
        private bool filterTraceTower, filterTraceMonster;
        private Vector2 traceScroll;
        private CombatTrace lastTrace;
        private CombatTrace previousTrace;
        private TestMonsterSettings monsterEdit;
        private TestTowerSettings towerEdit;
        private int editingMonsterId, editingTowerId;
        private readonly Dictionary<string, string> numericEdits = new();
        private readonly Stack<IEnumerator> executingStack = new();
        private GUISkin labSkin;
        private int labTab;
        private RectTransform inputShieldRoot;
        private readonly List<RectTransform> inputShields = new();
        public bool IsExecuting => executing;

        private void OnGUI()
        {
            var previous = GUI.skin;
            if (labSkin == null)
            {
                labSkin = Instantiate(previous);
                labSkin.label.fontSize = labSkin.textField.fontSize = labSkin.toggle.fontSize = 14;
                labSkin.button.fontSize = 13;
            }
            try { GUI.skin = labSkin; DrawLabGui(); }
            finally { GUI.skin = previous; GUI.enabled = true; }
        }
        private void OnDestroy() { if (labSkin != null) Destroy(labSkin); }
        private static void DrawPanelBackground(Rect rect)
        {
            var previous = GUI.color; GUI.color = new Color(.055f, .065f, .085f, .97f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }
        private void DrawAttackSection()
        {
            DrawIntField("타워 타일", ref towerTile, ref towerTileText, 0, 35);
            DrawMapPicking();
            DrawTowerStateEditor();
            DrawAttackControls();
            var turnOpen = game.Session.CanEndManualTurn;
            GUILayout.Label($"효과 실행 ({game.State.Round}턴 · {(turnOpen ? "진행 중" : game.State.RegionGimmickEndedRound == game.State.Round ? "종료됨 / 다음 턴 시작 대기" : "시작 전")})", UpgradeTierStyle());
            GUILayout.Label("지역 적용 시 첫 턴은 자동 시작됩니다. 턴 종료 후 다음 턴 시작을 눌러 맵 기믹을 갱신하세요.", WrapStyle());
            var enabledBeforeTurnButtons = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = enabledBeforeTurnButtons && game.Session.CanBeginManualTurn;
            if (GUILayout.Button(game.State.RegionGimmickEndedRound == game.State.Round ? "다음 턴 시작" : "턴 시작")) RunEvent("start", 0);
            GUI.enabled = enabledBeforeTurnButtons;
            if (GUILayout.Button("화상 / 스탠바이")) RunEvent("standby", 0);
            GUI.enabled = enabledBeforeTurnButtons && game.Session.CanEndManualTurn;
            if (GUILayout.Button("턴 종료")) RunEvent("end", 0);
            GUI.enabled = enabledBeforeTurnButtons;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("타워 밟기")) RunEvent("arrival", 0);
            if (GUILayout.Button("경로 효과 실행")) RunEvent("path", pathDistance);
            if (GUILayout.Button("완주 처리")) RunEvent("lap", 0);
            GUILayout.EndHorizontal();
            DrawIntField("경로 길이", ref pathDistance, ref pathDistanceText, 1, 35);
            GUILayout.Label($"수풀 보상: 이번 턴 남은 추가 굴림 {game.State.Player.AvailableExtraRolls}회 / 다음 턴 대기 {game.State.Player.PendingExtraRolls}회", WrapStyle());
            if (GUILayout.Button("수풀 추가 굴림 1회 사용")) RunEvent("extra_roll", 0);
        }

        private void DrawAttackControls()
        {
            GUILayout.Label("공격 실행", SectionStyle());
            forceAttack = GUILayout.Toolbar(forceAttack ? 1 : 0,
                new[] { "실제 규칙", "강제 실행" }) == 1;
            GUILayout.Label(forceAttack
                ? "강제 실행: 깃털·쿨다운·과열 휴식을 무시하고 공격 횟수를 지정합니다. 벽 타워의 특성은 유지되며 공격 관련 카운터는 갱신됩니다."
                : "실제 규칙: 계산된 공격 횟수·사거리·쿨다운·깃털·과열 규칙을 그대로 적용합니다.", WrapStyle());
            if (forceAttack)
            {
                DrawIntField("공격 횟수", ref attackCount, ref attackCountText, 1, 99);
                ignoreRange = GUILayout.Toggle(ignoreRange, "사거리 무시 (라인 공격의 범위는 유지)");
            }
            preferSelectedTarget = GUILayout.Toggle(preferSelectedTarget, "선택한 몬스터 우선 공격 (해제 시 자동 타겟팅)");
            GUILayout.Label("지정 대상은 공격 가능한 경우에만 우선합니다. 광역·라인·무작위 공격은 원래 공격 형식을 유지합니다.", WrapStyle());
            DrawIntField("반복 횟수", ref repetitions, ref repetitionsText, 1, 99);
            GUILayout.Label("1회 반복은 타워 페이즈 1회입니다. 턴 번호는 유지되며 쿨다운·충전·주기 카운터는 진행됩니다.", WrapStyle());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("선택 타워 공격", GUILayout.Height(32)))
                RunAttack(false);
            if (GUILayout.Button("모든 타워 공격 (실제 규칙)", GUILayout.Height(32)))
                RunAttack(true);
            GUILayout.EndHorizontal();
        }

        private void RunAttack(bool all)
        {
            if (!int.TryParse(towerTileText, out var parsedTile) || parsedTile < 0 || parsedTile >= game.State.Board.TileCount ||
                !int.TryParse(repetitionsText, out var parsedRepeat) || parsedRepeat < 1 || parsedRepeat > 99 ||
                (forceAttack && !all && (!int.TryParse(attackCountText, out var parsedCount) || parsedCount < 1 || parsedCount > 99)))
            { ReportFailure("타일 번호·반복 횟수·강제 공격 횟수를 올바르게 입력하세요."); return; }
            if (!all && !game.State.Board.Tiles[towerTile].HasTower)
            { ReportFailure("타워가 설치된 타일을 선택하세요."); return; }
            if (all && !game.State.Board.Tiles.Exists(t => t.HasTower))
            { ReportFailure("보드에 설치된 타워가 없습니다."); return; }
            if (!all && preferSelectedTarget && FindMonster(selectedMonsterId) == null)
            { ReportFailure("살아 있는 우선 공격 대상을 선택하거나 자동 타겟팅을 사용하세요."); return; }
            var tile = towerTile; var force = forceAttack; var count = attackCount;
            var repeat = repetitions; var target = preferSelectedTarget ? selectedMonsterId : 0;
            var range = ignoreRange;
            RunAction(all ? "모든 타워 / 실제 규칙" : $"타워 {tile} / {(force ? "강제 실행" : "실제 규칙")}",
                () => AttackAndSummarize(tile, all, force, count, repeat, target, range));
        }

        private IEnumerator AttackAndSummarize(int tile, bool all, bool force, int count, int repeat, int target, bool range)
        {
            yield return game.TestLabRunAttacks(tile, all, force, count, repeat, target, range);
            AddLog($"{(game.TestLabHitCount == 0 ? "피해 없음" : "실행 결과")}: " +
                $"피해 발생 {game.TestLabHitCount}회, 총 피해 {game.TestLabDamage:0.###}, 포획 {game.TestLabKillCount}마리. 미발동 사유와 상태 변화는 상세 전투 기록에서 확인하세요.");
        }

        private void RunEvent(string name, int distance)
        {
            var tile = towerTile;
            RunAction("효과 실행: " + TestLabKoreanText.EventName(name), () => {
                if (name == "arrival" || name == "path") RequireTileInput(towerTileText);
                if (name == "path" && (!int.TryParse(pathDistanceText, out var parsed) || parsed < 1 || parsed > 35))
                    throw new InvalidOperationException("경로 길이를 1~35칸으로 입력하세요.");
                return game.TestLabResolveUpgradeEvent(name, tile, distance);
            });
        }
        private void RequireTileInput(string text)
        {
            if (!int.TryParse(text, out var tile) || tile < 0 || tile >= game.State.Board.TileCount)
                throw new InvalidOperationException("올바른 타일 번호를 입력하세요. 빈 입력칸은 이전 값으로 실행하지 않습니다.");
        }

        // Flatten nested iterators so failures in nested presentation coroutines also
        // release the lock. No simultaneous attack/move/reset/state-edit operations.
        public bool RunAction(string label, Func<IEnumerator> factory)
        {
            if (!TryBegin(label)) return false;
            previousTrace = game.State.CombatTrace;
            lastTrace = new CombatTrace(); game.State.CombatTrace = lastTrace;
            StartCoroutine(ExecuteAction(label, factory));
            return true;
        }

        private IEnumerator ExecuteAction(string label, Func<IEnumerator> factory)
        {
            string failure = null;
            var before = Fingerprint();
            try
            {
                try { executingStack.Push(factory() ?? throw new InvalidOperationException("실행할 작업이 없습니다.")); }
                catch (Exception exception) { failure = exception.Message; }
                while (failure == null && executingStack.Count > 0)
                {
                    object next = null;
                    try
                    {
                        var iterator = executingStack.Peek();
                        if (!iterator.MoveNext())
                        { executingStack.Pop(); (iterator as IDisposable)?.Dispose(); continue; }
                        next = iterator.Current;
                    }
                    catch (Exception exception) { failure = exception.Message; }
                    if (failure != null) break;
                    if (next is IEnumerator nested) executingStack.Push(nested);
                    else yield return next;
                }
            }
            finally
            {
                DisposeActionStack();
                if (game != null && game.State != null)
                {
                    if (lastTrace != null)
                    {
                        lastTrace.Add($"Final: round {game.State.Round}, phase {game.State.CurrentPhase}");
                        lastTrace.Add($"수풀 보상: 이번 턴 {game.State.Player.AvailableExtraRolls}회 / 다음 턴 {game.State.Player.PendingExtraRolls}회");
                        foreach (var monster in game.State.Monsters) lastTrace.Add("Final: " + CombatTrace.Describe(monster), targetId: monster.InstanceId);
                        foreach (var tile in game.State.Board.Tiles)
                            if (tile.HasTower || tile.FireTurnsRemaining > 0 || tile.IceTurnsRemaining > 0 || tile.HasOvergrowth)
                                lastTrace.Add($"Final tile {tile.Index}: fire {tile.FireTurnsRemaining}, ice {tile.IceTurnsRemaining}; " +
                                    $"수풀 {tile.OvergrowthTurnsRemaining}턴; " +
                                    (tile.HasTower ? $"cooldown {tile.Tower.AttackCooldownRounds}, charge {tile.Tower.ChargeCount}, heat {tile.Tower.OverheatAttackTurns}, periodic {tile.Tower.PeriodicTurnCount}" : "no tower"), tile.Tower?.InstanceId ?? 0);
                    }
                    if (game.State.CombatTrace == lastTrace) game.State.CombatTrace = previousTrace;
                }
                EndAction();
            }
            if (failure != null) ReportFailure(label + ": " + failure);
            else
            {
                actionStatus = before == Fingerprint() ? "변화 없음: " + label : "완료: " + label;
                AddLog(actionStatus);
                monsterEdit = null; towerEdit = null;
            }
        }

        private bool TryBegin(string label)
        {
            if (executing || game == null || game.State == null) return false;
            executing = true; game.TestLabExecuting = true;
            actionStatus = "실행 중: " + label; AddLog(actionStatus); return true;
        }

        private void RunSync(string label, Action action)
        {
            if (!TryBegin(label)) return;
            try { action(); actionStatus = "완료: " + label; AddLog(actionStatus); }
            catch (Exception exception) { ReportFailure(label + ": " + exception.Message); }
            finally { EndAction(); }
        }

        private void ReportFailure(string error) { actionStatus = "실패: " + error; AddLog(actionStatus); }
        private void EndAction() { executing = false; if (game != null) game.TestLabExecuting = false; }
        private void DisposeActionStack()
        {
            while (executingStack.Count > 0)
                try { (executingStack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { AddLog("작업 정리: " + exception.Message); }
        }
        private void OnDisable()
        {
            StopAllCoroutines(); DisposeActionStack();
            if (game?.State != null && game.State.CombatTrace == lastTrace) game.State.CombatTrace = previousTrace;
            EndAction();
            if (inputShieldRoot != null) inputShieldRoot.gameObject.SetActive(false);
        }

        private string Fingerprint()
        {
            if (game?.State == null) return "No game state";
            var result = new StringBuilder($"round {game.State.Round}, phase {game.State.CurrentPhase}, laps {game.State.CompletedLaps}; ");
            result.Append($"extra rolls {game.State.Player.AvailableExtraRolls}/{game.State.Player.PendingExtraRolls}; ");
            var oasis = game.Session.PendingOasisUpgrade;
            if (oasis != null) result.Append($"oasis {oasis.TileIndex}:{oasis.RemainingChoices}; ");
            foreach (var monster in game.State.Monsters)
                result.Append($"M#{monster.InstanceId}: {CombatTrace.Describe(monster)}; ");
            foreach (var tile in game.State.Board.Tiles)
            {
                if (tile.FireTurnsRemaining > 0 || tile.IceTurnsRemaining > 0)
                    result.Append($"tile {tile.Index} fire {tile.FireTurnsRemaining}/ice {tile.IceTurnsRemaining}; ");
                if (tile.HasOvergrowth) result.Append($"overgrowth {tile.Index}:{tile.OvergrowthTurnsRemaining}; ");
                if (tile.HasTower)
                {
                    result.Append($"tier {tile.Tower.UpgradeTier}, upgrades {string.Join(",", tile.Tower.AppliedUpgradeIds)}; ");
                    result.Append($"T#{tile.Tower.InstanceId}@{tile.Index}: cd {tile.Tower.AttackCooldownRounds}, charge {tile.Tower.ChargeCount}, heat {tile.Tower.OverheatAttackTurns}, periodic {tile.Tower.PeriodicTurnCount}, consecutive {tile.Tower.ConsecutiveDamageBonus:0.###}, temporary +{tile.Tower.BonusAttackCount}; ");
                }
            }
            return result.ToString();
        }

        private void DrawMonsterStateEditor()
        {
            var monster = FindMonster(selectedMonsterId);
            if (monster == null) { monsterEdit = null; return; }
            if (monsterEdit == null || editingMonsterId != monster.InstanceId)
            {
                monsterEdit = TestMonsterSettings.Read(monster); editingMonsterId = monster.InstanceId;
                ClearNumericEdits("M.");
            }
            showMonsterEditor = GUILayout.Toggle(showMonsterEditor, "몬스터 능력치·위치·상태이상·면역 편집");
            if (!showMonsterEditor) return;
            DrawSettings(monsterEdit, "M.");
            GUILayout.Label("방어력은 기본값이며 난이도 보정은 별도로 적용됩니다. 위치와 누적 이동거리는 각각 지정할 수 있습니다.", WrapStyle());
            GUILayout.BeginHorizontal();
            foreach (var percent in new[] { 100, 50, 20 })
                if (GUILayout.Button("체력 " + percent + "%"))
                {
                    if (!ReadNumericEdits(monsterEdit, "M.", out var error)) ReportFailure(error);
                    else { monsterEdit.Health = monsterEdit.MaxHealth * percent / 100f; numericEdits.Remove("M.Health"); }
                }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("몬스터 상태 적용")) ApplyMonsterEditor();
            if (GUILayout.Button("현재 값 불러오기")) monsterEdit = null;
            if (GUILayout.Button("상태 초기화")) RunSync("몬스터 상태 초기화", () => {
                TestMonsterSettings.ClearStatuses(monster); game.TestLabRefreshViews(); monsterEdit = null;
            });
            GUILayout.EndHorizontal();
            GUILayout.Label("상태 초기화는 고유 면역·패시브·체력·위치를 유지합니다.", WrapStyle());
        }

        private void ApplyMonsterEditor()
        {
            if (!ReadNumericEdits(monsterEdit, "M.", out var error)) { ReportFailure(error); return; }
            RunSync("몬스터 상태 적용", () => {
                if (!game.TestLabApplyMonster(selectedMonsterId, monsterEdit, out var message)) throw new InvalidOperationException(message);
                monsterEdit = null;
            });
        }

        private void DrawTowerStateEditor()
        {
            var tile = game.State.Board.Tiles[towerTile];
            if (!tile.HasTower) { towerEdit = null; return; }
            if (towerEdit == null || editingTowerId != tile.Tower.InstanceId)
            { towerEdit = TestTowerSettings.Read(tile.Tower); editingTowerId = tile.Tower.InstanceId; ClearNumericEdits("T."); }
            var stats = game.TestLabTowerStats(towerTile);
            if (stats.HasValue)
                GUILayout.Label($"현재 공격력: {stats.Value.DamageFormula} = {stats.Value.CombatStats.Damage}; 사거리 {stats.Value.CombatStats.Range}, 대상 {stats.Value.CombatStats.TargetCount}, 공격 횟수 {stats.Value.CombatStats.AttackCount}" +
                    (tile.Tower.AttackCountLocked ? " (횟수 고정)" : $" + 임시 {tile.Tower.BonusAttackCount}회"), WrapStyle());
            var effects = new StringBuilder("적용 중인 효과: ");
            foreach (var effect in tile.Tower.AppliedEffectIds) effects.Append($"{effect}={tile.Tower.GetEffectValue(effect):0.##}  ");
            GUILayout.Label(effects.ToString(), WrapStyle());
            showTowerEditor = GUILayout.Toggle(showTowerEditor, "타워 쿨다운·충전·과열·깃털·추가 공격 편집");
            if (!showTowerEditor) return;
            DrawSettings(towerEdit, "T.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("타워 상태 적용"))
            {
                if (!ReadNumericEdits(towerEdit, "T.", out var error)) ReportFailure(error);
                else RunSync("타워 상태 적용", () => {
                    if (!towerEdit.Apply(tile.Tower, out var message)) throw new InvalidOperationException(message);
                    tile.HasBossFeather = tile.Tower.IsFeatherSealed;
                    game.TestLabRefreshViews(); towerEdit = null;
                });
            }
            if (GUILayout.Button("현재 값 불러오기")) towerEdit = null;
            if (GUILayout.Button("카운터 초기화")) RunSync("타워 카운터 초기화", () => {
                TestTowerSettings.ClearCounters(tile.Tower); tile.HasBossFeather = false; game.TestLabRefreshViews(); towerEdit = null;
            });
            GUILayout.EndHorizontal();
            GUILayout.Label("연속 공격 보너스는 0.2가 +20%를 뜻합니다. 깃털 봉쇄는 테스트용 설정이며 이후 보스 행동으로 바뀔 수 있습니다.", WrapStyle());
        }

        private void DrawSettings(object settings, string prefix)
        {
            var column = 0;
            foreach (var field in settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (column == 0) GUILayout.BeginHorizontal();
                GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.ExpandWidth(true));
                if (field.FieldType == typeof(bool))
                    field.SetValue(settings, GUILayout.Toggle((bool)field.GetValue(settings), TestLabKoreanText.FieldName(field.Name), new GUIStyle(GUI.skin.toggle) { wordWrap = true }));
                else
                {
                    GUILayout.Label(TestLabKoreanText.FieldName(field.Name), WrapStyle(), GUILayout.ExpandWidth(true));
                    var key = prefix + field.Name;
                    if (!numericEdits.TryGetValue(key, out var value))
                        value = Convert.ToString(field.GetValue(settings), CultureInfo.InvariantCulture);
                    numericEdits[key] = GUILayout.TextField(value, GUILayout.Width(65));
                }
                GUILayout.EndHorizontal();
                if (++column == 2) { GUILayout.EndHorizontal(); column = 0; }
            }
            if (column != 0) GUILayout.EndHorizontal();
        }
        private bool ReadNumericEdits(object settings, string prefix, out string error)
        {
            error = "숫자를 올바르게 입력하세요.";
            if (settings == null) return false;
            foreach (var field in settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType == typeof(bool) || !numericEdits.TryGetValue(prefix + field.Name, out var text)) continue;
                if (field.FieldType == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) field.SetValue(settings, integer);
                else if (field.FieldType == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !float.IsNaN(number) && !float.IsInfinity(number)) field.SetValue(settings, number);
                else { error = "숫자 입력 오류: " + TestLabKoreanText.FieldName(field.Name); return false; }
            }
            return true;
        }
        private void ClearNumericEdits(string prefix)
        { foreach (var key in new List<string>(numericEdits.Keys)) if (key.StartsWith(prefix, StringComparison.Ordinal)) numericEdits.Remove(key); }

        private bool MatchesUpgradeSearch(TowerUpgradeDefinition u) =>
            string.IsNullOrWhiteSpace(upgradeSearch) ||
            (u.DisplayName + " " + u.Description + " " + u.Id).IndexOf(upgradeSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        private void DrawSelectedUpgradeSummary()
        {
            var summary = new StringBuilder("선택한 강화: ");
            for (var i = 0; i < upgrades.Length; i++)
                if (selectedUpgrades[i] && upgrades[i] != null) summary.Append($"[T{upgrades[i].Tier}] {upgrades[i].Description}  ");
            GUILayout.Label(summary.ToString(), WrapStyle());
        }
        private bool SelectionHasEffect(string id)
        {
            foreach (var effect in towers[towerIndex].BaseEffects) if (effect.Id == id) return true;
            for (var i = 0; i < upgrades.Length; i++)
                if (selectedUpgrades[i]) foreach (var effect in upgrades[i].Effects) if (effect.Id == id) return true;
            return false;
        }
        private string UpgradeWarning(TowerUpgradeDefinition u)
        {
            foreach (var effect in u.Effects)
            {
                if (effect.Id == "rolling_stone_stun" && !SelectionHasEffect(TowerEffectCatalog.RollingStone))
                    return "구르는 돌 효과가 있어야 발동합니다. 선택은 가능합니다.";
                if (effect.Id == "knockback_impact" && !SelectionHasEffect(TowerEffectCatalog.Knockback) && !SelectionHasEffect("knockback_line"))
                    return "넉백 효과가 있어야 발동합니다. 선택은 가능합니다.";
                if (effect.Id == "disable_burn") return "이 타워의 기본 효과를 포함한 화상 부여를 비활성화합니다.";
                if (effect.Id == "shock_damage_bonus" || effect.Id == "shock_link") return "공격 이전부터 감전 상태인 대상이 필요합니다.";
                if (effect.Id == TowerEffectCatalog.BurnExplode || effect.Id == "burn_consume_burst") return "이번 공격 이전의 화상 중첩을 기준으로 판정합니다.";
                if (effect.Id == TowerEffectCatalog.TileBreak || effect.Id == "ice_tile_vulnerability") return "대상이 있는 타일에 얼음 장판이 필요합니다.";
                if (effect.Id == TowerEffectCatalog.FieldSpread) return "전이할 불 또는 얼음 장판이 미리 설치되어 있어야 합니다.";
            }
            return "";
        }

        private void DrawMapPicking()
        {
            mapPicking = GUILayout.Toggle(mapPicking, "맵 클릭으로 타일·타워·몬스터 선택 (카메라 확대 없음)");
        }
        private void Update()
        {
            UpdateInputShields();
            if (!mapPicking || executing || showTrace || game?.State == null || Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame) return;
            var screen = Mouse.current.position.ReadValue();
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            if (collapsed ? new Rect(8, 8, 180, 32).Contains(gui) : gui.x < Mathf.Min(660f, Screen.width * .48f)) return;
            if (!collapsed && inspectorVisible && gui.x > Screen.width - Mathf.Min(370f, Screen.width * .28f)) return;
            if (PointerHitsUi(screen)) return;
            var camera = UnityEngine.Camera.main;
            var board = FindFirstObjectByType<BoardTilemapView>();
            if (camera == null || board == null) return;
            var ray = camera.ScreenPointToRay(screen);
            if (!new Plane(Vector3.forward, board.transform.position).Raycast(ray, out var distance)) return;
            var world = ray.GetPoint(distance); var nearest = -1; var best = .75f * .75f;
            for (var i = 0; i < game.State.Board.TileCount; i++)
            {
                var squared = (board.GetWorldPosition(i) - world).sqrMagnitude;
                if (squared < best) { best = squared; nearest = i; }
            }
            if (nearest < 0) return;
            towerTile = monsterTile = tileEffectTile = nearest;
            towerTileText = monsterTileText = tileEffectTileText = nearest.ToString();
            if (SyncTowerSelectionToTile())
            {
                var tile = game.State.Board.Tiles[nearest];
                bonusTile = tile.IsBonusTile;
                if (tile.HasTower)
                    for (var i = 0; i < upgrades.Length; i++)
                        selectedUpgrades[i] = upgrades[i] != null && tile.Tower.AppliedUpgradeIds.Contains(upgrades[i].Id);
            }
            var occupants = game.State.Monsters.FindAll(m => m.CurrentTileIndex == nearest);
            if (occupants.Count > 0)
            {
                var current = occupants.FindIndex(m => m.InstanceId == selectedMonsterId);
                selectedMonsterId = occupants[(current + 1) % occupants.Count].InstanceId;
            }
            towerEdit = null; monsterEdit = null;
            AddLog($"{nearest}번 타일 선택. 다시 클릭하면 같은 타일의 몬스터를 순환 선택합니다.");
        }

        private static bool PointerHitsUi(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            foreach (var hit in hits)
                if (hit.gameObject.GetComponentInParent<BoardPointerPassthrough>() == null) return true;
            return false;
        }

        // IMGUI does not consume uGUI raycasts. Transparent, runtime-only shields
        // keep clicks on lab controls from activating game buttons behind them.
        private void UpdateInputShields()
        {
            if (inputShieldRoot == null)
            {
                var root = new GameObject("TestLab Input Shields", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                root.transform.SetParent(transform, false);
                inputShieldRoot = root.GetComponent<RectTransform>();
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
                for (var i = 0; i < 3; i++)
                {
                    var shield = new GameObject("Input Shield " + i, typeof(RectTransform), typeof(Image));
                    shield.transform.SetParent(root.transform, false);
                    shield.GetComponent<Image>().color = Color.clear;
                    inputShields.Add(shield.GetComponent<RectTransform>());
                }
            }
            inputShieldRoot.gameObject.SetActive(true);
            SetShield(inputShields[0], collapsed ? new Rect(8, 8, 180, 32) : new Rect(0, 0, Mathf.Min(660f, Screen.width * .48f), Screen.height), true);
            var rightWidth = Mathf.Min(370f, Screen.width * .28f);
            SetShield(inputShields[1], new Rect(Screen.width - rightWidth, 0, rightWidth, Screen.height), !collapsed && inspectorVisible);
            SetShield(inputShields[2], new Rect(0, 0, Screen.width, Screen.height), showTrace && !collapsed);
        }
        private static void SetShield(RectTransform transform, Rect rectangle, bool visible)
        {
            transform.gameObject.SetActive(visible);
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            transform.anchoredPosition = new Vector2(rectangle.x, -rectangle.y);
            transform.sizeDelta = rectangle.size;
        }

        private void DrawTraceWindow()
        {
            DrawPanelBackground(new Rect(Screen.width * .06f, Screen.height * .05f, Screen.width * .88f, Screen.height * .9f));
            GUI.ModalWindow(7184, new Rect(Screen.width * .06f, Screen.height * .05f,
                Screen.width * .88f, Screen.height * .9f), _ => {
                GUILayout.Label("상세 전투 기록 — 실제 처리 순서", HeaderStyle());
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("닫기", GUILayout.Width(75))) showTrace = false;
                GUILayout.Label("검색", GUILayout.Width(50)); traceSearch = GUILayout.TextField(traceSearch);
                filterTraceTower = GUILayout.Toggle(filterTraceTower, "선택한 타워만");
                filterTraceMonster = GUILayout.Toggle(filterTraceMonster, "선택한 몬스터만");
                GUILayout.EndHorizontal();
                tracePhase = GUILayout.Toolbar(tracePhase, new[] { "전체", "시스템", "1 조건 판정", "2 기본 공격", "3 기본 지속 효과", "4 적중 효과", "5 조건부 효과" });
                GUILayout.Label("마지막 실행의 상세 기록입니다. 변화가 없다면 미발동 사유를 확인하세요. 일반 게임에서는 이 기록을 수집하지 않습니다.", WrapStyle());
                var visible = new StringBuilder();
                traceScroll = GUILayout.BeginScrollView(traceScroll);
                if (lastTrace == null) GUILayout.Label("먼저 공격 또는 효과를 실행하세요.");
                else
                {
                    var tower = game.State.Board.Tiles[towerTile].Tower;
                    foreach (var entry in lastTrace.Entries)
                    {
                        if (tracePhase > 0 && entry.Phase != tracePhase - 1) continue;
                        if (filterTraceTower && (tower == null || entry.TowerId != tower.InstanceId)) continue;
                        if (filterTraceMonster && entry.TargetId != selectedMonsterId) continue;
                        var text = TestLabKoreanText.FormatEntry(entry);
                        if (!string.IsNullOrWhiteSpace(traceSearch) && text.IndexOf(traceSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        visible.AppendLine(text); GUILayout.Label(text, WrapStyle());
                    }
                    if (lastTrace.DroppedEntries > 0) GUILayout.Label($"기록은 최대 {CombatTrace.EntryLimit}행입니다. 초과된 {lastTrace.DroppedEntries}행은 생략했습니다. 반복 횟수를 줄여주세요.");
                }
                GUILayout.EndScrollView();
                if (GUILayout.Button("보이는 기록 복사")) GUIUtility.systemCopyBuffer = visible.ToString();
            }, "상세 전투 기록");
        }
    }

    // Display-only translation. Effect IDs, JSON keys and original diagnostic
    // records stay unchanged, so localization cannot alter combat behavior.
    internal static class TestLabKoreanText
    {
        private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
        {
            ["MaxHealth"] = "최대 체력", ["Health"] = "현재 체력", ["Defense"] = "기본 방어력",
            ["Tile"] = "현재 타일", ["Move"] = "이동력", ["Distance"] = "누적 이동거리",
            ["Burn"] = "화상 중첩", ["MaxBurn"] = "화상 최대 중첩", ["Frost"] = "동상 중첩",
            ["Freeze"] = "빙결 횟수", ["Stun"] = "기절 횟수", ["Slow"] = "다음 이동 감소량",
            ["Shock"] = "감전 상태", ["BurnImmune"] = "고유 화상 면역", ["FrostImmune"] = "고유 동상 면역",
            ["FreezeImmune"] = "고유 빙결 면역", ["KnockbackImmune"] = "고유 넉백 면역",
            ["ShockImmune"] = "고유 감전 면역", ["FreezeImmuneThisTurn"] = "이번 턴 빙결 면역",
            ["FreezeImmunityPending"] = "빙결 면역 대기", ["KnockbackConsumed"] = "현재 넉백 면역",
            ["KnockbackPending"] = "넉백 면역 대기", ["BurnSourceTowerId"] = "화상 부여 타워 번호",
            ["Cooldown"] = "남은 쿨다운", ["Charge"] = "충전 횟수", ["Overheat"] = "과열 카운터",
            ["Periodic"] = "주기 카운터", ["LastTarget"] = "직전 공격 대상 번호",
            ["BonusAttacks"] = "임시 추가 공격 횟수", ["BonusTurns"] = "추가 공격 남은 턴",
            ["PendingAttacks"] = "대기 중 추가 공격 횟수", ["PendingTurns"] = "대기 중 추가 공격 턴",
            ["PermanentAttacks"] = "영구 추가 공격 횟수", ["ConsecutiveRate"] = "연속 공격 보너스",
            ["FeatherSealed"] = "깃털로 공격 봉쇄"
        };

        private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Invalid monster values: HP > 0, HP <= max HP, nonnegative numbers and a valid tile required."] = "몬스터 입력값을 확인하세요. 체력은 0보다 크고 최대 체력 이하여야 하며, 나머지 수치는 0 이상이고 타일 번호도 유효해야 합니다.",
            ["Tower counters must be nonnegative, charge/overheat/consecutive values must fit effect caps."] = "타워 카운터는 0 이상이어야 하며 충전·과열·연속 공격 보너스는 해당 효과의 상한을 넘을 수 없습니다.",
            ["Select exactly one tier-2 upgrade before selecting tier-3 upgrades."] = "3티어 강화를 선택하려면 2티어 강화를 정확히 1개 선택하세요.",
            ["Tower element must match the tile's build property."] = "타워 속성이 해당 타일의 건설 속성과 같아야 합니다.",
            ["The tile already has a tower."] = "이미 타워가 설치된 타일입니다.",
            ["This tower cannot be built on the selected tile."] = "선택한 타일에는 이 타워를 건설할 수 없습니다.",
            ["The tile does not have a tower to upgrade."] = "강화할 타워가 없습니다.",
            ["The upgrade tier must be the tower's next tier."] = "현재 타워의 다음 티어 강화를 선택해야 합니다.",
            ["The additional upgrade must be tier 3."] = "추가 강화는 3티어여야 합니다.",
            ["The selected upgrade is already applied."] = "이미 적용된 강화입니다.",
            ["An upgrade definition id is required."] = "강화 데이터 ID가 필요합니다.",
            ["Selected monster no longer exists."] = "선택한 몬스터가 더 이상 존재하지 않습니다.",
            ["Select a living monster."] = "살아 있는 몬스터를 선택하세요.",
            ["Select two valid dice."] = "유효한 주사위 2개를 선택하세요.",
            ["Invalid tile."] = "올바르지 않은 타일 번호입니다.",
            ["Tower not found"] = "타워를 찾을 수 없습니다", ["Region not found"] = "지역을 찾을 수 없습니다",
            ["Invalid upgrade selection"] = "선택할 수 없는 강화", ["Region changed"] = "지역 변경",
            ["Equipped dice"] = "장착한 주사위",
            ["No automatic round advance."] = "턴 번호는 자동으로 증가하지 않습니다.",
            ["No ordinary attack: no living target in range"] = "일반 공격 없음: 사거리 안에 살아 있는 대상이 없습니다",
            ["No ordinary attack: passive wall / line-buff tower"] = "일반 공격 없음: 벽 또는 라인 버프 전용 타워입니다",
            ["No ordinary attack: overheat rest phase"] = "일반 공격 없음: 과열로 인한 휴식입니다",
            ["No ordinary attack: feather sealed"] = "일반 공격 없음: 깃털로 봉쇄되었습니다",
            ["No ordinary attack: cooldown"] = "일반 공격 없음: 쿨다운 중입니다",
            ["No target in range: charge"] = "사거리 내 대상 없음: 충전",
            ["automatic: boss > distance > HP"] = "자동: 보스 > 누적 이동거리 > 체력",
            ["Ordinary attack volleys"] = "일반 공격 횟수", ["targeting"] = "대상 선정",
            ["preferred"] = "우선 대상", ["if eligible"] = "공격 가능한 경우",
            ["pre-hit conditions"] = "공격 전 조건 판정", ["base persistent effects"] = "기본 지속 효과",
            ["additional upgrade on-hit"] = "추가 강화 적중 효과", ["upgrade on-hit"] = "강화 적중 효과",
            ["burn/freeze/shock on-hit"] = "화상·빙결·감전 적중 효과",
            ["additional conditional effects"] = "추가 조건부 효과", ["conditional follow-up"] = "조건부 후속 효과",
            ["chain/pierce on-hit"] = "연쇄/관통 적중 효과", ["chain/pierce conditional"] = "연쇄/관통 조건부 효과",
            ["frostbite immediate burst"] = "동상 즉시 폭발", ["death follow-up effects"] = "처치 후속 효과",
            ["burn tick: max HP * 0.25% * stacks"] = "화상 피해: 최대 체력 * 0.25% * 중첩",
            ["damage_store release"] = "저장 피해 방출", ["status_transfer recipient"] = "상태이상 전이 대상",
            ["NOT MET (before hit)"] = "미충족 (공격 이전 기준)", ["MET (before hit)"] = "충족 (공격 이전 기준)",
            ["End: state unchanged"] = "종료: 상태 변화 없음",
            ["burn skipped: disable_burn"] = "화상 미적용: 화상 부여 비활성화",
            ["burn skipped: immune or no stacks"] = "화상 미적용: 면역 또는 부여 중첩 없음",
            ["freeze skipped: innate/temporary immunity"] = "빙결 미적용: 고유/임시 면역",
            ["frostbite skipped: dead / immune / no stacks"] = "동상 미적용: 처치됨 / 면역 / 부여 중첩 없음",
            ["knockback skipped: dead / innate or temporary immunity"] = "넉백 미적용: 처치됨 / 고유 또는 임시 면역",
            ["Primary modifiers"] = "기본 공격 보정", ["summed hit bonus"] = "합산 타격 보너스",
            ["Conditional extra factor"] = "조건부 추가 피해 배율",
            ["rates are summed from the enabled pre-hit conditional effects"] = "공격 전 조건을 충족한 효과들의 배율을 합산합니다",
            ["direct remaining HP removal"] = "남은 체력 직접 제거", ["not a defense-scaled hit"] = "방어력을 적용하지 않는 처치 판정",
            ["immediate burst threshold"] = "즉시 폭발 기준 중첩",
            ["freeze immunity"] = "빙결 면역", ["knockback immunity"] = "넉백 면역",
            ["damage raw"] = "피해 원값", ["incoming"] = "받는 피해 배율", ["defense"] = "방어력",
            ["temporary"] = "임시", ["ignore range"] = "사거리 무시", ["bypass restrictions"] = "공격 제한 무시",
            ["ACTUAL rules"] = "실제 규칙", ["FORCED count"] = "강제 공격 횟수",
            ["no tower"] = "타워 없음", ["turn volley"] = "이번 페이즈 공격 횟수",
            ["Projectile"] = "투사체", ["AreaTile"] = "타일 광역", ["ChainLine"] = "라인 공격",
            ["ChainTile"] = "타일 연쇄", ["RollingStone"] = "구르는 돌",
            ["PlayerTurnStart"] = "플레이어 턴 시작", ["DiceRoll"] = "주사위 굴림", ["PlayerMove"] = "플레이어 이동",
            ["TowerSelection"] = "건설·강화 선택", ["TowerResolve"] = "건설·강화 처리",
            ["MonsterStandby"] = "몬스터 스탠바이", ["MonsterSpawn"] = "몬스터 소환",
            ["MonsterMove"] = "몬스터 이동", ["TowerCombat"] = "타워 전투", ["TileResolve"] = "타일 계산",
            ["RoundEnd"] = "턴 종료", ["Victory"] = "승리", ["Defeat"] = "패배",
            ["primary"] = "기본 공격", ["system"] = "시스템", ["Begin"] = "시작", ["End"] = "종료",
            ["Execution"] = "실행", ["Final tile"] = "최종 타일", ["Final"] = "최종 상태",
            ["round"] = "턴", ["phase"] = "페이즈", ["tile"] = "타일", ["HP"] = "체력",
            ["burn"] = "화상", ["frostbite"] = "동상", ["frost"] = "동상", ["freeze"] = "빙결",
            ["shock"] = "감전", ["stun"] = "기절", ["slow"] = "감속", ["knockback"] = "넉백",
            ["store"] = "저장 피해", ["cooldown"] = "쿨다운", ["charge"] = "충전", ["heat"] = "과열",
            ["periodic"] = "주기", ["damage"] = "피해", ["range"] = "사거리", ["targets"] = "대상 수",
            ["attacks"] = "공격 횟수", ["locked"] = "횟수 고정", ["requested"] = "요청",
            ["fire-tile"] = "불 장판", ["previous fire"] = "기존 불 장판", ["fire"] = "불",
            ["ice"] = "얼음", ["place"] = "설치", ["base"] = "기본", ["consecutive"] = "연속 보너스",
            ["True"] = "예", ["False"] = "아니오", ["None"] = "없음", ["execute"] = "처형"
        };

        private static readonly Regex Tokens = new(
            @"(?<![A-Za-z0-9_])(?:" + string.Join("|", Words.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string FieldName(string field) => FieldLabels.TryGetValue(field, out var label) ? label : field;
        public static string YesNo(bool value) => value ? "예" : "아니오";
        public static string Element(GaeBullBing.Core.TowerElement element) => element switch
        {
            GaeBullBing.Core.TowerElement.Fire => "불", GaeBullBing.Core.TowerElement.Ice => "얼음",
            GaeBullBing.Core.TowerElement.Physics => "물리", GaeBullBing.Core.TowerElement.Electric => "전기", _ => "없음"
        };
        public static string StatusNames(IEnumerable<string> statuses) => string.Join(", ", statuses.Select(Translate));
        public static string EventName(string name) => name switch
        { "start" => "턴 시작", "standby" => "화상 / 스탠바이", "end" => "턴 종료", "arrival" => "타워 밟기", "path" => "경로", "lap" => "완주", _ => name };
        public static string FormatEntry(CombatTraceEntry entry) =>
            $"[{(entry.Phase == 0 ? "시스템" : entry.Phase + "단계")}: {Translate(entry.Source)}] " +
            $"타워#{entry.TowerId} 몬스터#{entry.TargetId} — {Translate(entry.Message)}";

        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Regex.Replace(text, @"Tier-3 selection limit: (\d+)\.", "3티어 강화는 최대 $1개까지 선택할 수 있습니다.");
            text = Regex.Replace(text, @"Configured (.+) on tile (\d+)\.", "$2번 타일의 $1 설정을 적용했습니다.");
            return Tokens.Replace(text, match => Words[match.Value]);
        }
    }
}
