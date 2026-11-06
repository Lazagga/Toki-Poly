using System;
using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Monsters
{
    public readonly struct MonsterMoveResult
    {
        public MonsterMoveResult(int instanceId, int startTileIndex, int distance, bool reachedBase,
            bool isBoss = false, IReadOnlyList<BossFeatherEvent> featherEvents = null,
            IReadOnlyList<TowerAttackResult> tileEffectResults = null)
        {
            InstanceId = instanceId;
            StartTileIndex = startTileIndex;
            Distance = distance;
            ReachedBase = reachedBase;
            IsBoss = isBoss;
            FeatherEvents = featherEvents ?? Array.Empty<BossFeatherEvent>();
            TileEffectResults = tileEffectResults ?? Array.Empty<TowerAttackResult>();
        }

        public int InstanceId { get; }
        public int StartTileIndex { get; }
        public int Distance { get; }
        public bool ReachedBase { get; }
        public bool IsBoss { get; }
        public IReadOnlyList<BossFeatherEvent> FeatherEvents { get; }
        public IReadOnlyList<TowerAttackResult> TileEffectResults { get; }

        public MonsterMoveResult WithFeatherEvents(IReadOnlyList<BossFeatherEvent> events) =>
            new(InstanceId, StartTileIndex, Distance, ReachedBase, IsBoss, events, TileEffectResults);
    }

    public enum BossFeatherEventType { Drop, Recover }

    public readonly struct BossFeatherEvent
    {
        public BossFeatherEvent(BossFeatherEventType type, int tileIndex, int stepOffset)
        {
            Type = type;
            TileIndex = tileIndex;
            StepOffset = stepOffset;
        }

        public BossFeatherEventType Type { get; }
        public int TileIndex { get; }
        public int StepOffset { get; }
    }

    public sealed class MonsterService
    {
        private int nextInstanceId = 1;

        public MonsterState Spawn(GameState state, MonsterDefinition definition, float healthMultiplier = 1f)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var maxHealth = Math.Max(1f, definition.MaxHp *
                (definition.Tier == MonsterTier.Boss ? 1f : healthMultiplier));
            var monster = new MonsterState
            {
                InstanceId = nextInstanceId++,
                DefinitionId = definition.Id,
                Tier = definition.Tier,
                CurrentHealth = maxHealth,
                MaxHealth = maxHealth,
                BaseDefense = definition.BaseDefense,
                CurrentTileIndex = 0,
                MoveDistance = definition.MoveDistance,
                DistanceTravelled = 0,
                IsNewlySpawned = true
            };
            monster.PassiveRules = state.MonsterPassiveRules;
            foreach (var immunity in definition.StatusImmunities ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(immunity)) monster.StatusImmunities.Add(immunity);
            foreach (var passive in definition.PassiveEffects ?? Array.Empty<TowerUpgradeEffect>())
                if (!string.IsNullOrWhiteSpace(passive.Id))
                    monster.PassiveEffects[passive.Id] = passive.Value;
            monster.BeginPassiveTurn(state.Round);
            state.Monsters.Add(monster);
            return monster;
        }

        public IReadOnlyList<MonsterMoveResult> MoveAll(
            GameState state,
            IReadOnlyDictionary<int, float> wallDamageByTowerInstanceId = null,
            IReadOnlyList<TowerDefinition> definitions = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            state.Monsters.Sort((left, right) => right.DistanceTravelled.CompareTo(left.DistanceTravelled));
            var results = new List<MonsterMoveResult>(state.Monsters.Count);
            var reachedBase = new List<MonsterState>();

            foreach (var monster in state.Monsters)
            {
                monster.BeginPassiveTurn(state.Round);
                if (monster.FreezeImmuneThisTurn)
                    monster.FreezeImmuneThisTurn = false;
                if (monster.FreezeImmunityPending)
                {
                    monster.FreezeImmunityPending = false;
                    monster.FreezeImmuneThisTurn = true;
                }
                var remainingToBase = BoardState.DefaultTileCount - monster.DistanceTravelled;
                var startTileIndex = monster.CurrentTileIndex;
                if (monster.KnockbackConsumed && monster.KnockbackImmunityLine >= 0 &&
                    GetLine(startTileIndex) != monster.KnockbackImmunityLine)
                    monster.KnockbackConsumed = false;
                monster.TouchedFireThisMove = false;
                var tileEffects = new List<TowerAttackResult>();
                if (monster.IsNewlySpawned)
                {
                    ApplyFireTile(state, monster, monster.CurrentTileIndex, tileEffects);
                    monster.IsNewlySpawned = false;
                }
                var cannotMove = monster.IsDead || monster.FrozenMovesRemaining > 0 ||
                                 monster.StunnedMovesRemaining > 0 || monster.IsPeriodicallyStopped || monster.MoveDistance <= 0;
                if (monster.FrozenMovesRemaining > 0)
                {
                    monster.FrozenMovesRemaining--;
                    if (monster.FrozenMovesRemaining == 0)
                        monster.FreezeImmunityPending = true;
                }
                if (monster.StunnedMovesRemaining > 0) monster.StunnedMovesRemaining--;

                var onIce = !monster.IgnoresMovementTileEffects && HasIce(state, monster.CurrentTileIndex);
                var startTile = startTileIndex < state.Board.TileCount ? state.Board.Tiles[startTileIndex] : null;
                var waterBonus = WaterBonus(monster, startTile);
                var speed = monster.MoveDistance + waterBonus;
                var iceSlide = onIce ? Math.Max(0, (int)monster.GetPassiveValue("ice_overlay_slide")) : 0;
                var plannedDistance = cannotMove
                    ? 0
                    : Math.Min(onIce ? Math.Max(1, iceSlide + 1) :
                        (monster.NextMoveReduction > 0 && speed > 0
                            ? Math.Max(1, speed - monster.NextMoveReduction) : speed), remainingToBase);
                if (!cannotMove) monster.NextMoveReduction = 0;
                var distance = 0;
                while (distance < plannedDistance && !monster.IsDead)
                {
                    LeaveWaterTrail(state, monster, (startTileIndex + distance) % BoardState.DefaultTileCount);
                    distance++;
                    var enteredTileIndex = (startTileIndex + distance) % BoardState.DefaultTileCount;
                    var entered = enteredTileIndex < state.Board.TileCount ? state.Board.Tiles[enteredTileIndex] : null;
                    if (!monster.IgnoresMovementTileEffects && entered != null && entered.HasOvergrowth &&
                        (!monster.PassiveRules.BushHealOncePerTurn || !monster.BushHealedThisTurn) &&
                        monster.GetPassiveValue("bush_pass_heal_percent") > 0)
                    {
                        var basis = monster.HasPassive("bush_pass_heal_use_max_hp") ? monster.MaxHealth : monster.CurrentHealth;
                        monster.CurrentHealth = Math.Min(monster.MaxHealth, monster.CurrentHealth +
                            basis * Math.Max(0f, monster.GetPassiveValue("bush_pass_heal_percent")) / 100f);
                        monster.BushHealedThisTurn = true;
                    }
                    if (monster.PassiveRules.WaterTrailBonusOnEntry && !onIce)
                    {
                        var enteredBonus = WaterBonus(monster, entered);
                        if (enteredBonus > waterBonus)
                            plannedDistance = Math.Min(remainingToBase, plannedDistance + enteredBonus - waterBonus);
                        waterBonus = Math.Max(waterBonus, enteredBonus);
                    }
                    // Ground bosses still touch intermediate tiles. Only flying/hopping movement skips them.
                    if (!monster.SkipsIntermediateTiles || distance == plannedDistance)
                    {
                        if (ResolveEnteredTile(state, monster, enteredTileIndex,
                            wallDamageByTowerInstanceId, tileEffects, !monster.SkipsIntermediateTiles)) break;
                    }
                    if (!monster.SkipsIntermediateTiles && !monster.IgnoresMovementTileEffects &&
                        HasIce(state, enteredTileIndex) && distance < plannedDistance)
                        plannedDistance = Math.Min(plannedDistance, distance + 1);
                    if (monster.PassiveRules.SnatchOnEachEnteredTile && !monster.IsDead)
                        ResolveTowerSnatch(state, monster, enteredTileIndex, definitions);
                }

                monster.DistanceTravelled += distance;
                monster.CurrentTileIndex = (monster.CurrentTileIndex + distance) % BoardState.DefaultTileCount;
                if (monster.KnockbackConsumed && monster.KnockbackImmunityLine >= 0 &&
                    GetLine(monster.CurrentTileIndex) != monster.KnockbackImmunityLine)
                    monster.KnockbackConsumed = false;
                monster.OnIceOverlay = HasIce(state, monster.CurrentTileIndex);
                if (distance > 0 && !monster.IsDead && !monster.PassiveRules.SnatchOnEachEnteredTile)
                    ResolveTowerSnatch(state, monster, monster.CurrentTileIndex, definitions);
                var hasReachedBase = monster.DistanceTravelled >= BoardState.DefaultTileCount;
                results.Add(new MonsterMoveResult(monster.InstanceId, startTileIndex, distance, hasReachedBase,
                    monster.IsBoss, tileEffectResults: tileEffects));

                if (hasReachedBase)
                    reachedBase.Add(monster);
            }

            if (results.Count > 0)
            {
                var deathEffects = new List<TowerAttackResult>();
                new TowerEffectService().ResolveDeathEffects(state, deathEffects);
                if (deathEffects.Count > 0)
                {
                    var last = results[results.Count - 1];
                    var combined = new List<TowerAttackResult>(last.TileEffectResults);
                    combined.AddRange(deathEffects);
                    results[results.Count - 1] = new MonsterMoveResult(last.InstanceId,
                        last.StartTileIndex, last.Distance, last.ReachedBase, last.IsBoss,
                        last.FeatherEvents, combined);
                }
            }
            foreach (var monster in reachedBase)
            {
                ReleaseCarriedTowers(state, monster);
                state.Monsters.Remove(monster);
            }
            state.Monsters.RemoveAll(monster => monster.IsDead);

            return results;
        }

        private static int WaterBonus(MonsterState monster, TileState tile) =>
            tile != null && tile.WaterTrailTurnsRemaining > 0 && !monster.IgnoresMovementTileEffects &&
            (monster.PassiveRules.WaterTrailAffectsCreators || !monster.HasPassive("water_trail_duration_turns"))
                ? tile.WaterTrailMoveSpeedBonus : 0;

        private static void LeaveWaterTrail(GameState state, MonsterState monster, int tileIndex)
        {
            var duration = Math.Max(0, (int)monster.GetPassiveValue("water_trail_duration_turns"));
            if (duration == 0 || tileIndex >= state.Board.TileCount) return;
            var tile = state.Board.Tiles[tileIndex];
            tile.WaterTrailTurnsRemaining = Math.Max(tile.WaterTrailTurnsRemaining, duration + 1);
            tile.WaterTrailMoveSpeedBonus = Math.Max(tile.WaterTrailMoveSpeedBonus,
                Math.Max(0, (int)monster.GetPassiveValue("water_trail_move_speed_bonus")));
        }

        private static bool CanDrop(MonsterState monster, TowerState tower, TileState tile) =>
            monster.PassiveRules.AllowCrossElementTowerDrop || tile.BuildTowerDefinitionId == tower.DefinitionId;

        private static void ResolveTowerSnatch(GameState state, MonsterState monster, int tileIndex,
            IReadOnlyList<TowerDefinition> definitions)
        {
            var capacity = Math.Max(0, (int)monster.GetPassiveValue("tower_snatch_count"));
            if (capacity == 0 || tileIndex >= state.Board.TileCount) return;
            var tile = state.Board.Tiles[tileIndex];
            var original = tile.Tower;
            if (monster.CarriedTowers.Count > 0 && monster.HasPassive("tower_snatch_swap_release"))
            {
                if (!CanDrop(monster, monster.CarriedTowers[0].Tower, tile)) return;
                tile.Tower = monster.CarriedTowers[0].Tower;
                monster.CarriedTowers.RemoveAt(0);
            }
            else if (monster.CarriedTowers.Count >= capacity) return;
            else tile.Tower = null;
            if (original != null)
            {
                if (!monster.PassiveRules.PreserveSnatchedTowerUpgrades)
                {
                    TowerDefinition definition = null;
                    foreach (var candidate in definitions ?? Array.Empty<TowerDefinition>())
                        if (candidate.Id == original.DefinitionId) { definition = candidate; break; }
                    if (definition != null)
                    {
                        original.UpgradeTier = 1;
                        original.AppliedUpgradeIds.Clear(); original.AppliedEffectIds.Clear(); original.EffectValues.Clear();
                        original.AdditionalTier3UpgradesClaimed = 0;
                        foreach (var effect in definition.BaseEffects)
                        { original.AppliedEffectIds.Add(effect.Id); original.EffectValues[effect.Id] = effect.Value; }
                        original.AttackCooldownRounds = original.OverheatAttackTurns = original.ChargeCount = 0;
                        original.PeriodicTurnCount = 0; original.EffectReadyPhases.Clear();
                        original.StoneActive = false;
                    }
                }
                monster.CarriedTowers.Add(new CarriedTower { Tower = original, OriginTileIndex = tile.Index });
            }
        }

        public static void ReleaseCarriedTowers(GameState state, MonsterState monster)
        {
            foreach (var carried in new List<CarriedTower>(monster.CarriedTowers))
            {
                var index = monster.PassiveRules.ReturnCarriedTowersToOrigin ? carried.OriginTileIndex : monster.CurrentTileIndex;
                var destination = index < state.Board.TileCount ? state.Board.Tiles[index] : null;
                if (destination == null || destination.HasTower || !CanDrop(monster, carried.Tower, destination))
                    destination = state.Board.Tiles.Find(t => t.CanBuildTower && !t.HasTower && CanDrop(monster, carried.Tower, t));
                if (destination == null) continue;
                destination.Tower = carried.Tower;
                monster.CarriedTowers.Remove(carried);
            }
            if (monster.CarriedTowers.Count > 0)
            {
                if (!state.PendingTowerReturns.Contains(monster)) state.PendingTowerReturns.Add(monster);
            }
            else state.PendingTowerReturns.Remove(monster);
        }

        private static bool HasIce(GameState state, int tileIndex) =>
            tileIndex >= 0 && tileIndex < state.Board.Tiles.Count &&
            state.Board.Tiles[tileIndex].IceTurnsRemaining > 0;

        private static bool ResolveEnteredTile(
            GameState state,
            MonsterState monster,
            int tileIndex,
            IReadOnlyDictionary<int, float> wallDamageByTowerInstanceId,
            ICollection<TowerAttackResult> results,
            bool stopsGroundMovement)
        {
            if (monster.IgnoresMovementTileEffects) return false;
            ApplyFireTile(state, monster, tileIndex, results);
            if (monster.IsDead || tileIndex < 0 || tileIndex >= state.Board.Tiles.Count) return monster.IsDead;
            var tower = state.Board.Tiles[tileIndex].Tower;
            if (tower == null || !tower.HasEffect(TowerEffectCatalog.Wall)) return false;
            var damage = wallDamageByTowerInstanceId != null &&
                         wallDamageByTowerInstanceId.TryGetValue(tower.InstanceId, out var configured)
                ? configured
                : 0f;
            var previousTile = monster.CurrentTileIndex;
            monster.CurrentTileIndex = tileIndex;
            var actual = TowerUpgradeRules.Receive(state, monster, damage, tower.InstanceId);
            monster.CurrentTileIndex = previousTile;
            results.Add(new TowerAttackResult(tower.InstanceId, monster.InstanceId, actual, monster.IsDead,
                targetTileIndex: tileIndex, visualKind: TowerAttackVisualKind.Projectile));
            return stopsGroundMovement;
        }

        private static void ApplyFireTile(
            GameState state,
            MonsterState monster,
            int tileIndex,
            ICollection<TowerAttackResult> results)
        {
            if (monster.IgnoresMovementTileEffects || tileIndex < 0 || tileIndex >= state.Board.Tiles.Count ||
                state.Board.Tiles[tileIndex].FireTurnsRemaining <= 0)
                return;
            TowerUpgradeRules.AddBurn(state, monster, 1, state.Board.Tiles[tileIndex].FireSourceTowerId);
            monster.TouchedFireThisMove = true;
            TowerUpgradeRules.BurnTick(state, monster, tileIndex, results);
        }

        public static int GetLine(int tileIndex)
        {
            if (tileIndex <= 9) return 0;
            if (tileIndex <= 18) return 1;
            if (tileIndex <= 27) return 2;
            return 3;
        }
    }
}
