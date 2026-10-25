using System;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Dice;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Game
{
    public sealed partial class GameSession
    {
        private readonly BoardService boardService;
        private readonly PlayerMovementService movementService;
        private readonly WeightedDiceRoller diceRoller;
        private readonly MonsterService monsterService;
        private readonly TowerService towerService;
        private readonly TowerCombatService towerCombatService;
        private readonly TowerEffectService towerEffectService = new();
        private readonly RegionGimmickService regionGimmickService = new();
        private RegionDefinition currentRegion;
        private readonly System.Collections.Generic.Dictionary<int, int> killRewardDicePoints = new();
        private readonly System.Collections.Generic.HashSet<int> rewardedMonsterIds = new();
        private int[] nextDiceResults;

        public GameSession(
            GameState state,
            BoardService boardService,
            PlayerMovementService movementService,
            WeightedDiceRoller diceRoller,
            MonsterService monsterService,
            TowerService towerService,
            TowerCombatService towerCombatService)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            this.boardService = boardService ?? throw new ArgumentNullException(nameof(boardService));
            this.movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
            this.diceRoller = diceRoller ?? throw new ArgumentNullException(nameof(diceRoller));
            this.monsterService = monsterService ?? throw new ArgumentNullException(nameof(monsterService));
            this.towerService = towerService ?? throw new ArgumentNullException(nameof(towerService));
            this.towerCombatService = towerCombatService ?? throw new ArgumentNullException(nameof(towerCombatService));
        }

        public GameState State { get; }
        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> LastPlayerPathResults { get; private set; } =
            Array.Empty<TowerAttackResult>();

        public void StartNewGame(int diceCount = 2, BoardDefinition boardDefinition = null)
        {
            if (diceCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(diceCount));

            if (boardDefinition != null)
                boardService.Initialize(State.Board, boardDefinition);
            else
                boardService.Initialize(State.Board);
            State.DiceInventory.ResetToDefaults();
            State.Monsters.Clear();
            State.Dice.Clear();
            State.Dice.Add(State.DiceInventory.Dice[0]);
            State.Dice.Add(State.DiceInventory.Dice[1]);

            State.Player.CurrentTileIndex = 0;
            State.Player.DicePoints = 0;
            State.Player.PathDamageBuffs.Clear();
            State.Player.ArrivalSerial = 0;
            regionGimmickService.Reset(State);
            currentRegion = null;
            CancelOasisUpgrade();
            LastPlayerPathResults = Array.Empty<TowerAttackResult>();
            State.Round = 1;
            State.CompletedLaps = 0;
            State.EscapedMonsterCount = 0;
            State.BossSpawned = false;
            State.BossDefeated = false;
            State.BossEscaped = false;
            State.BossInstanceId = 0;
            State.ResetPermanentTowerBonuses();
            State.EscapeLimit = GameState.DefaultEscapeLimit;
            State.CurrentPhase = TurnPhase.PlayerTurnStart;
            State.LastDiceResults.Clear();
            State.LastMoveDistance = 0;
            nextDiceResults = null;
            killRewardDicePoints.Clear();
            rewardedMonsterIds.Clear();
        }

        public void SetNextDiceResults(int first, int second)
        {
            if (first < 1 || first > 6 || second < 1 || second > 6)
                throw new ArgumentOutOfRangeException("Dice values must be between 1 and 6.");
            nextDiceResults = new[] { first, second };
        }

        public bool TryShiftDiceWeight(int diceIndex, int faceValue, int delta)
        {
            if (diceIndex < 0 || diceIndex >= State.Dice.Count || faceValue < 1 || faceValue > 6 ||
                (delta != -1 && delta != 1))
                return false;

            var dice = State.Dice[diceIndex];
            var sourceIndex = Array.IndexOf(dice.Faces, faceValue);
            var targetValue = (faceValue - 1 + delta + 6) % 6 + 1;
            var targetIndex = Array.IndexOf(dice.Faces, targetValue);
            if (sourceIndex < 0 || targetIndex < 0 || dice.Weights[sourceIndex] <= 0)
                return false;

            dice.SetWeight(sourceIndex, dice.Weights[sourceIndex] - 1);
            dice.SetWeight(targetIndex, dice.Weights[targetIndex] + 1);
            return true;
        }

        public int RollDiceAndMovePlayer(bool deferPathEffects = false)
        {
            if (State.Board.TileCount == 0 || !CanRollDice)
                throw new InvalidOperationException("Start a game before rolling dice.");

            State.CurrentPhase = TurnPhase.DiceRoll;
            State.LastDiceResults.Clear();

            var distance = 0;
            for (var index = 0; index < State.Dice.Count; index++)
            {
                var result = nextDiceResults != null && index < nextDiceResults.Length
                    ? nextDiceResults[index]
                    : diceRoller.Roll(State.Dice[index]);
                State.LastDiceResults.Add(result);
                distance += result;
            }
            nextDiceResults = null;

            State.LastMoveDistance = distance;
            State.CurrentPhase = TurnPhase.PlayerMove;
            // Animated gameplay resolves each departed tile after landing on
            // the next tile. Headless callers retain synchronous resolution.
            LastPlayerPathResults = deferPathEffects ? Array.Empty<TowerAttackResult>() :
                towerEffectService.ResolvePlayerPath(State, State.Player.CurrentTileIndex, distance, false);
            var completedLap = State.Player.CurrentTileIndex + distance >= State.Board.TileCount;
            movementService.Move(State.Player, State.Board, distance);
            State.Player.ArrivalSerial++;
            if (completedLap && !deferPathEffects)
                CompletePlayerLap();
            State.CurrentPhase = TurnPhase.TileResolve;
            ResolveBossVictory(LastPlayerPathResults);
            return distance;
        }

        public System.Collections.Generic.IReadOnlyList<MonsterMoveResult> MoveMonsters(
            System.Collections.Generic.IReadOnlyList<TowerDefinition> definitions = null,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades = null)
        {
            State.CurrentPhase = TurnPhase.MonsterMove;
            var bossEvents = new System.Collections.Generic.Dictionary<int,
                System.Collections.Generic.List<BossFeatherEvent>>();
            foreach (var monster in State.Monsters)
            {
                if (!monster.IsBoss || monster.BossInitialFeatherPlaced) continue;
                monster.BossInitialFeatherPlaced = true;
                var events = new System.Collections.Generic.List<BossFeatherEvent>();
                bossEvents[monster.InstanceId] = events;
                PlaceBossFeather(0, 0, events, definitions, upgrades);
            }

            var wallDamage = new System.Collections.Generic.Dictionary<int, float>();
            foreach (var tile in State.Board.Tiles)
            {
                if (!tile.HasTower || !tile.Tower.HasEffect(TowerEffectCatalog.Wall)) continue;
                TowerDefinition definition = null;
                foreach (var candidate in definitions ?? System.Array.Empty<TowerDefinition>())
                    if (candidate != null && candidate.Id == tile.Tower.DefinitionId) { definition = candidate; break; }
                if (definition != null)
                    wallDamage[tile.Tower.InstanceId] = BuildCombatStats(definition, tile, upgrades).Damage;
            }
            var moved = monsterService.MoveAll(State, wallDamage);
            var results = new System.Collections.Generic.List<MonsterMoveResult>(moved.Count);
            foreach (var result in moved)
            {
                if (!result.IsBoss)
                {
                    results.Add(result);
                    if (result.ReachedBase)
                        State.EscapedMonsterCount++;
                    continue;
                }

                if (!bossEvents.TryGetValue(result.InstanceId, out var events))
                    events = new System.Collections.Generic.List<BossFeatherEvent>();
                for (var step = 1; step <= result.Distance; step++)
                {
                    var enteredTileIndex = (result.StartTileIndex + step) % State.Board.TileCount;
                    RecoverBossFeather(enteredTileIndex, step, events);
                    var line = enteredTileIndex switch { 9 => 1, 18 => 2, 27 => 3, _ => -1 };
                    if (line >= 0)
                        PlaceBossFeather(line, step, events, definitions, upgrades);
                }
                results.Add(result.WithFeatherEvents(events));
                if (result.ReachedBase)
                {
                    State.BossEscaped = true;
                    State.CurrentPhase = TurnPhase.Defeat;
                }
            }

            if (State.CurrentPhase != TurnPhase.Defeat &&
                State.EscapedMonsterCount >= State.EscapeLimit)
                State.CurrentPhase = TurnPhase.Defeat;
            return results;
        }

        private void PlaceBossFeather(
            int line,
            int stepOffset,
            System.Collections.Generic.ICollection<BossFeatherEvent> events,
            System.Collections.Generic.IReadOnlyList<TowerDefinition> definitions,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            if (definitions == null) return;
            TileState selected = null;
            var selectedDamage = int.MinValue;
            foreach (var tile in State.Board.Tiles)
            {
                if (!tile.HasTower || tile.Tower.IsFeatherSealed ||
                    MonsterService.GetLine(tile.Index) != line)
                    continue;
                TowerDefinition definition = null;
                foreach (var candidate in definitions)
                    if (candidate != null && candidate.Id == tile.Tower.DefinitionId)
                    {
                        definition = candidate;
                        break;
                    }
                if (definition == null) continue;
                var damage = BuildCombatStats(definition, tile, upgrades).Damage;
                if (damage <= selectedDamage) continue;
                selected = tile;
                selectedDamage = damage;
            }

            if (selected == null) return;
            selected.Tower.IsFeatherSealed = true;
            selected.HasBossFeather = true;
            events.Add(new BossFeatherEvent(BossFeatherEventType.Drop, selected.Index, stepOffset));
        }

        private void RecoverBossFeather(
            int tileIndex,
            int stepOffset,
            System.Collections.Generic.ICollection<BossFeatherEvent> events)
        {
            var tile = State.Board.Tiles[tileIndex];
            if (!tile.HasBossFeather) return;
            tile.HasBossFeather = false;
            if (tile.HasTower) tile.Tower.IsFeatherSealed = false;
            events.Add(new BossFeatherEvent(BossFeatherEventType.Recover, tileIndex, stepOffset));
        }

        private void ClearBossFeathers()
        {
            foreach (var tile in State.Board.Tiles)
            {
                tile.HasBossFeather = false;
                if (tile.HasTower) tile.Tower.IsFeatherSealed = false;
            }
        }

        private void ResolveBossVictory(
            System.Collections.Generic.IEnumerable<TowerAttackResult> results)
        {
            if (State.BossInstanceId == 0) return;
            if (!State.BossDefeated)
                foreach (var result in results)
                {
                    if (!result.Killed || result.TargetInstanceId != State.BossInstanceId) continue;
                    State.BossDefeated = true;
                    ClearBossFeathers();
                    break;
                }

            if (State.BossDefeated && State.Monsters.Count == 0)
                State.CurrentPhase = TurnPhase.Victory;
        }

        public MonsterState SpawnMonster(MonsterDefinition definition, float healthMultiplier = 1f)
        {
            State.CurrentPhase = TurnPhase.MonsterSpawn;
            var monster = monsterService.Spawn(State, definition, healthMultiplier);
            killRewardDicePoints[monster.InstanceId] = definition.KillRewardDicePoints;
            if (monster.IsBoss)
            {
                State.BossSpawned = true;
                State.BossInstanceId = monster.InstanceId;
            }
            return monster;
        }

        public int CollectKillRewards(System.Collections.Generic.IEnumerable<TowerAttackResult> results)
        {
            if (results == null) return 0;
            var awarded = 0;
            foreach (var result in results)
            {
                if (!result.Killed || result.TargetInstanceId <= 0 ||
                    !rewardedMonsterIds.Add(result.TargetInstanceId)) continue;
                if (killRewardDicePoints.TryGetValue(result.TargetInstanceId, out var reward))
                    awarded += System.Math.Max(0, reward);
            }
            State.Player.DicePoints += awarded;
            return awarded;
        }

        public void CompleteRound()
        {
            if (State.IsFinished)
                return;

            CancelOasisUpgrade();
            State.CurrentPhase = TurnPhase.RoundEnd;
            State.Round++;
            State.CurrentPhase = TurnPhase.PlayerTurnStart;
        }

        public TowerState BuildTower(int tileIndex, string definitionId)
        {
            if (State.Board.Tiles[tileIndex].IsBuildRestricted)
                throw new InvalidOperationException("수풀 타일에는 타워를 건설할 수 없습니다.");
            State.CurrentPhase = TurnPhase.TowerResolve;
            return towerService.Build(State.Board.Tiles[tileIndex], definitionId);
        }

        public TowerState BuildTower(int tileIndex, TowerDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var tower = BuildTower(tileIndex, definition.Id);
            tower.Element = definition.Element;
            tower.LastResolvedRange = definition.Range;
            tower.LastResolvedDamage = definition.Damage;
            tower.LastResolvedTargetCount = definition.TargetCount;
            foreach (var effect in definition.BaseEffects ?? Array.Empty<TowerUpgradeEffect>())
                if (!string.IsNullOrWhiteSpace(effect.Id))
                {
                    if (!tower.AppliedEffectIds.Contains(effect.Id)) tower.AppliedEffectIds.Add(effect.Id);
                    tower.EffectValues[effect.Id] = effect.Value;
                }
            return tower;
        }

        public TowerState UpgradeTower(int tileIndex, TowerUpgradeDefinition upgrade)
            => ApplyTowerUpgrade(tileIndex, upgrade, true);

        private TowerState ApplyTowerUpgrade(int tileIndex, TowerUpgradeDefinition upgrade, bool consumeVisitGrant)
        {
            if (State.Board.Tiles[tileIndex].IsUpgradeRestricted)
                throw new InvalidOperationException("수풀 타일에서는 타워를 강화할 수 없습니다.");
            State.CurrentPhase = TurnPhase.TowerResolve;
            var tile = State.Board.Tiles[tileIndex];
            var tower = tile.Tower != null &&
                tile.Tower.UpgradeTier == 3 &&
                upgrade.Tier == 3
                    ? towerService.ApplyAdditionalTier3Upgrade(tile, upgrade.Id, upgrade.Tier, consumeVisitGrant)
                    : towerService.Upgrade(tile, upgrade.Id, upgrade.Tier);
            foreach (var effect in upgrade.Effects)
                if (!string.IsNullOrWhiteSpace(effect.Id))
                {
                    if (!tower.AppliedEffectIds.Contains(effect.Id)) tower.AppliedEffectIds.Add(effect.Id);
                    if (effect.Id == TowerEffectCatalog.Burn || effect.Id == TowerEffectCatalog.Frostbite)
                        tower.EffectValues[effect.Id] = tower.GetEffectValue(effect.Id) + effect.Value;
                    else
                        tower.EffectValues[effect.Id] = Math.Max(tower.GetEffectValue(effect.Id), effect.Value);
                }
            return tower;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveTowerCombat(
            System.Collections.Generic.IReadOnlyList<TowerDefinition> definitions,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            State.CurrentPhase = TurnPhase.TowerCombat;
            var stats = new System.Collections.Generic.Dictionary<int, TowerCombatStats>();
            foreach (var tile in State.Board.Tiles)
            {
                if (!tile.HasTower) continue;
                TowerDefinition definition = null;
                foreach (var candidate in definitions)
                    if (candidate != null && candidate.Id == tile.Tower.DefinitionId) { definition = candidate; break; }
                if (definition != null)
                {
                    var resolvedStats = BuildCombatStats(definition, tile, upgrades);
                    stats[tile.Tower.InstanceId] = resolvedStats;
                    tile.Tower.LastResolvedDamage = resolvedStats.Damage;
                }
            }
            var combined = new System.Collections.Generic.List<TowerAttackResult>();
            for (var tileIndex = State.Board.TileCount - 1; tileIndex >= 0; tileIndex--)
            {
                var tile = State.Board.Tiles[tileIndex];
                if (!tile.HasTower || !stats.TryGetValue(tile.Tower.InstanceId, out var towerStats))
                    continue;
                foreach (var result in ResolveLabTowerPhase(tileIndex, false, 1, 0, false, definitions, upgrades))
                    combined.Add(result);
            }
            ResolveBossVictory(combined);
            return combined;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveTestTowerAttack(
            int tileIndex,
            int attackCount,
            int preferredTargetInstanceId,
            bool ignoreRange,
            System.Collections.Generic.IReadOnlyList<TowerDefinition> definitions,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades)
            => ResolveSelectedTowerPhase(tileIndex, attackCount, preferredTargetInstanceId, ignoreRange, false, definitions, upgrades);

        /// <summary>One real tower phase, or an explicit forced override. Repetition belongs to the caller.</summary>
        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveLabTowerPhase(
            int tileIndex, bool force, int forcedCount, int preferredTargetInstanceId, bool ignoreRange,
            System.Collections.Generic.IReadOnlyList<TowerDefinition> definitions,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades) =>
            ResolveSelectedTowerPhase(tileIndex, force ? Math.Max(1, forcedCount) : (int?)null,
                preferredTargetInstanceId, force && ignoreRange, force, definitions, upgrades);

        private System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveSelectedTowerPhase(
            int tileIndex, int? overrideAttackCount, int preferredTargetInstanceId, bool ignoreRange, bool bypassRestrictions,
            System.Collections.Generic.IReadOnlyList<TowerDefinition> definitions,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileIndex));
            var tile = State.Board.Tiles[tileIndex];
            if (!tile.HasTower)
            { State.CombatTrace?.Add($"Tile {tileIndex}: no tower"); return System.Array.Empty<TowerAttackResult>(); }

            TowerDefinition definition = null;
            foreach (var candidate in definitions ?? System.Array.Empty<TowerDefinition>())
                if (candidate != null && candidate.Id == tile.Tower.DefinitionId)
                {
                    definition = candidate;
                    break;
                }
            if (definition == null) return System.Array.Empty<TowerAttackResult>();

            State.CurrentPhase = TurnPhase.TowerCombat;
            var calculation = CalculateTowerStats(definition, tile, upgrades);
            var resolved = calculation.CombatStats;
            State.CombatTrace?.Add($"Tile {tileIndex}, damage {calculation.DamageFormula} = {resolved.Damage}; range {resolved.Range}, targets {resolved.TargetCount}, " +
                $"attacks {resolved.AttackCount}, temporary +{tile.Tower.BonusAttackCount} (locked={tile.Tower.AttackCountLocked}). " +
                (overrideAttackCount.HasValue ? $"FORCED count {overrideAttackCount}, ignore range={ignoreRange}, bypass restrictions={bypassRestrictions}" : "ACTUAL rules"), tile.Tower.InstanceId);
            var stats = new TowerCombatStats(
                resolved.Damage,
                ignoreRange ? State.Board.TileCount : resolved.Range,
                resolved.TargetCount,
                overrideAttackCount ?? resolved.AttackCount);
            tile.Tower.LastResolvedDamage = stats.Damage;
            var byTower = new System.Collections.Generic.Dictionary<int, TowerCombatStats>
            {
                [tile.Tower.InstanceId] = stats
            };
            var combinedResults = new System.Collections.Generic.List<TowerAttackResult>();
            // Keep the same periodic -> ordinary -> stone order for single/all/normal gameplay.
            var periodic = towerEffectService.ResolvePeriodicAttack(State, tile.Tower);
            if (periodic.Count > 0)
                foreach (var hit in ResolveAttackEffects(periodic)) combinedResults.Add(hit);
            foreach (var hit in towerCombatService.ResolveByTower(State, byTower, ResolveAttackEffects,
                preferredTargetInstanceId, bypassRestrictions, overrideAttackCount.HasValue)) combinedResults.Add(hit);
            if (tile.Tower.StoneActive)
            {
                var stoneResults = new System.Collections.Generic.List<TowerAttackResult>();
                towerEffectService.ResolveStone(
                    State, tile.Tower, stoneResults, ResolveAttackEffects);
                combinedResults.AddRange(stoneResults);
            }
            ResolveBossVictory(combinedResults);
            return combinedResults;
        }

        private System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveAttackEffects(
            System.Collections.Generic.IReadOnlyList<TowerAttackResult> attacks)
        {
            var tileVisualChanges =
                new System.Collections.Generic.List<TileEffectVisualChange>();
            var effects = towerEffectService.ResolveAfterAttacks(
                State,
                attacks,
                tileVisualChanges);
            var combined = new System.Collections.Generic.List<TowerAttackResult>();
            var consumedEffects = new System.Collections.Generic.HashSet<int>();
            foreach (var attack in attacks)
            {
                var resolved = attack;
                for (var index = 0; index < effects.Count; index++)
                {
                    var effect = effects[index];
                    if (consumedEffects.Contains(index) || !effect.KnockbackApplied ||
                        effect.TowerInstanceId != attack.TowerInstanceId ||
                        effect.TargetInstanceId != attack.TargetInstanceId) continue;
                    resolved = effect.TeleportApplied
                        ? attack.WithTeleport(effect.KnockbackFromTile, effect.KnockbackToTile)
                        : attack.WithKnockback(effect.KnockbackFromTile, effect.KnockbackToTile);
                    consumedEffects.Add(index);
                    break;
                }
                combined.Add(resolved);
            }
            for (var index = 0; index < effects.Count; index++)
                if (!consumedEffects.Contains(index)) combined.Add(effects[index]);

            AttachTileEffectVisualChanges(combined, tileVisualChanges);
            return combined;
        }

        private static void AttachTileEffectVisualChanges(
            System.Collections.Generic.IList<TowerAttackResult> results,
            System.Collections.Generic.IReadOnlyList<TileEffectVisualChange> changes)
        {
            if (results == null || changes == null || changes.Count == 0)
                return;

            var assigned =
                new System.Collections.Generic.Dictionary<int,
                    System.Collections.Generic.List<TileEffectVisualChange>>();
            foreach (var change in changes)
            {
                var resultIndex = -1;
                for (var index = 0; index < results.Count; index++)
                {
                    var result = results[index];
                    if (result.TowerInstanceId != change.TowerInstanceId ||
                        result.VisualKind != change.TriggerVisualKind)
                        continue;
                    if (change.TriggerTileIndex >= 0 &&
                        result.TargetTileIndex != change.TriggerTileIndex)
                        continue;
                    resultIndex = index;
                    break;
                }

                if (resultIndex < 0)
                    for (var index = 0; index < results.Count; index++)
                        if (results[index].TowerInstanceId == change.TowerInstanceId)
                        {
                            resultIndex = index;
                            break;
                        }
                if (resultIndex < 0)
                    continue;

                if (!assigned.TryGetValue(resultIndex, out var list))
                {
                    list = new System.Collections.Generic.List<TileEffectVisualChange>();
                    assigned[resultIndex] = list;
                }
                list.Add(change);
            }

            foreach (var pair in assigned)
                results[pair.Key] =
                    results[pair.Key].WithTileEffectVisualChanges(pair.Value);
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveMonsterTurnEndEffects()
        {
            if (State.RegionGimmickEndedRound == State.Round) return Array.Empty<TowerAttackResult>();
            var results = towerEffectService.ResolveMonsterTurnEnd(State);
            regionGimmickService.EndTurn(State);
            ResolveBossVictory(results);
            return results;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveMonsterStandbyEffects()
        {
            var results = towerEffectService.ResolveMonsterStandby(State);
            ResolveBossVictory(results);
            return results;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> PlaceFireField(int tileIndex)
        {
            var results = new System.Collections.Generic.List<TowerAttackResult>(towerEffectService.PlaceFireField(State, tileIndex));
            towerEffectService.ResolveDeathEffects(State, results);
            State.Monsters.RemoveAll(monster => monster.IsDead);
            ResolveBossVictory(results);
            return results;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> PlaceIceField(int tileIndex)
        {
            var results = new System.Collections.Generic.List<TowerAttackResult>(towerEffectService.PlaceIceField(State, tileIndex));
            towerEffectService.ResolveDeathEffects(State, results);
            State.Monsters.RemoveAll(monster => monster.IsDead);
            ResolveBossVictory(results);
            return results;
        }

        public TowerStatCalculation CalculateTowerStats(
            TowerDefinition definition,
            TileState tile,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            var rateBonus = State.PermanentAllTowerDamageRateBonus +
                State.GetPermanentTowerDamageRateBonus(definition.Element) +
                State.GetPermanentLineTowerDamageRateBonus(MonsterService.GetLine(tile.Index)) +
                GetLineAuraDamageRateBonus(tile) + tile.Tower.PersonalDamageRateBonus +
                TowerUpgradeRules.ExtraDamageRate(State, tile);
            tile.Tower.Element = definition.Element;
            var calculation = TowerStatCalculator.Calculate(
                definition,
                tile.Tower,
                upgrades,
                rateBonus,
                State.GetPermanentTowerDamageFlatBonus(definition.Element) + tile.Tower.PersonalDamageFlatBonus,
                tile.Tower.PermanentBonusAttackCount + TowerUpgradeRules.ExtraAttackCount(State, tile.Tower),
                TowerUpgradeRules.ExtraBaseDamage(State, tile.Tower));
            tile.Tower.LastResolvedDamage = calculation.CombatStats.Damage;
            tile.Tower.LastResolvedRange = calculation.CombatStats.Range;
            tile.Tower.LastResolvedTargetCount = calculation.CombatStats.TargetCount;
            return calculation;
        }

        private TowerCombatStats BuildCombatStats(TowerDefinition definition, TileState tile,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades) =>
            CalculateTowerStats(definition, tile, upgrades).CombatStats;

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolvePlayerPathEffects(
            int start, int distance, bool teleport = false)
        {
            var results = towerEffectService.ResolvePlayerPath(State, start, distance, teleport);
            ResolveBossVictory(results);
            return results;
        }

        public void SetRegion(RegionDefinition region)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            regionGimmickService.Reset(State);
            CancelOasisUpgrade();
            currentRegion = region;
            State.CurrentRegionId = region.Id;
        }

        public bool TryConsumeRegionExtraRoll() => regionGimmickService.TryConsumeExtraRoll(State);
        public int ResolveRegionArrivalEffects(int tileIndex) => regionGimmickService.ResolveArrival(State, currentRegion, tileIndex);

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolvePlayerArrivalEffects(int tileIndex)
        {
            ResolveRegionArrivalEffects(tileIndex);
            return towerEffectService.ResolvePlayerArrival(State, tileIndex);
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveTurnStartEffects()
        {
            if (State.RegionGimmickStartedRound == State.Round) return Array.Empty<TowerAttackResult>();
            var results = new System.Collections.Generic.List<TowerAttackResult>(regionGimmickService.BeginTurn(State, currentRegion));
            results.AddRange(towerEffectService.ResolveTurnStart(State));
            ResolveBossVictory(results);
            return results;
        }

        // Manual testing advances the round at the NEXT start, never at the end button.
        // These guards also protect programmatic callers, not just disabled UI buttons.
        public bool CanBeginManualTurn => !State.IsFinished &&
            (State.RegionGimmickStartedRound != State.Round || State.RegionGimmickEndedRound == State.Round);
        public bool CanEndManualTurn => !State.IsFinished &&
            State.RegionGimmickStartedRound == State.Round && State.RegionGimmickEndedRound != State.Round;

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> BeginManualTurn()
        {
            if (!CanBeginManualTurn) throw new InvalidOperationException("이번 턴은 이미 시작되었습니다. 먼저 턴 종료를 실행하세요.");
            if (State.RegionGimmickEndedRound == State.Round) CompleteRound();
            State.CurrentPhase = TurnPhase.PlayerTurnStart;
            return ResolveTurnStartEffects();
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> EndManualTurn()
        {
            if (!CanEndManualTurn) throw new InvalidOperationException("진행 중인 턴이 없습니다. 먼저 턴 시작을 실행하세요.");
            CancelOasisUpgrade();
            State.CurrentPhase = TurnPhase.RoundEnd;
            return ResolveMonsterTurnEndEffects();
        }

        private float GetLineAuraDamageRateBonus(TileState targetTile)
        {
            var targetLine = MonsterService.GetLine(targetTile.Index);
            var rate = 0f;
            foreach (var tile in State.Board.Tiles)
                if (tile.HasTower && tile.Tower.InstanceId != targetTile.Tower.InstanceId &&
                    MonsterService.GetLine(tile.Index) == targetLine &&
                    tile.Tower.HasEffect(TowerEffectCatalog.LineTowerBuff))
                    rate += tile.Tower.GetEffectValue(TowerEffectCatalog.LineTowerBuff, 20f) / 100f;
            return rate;
        }

        public void AddPermanentLineTowerDamageRateBonus(int line, float amount) =>
            State.AddPermanentLineTowerDamageRateBonus(line, amount);

        public void AddPermanentTowerDamageRateBonus(TowerElement element, float amount) =>
            State.AddPermanentTowerDamageRateBonus(element, amount);

        public void AddPermanentTowerDamageFlatBonus(TowerElement element, int amount) =>
            State.AddPermanentTowerDamageFlatBonus(element, amount);

        public void AddPermanentAllTowerDamageRateBonus(float amount) =>
            State.AddPermanentAllTowerDamageRateBonus(amount);

        public void CompletePlayerLap()
        {
            State.Player.PathDamageBuffs.Clear();
            State.CompletedLaps++;
            State.AddPermanentAllTowerDamageRateBonus(.05f);
            foreach (var tile in State.Board.Tiles)
                if (tile.HasTower) tile.Tower.LapGrowthDamage +=
                    (int)Math.Round(tile.Tower.GetEffectValue("lap_damage_stack"));
        }

        public void TeleportPlayer(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
            State.Player.CurrentTileIndex = tileIndex;
            State.Player.ArrivalSerial++;
        }

        public bool ResolvePlayerBonusTile(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileIndex));

            // Concrete bonus handlers will be dispatched from here once their
            // effect definitions are finalized.
            return State.Board.Tiles[tileIndex].IsBonusTile;
        }


public DiceState CreateLapReward()
        {
            return DiceCatalog.GetReward(Math.Max(0, State.Round - 1));
        }

        public DiceState[] CreateLapRewardChoices()
        {
            var first = CreateLapReward();
            var second = CreateLapReward();
            for (var attempt = 0; attempt < 12 && second != null && first != null && second.Id == first.Id; attempt++)
                second = CreateLapReward();
            return new[] { first, second };
        }

        public bool StoreDiceReward(DiceState reward) =>
            State.DiceInventory.TryStoreReward(reward);

        public bool ReplaceReserveDice(int inventoryIndex, DiceState reward) =>
            State.DiceInventory.Replace(inventoryIndex, reward, State.Dice);

        public bool QueueDiceEquip(int slotIndex, int reserveIndex) =>
            State.DiceInventory.QueueEquip(State.Dice, slotIndex, reserveIndex);


public bool CanRollDice =>
            State.Dice.Count == DiceInventoryState.EquippedCount &&
            State.Dice[0] != null && State.Dice[1] != null;
}
}
