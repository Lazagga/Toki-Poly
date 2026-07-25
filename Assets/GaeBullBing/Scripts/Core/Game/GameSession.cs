using System;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Dice;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Game
{
    public sealed class GameSession
    {
        private readonly BoardService boardService;
        private readonly PlayerMovementService movementService;
        private readonly WeightedDiceRoller diceRoller;
        private readonly MonsterService monsterService;
        private readonly TowerService towerService;
        private readonly TowerCombatService towerCombatService;
        private readonly TowerEffectService towerEffectService = new();
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

        public void StartNewGame(int diceCount = 2, BoardDefinition boardDefinition = null)
        {
            if (diceCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(diceCount));

            if (boardDefinition != null)
                boardService.Initialize(State.Board, boardDefinition);
            else
                boardService.Initialize(State.Board);
            State.DiceInventory.ResetToDefaults();
            State.Dice.Clear();
            State.Dice.Add(State.DiceInventory.Dice[0]);
            State.Dice.Add(State.DiceInventory.Dice[1]);

            State.Player.CurrentTileIndex = 0;
            State.Player.DicePoints = 0;
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

        public int RollDiceAndMovePlayer()
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
            var completedLap = State.Player.CurrentTileIndex + distance >= State.Board.TileCount;
            movementService.Move(State.Player, State.Board, distance);
            if (completedLap)
            {
                State.CompletedLaps++;
                State.AddPermanentAllTowerDamageRateBonus(.05f);
            }
            State.CurrentPhase = TurnPhase.TileResolve;
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

            State.CurrentPhase = TurnPhase.RoundEnd;
            State.Round++;
            State.CurrentPhase = TurnPhase.PlayerTurnStart;
        }

        public TowerState BuildTower(int tileIndex, string definitionId)
        {
            State.CurrentPhase = TurnPhase.TowerResolve;
            return towerService.Build(State.Board.Tiles[tileIndex], definitionId);
        }

        public TowerState BuildTower(int tileIndex, TowerDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var tower = BuildTower(tileIndex, definition.Id);
            foreach (var effectId in definition.BaseEffectIds ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(effectId) && !tower.AppliedEffectIds.Contains(effectId))
                    tower.AppliedEffectIds.Add(effectId);
            return tower;
        }

        public TowerState UpgradeTower(int tileIndex, TowerUpgradeDefinition upgrade)
        {
            State.CurrentPhase = TurnPhase.TowerResolve;
            var tower = towerService.Upgrade(State.Board.Tiles[tileIndex], upgrade.Id, upgrade.Tier);
            foreach (var effect in upgrade.Effects)
                if (!string.IsNullOrWhiteSpace(effect.Id))
                {
                    if (!tower.AppliedEffectIds.Contains(effect.Id)) tower.AppliedEffectIds.Add(effect.Id);
                    tower.EffectValues[effect.Id] = effect.Value;
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
                var singleTowerStats = new System.Collections.Generic.Dictionary<int, TowerCombatStats>
                {
                    [tile.Tower.InstanceId] = towerStats
                };
                var attacks = towerCombatService.ResolveByTower(State, singleTowerStats);
                foreach (var result in ResolveAttackEffects(attacks)) combined.Add(result);
                if (tile.Tower.StoneActive)
                {
                    var stoneAttacks = new System.Collections.Generic.List<TowerAttackResult>();
                    towerEffectService.ResolveStone(State, tile.Tower, stoneAttacks);
                    foreach (var result in ResolveAttackEffects(stoneAttacks)) combined.Add(result);
                }
            }
            ResolveBossVictory(combined);
            return combined;
        }

        private System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveAttackEffects(
            System.Collections.Generic.IReadOnlyList<TowerAttackResult> attacks)
        {
            var effects = towerEffectService.ResolveAfterAttacks(State, attacks);
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
                    resolved = attack.WithKnockback(effect.KnockbackFromTile, effect.KnockbackToTile);
                    consumedEffects.Add(index);
                    break;
                }
                combined.Add(resolved);
            }
            for (var index = 0; index < effects.Count; index++)
                if (!consumedEffects.Contains(index)) combined.Add(effects[index]);
            return combined;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> ResolveMonsterTurnEndEffects()
        {
            var results = towerEffectService.ResolveMonsterTurnEnd(State);
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
            var results = towerEffectService.PlaceFireField(State, tileIndex);
            ResolveBossVictory(results);
            return results;
        }

        public System.Collections.Generic.IReadOnlyList<TowerAttackResult> PlaceIceField(int tileIndex)
        {
            var results = towerEffectService.PlaceIceField(State, tileIndex);
            ResolveBossVictory(results);
            return results;
        }

        private TowerCombatStats BuildCombatStats(
            TowerDefinition definition,
            TileState tile,
            System.Collections.Generic.IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            var rateBonus = State.PermanentAllTowerDamageRateBonus +
                State.GetPermanentTowerDamageRateBonus(definition.Element) +
                State.GetPermanentLineTowerDamageRateBonus(MonsterService.GetLine(tile.Index)) +
                GetLineAuraDamageRateBonus(tile);
            return TowerStatCalculator.Calculate(
                definition,
                tile.Tower,
                upgrades,
                rateBonus,
                State.GetPermanentTowerDamageFlatBonus(definition.Element)).CombatStats;
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

        public void TeleportPlayer(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= State.Board.TileCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
            State.Player.CurrentTileIndex = tileIndex;
        }


public DiceState CreateLapReward()
        {
            return DiceCatalog.GetReward(Math.Max(0, State.Round - 1));
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
