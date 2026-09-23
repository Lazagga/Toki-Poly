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
            foreach (var immunity in definition.StatusImmunities ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(immunity)) monster.StatusImmunities.Add(immunity);
            state.Monsters.Add(monster);
            return monster;
        }

        public IReadOnlyList<MonsterMoveResult> MoveAll(
            GameState state,
            IReadOnlyDictionary<int, float> wallDamageByTowerInstanceId = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            state.Monsters.Sort((left, right) => right.DistanceTravelled.CompareTo(left.DistanceTravelled));
            var results = new List<MonsterMoveResult>(state.Monsters.Count);
            var reachedBase = new List<MonsterState>();

            foreach (var monster in state.Monsters)
            {
                if (monster.FreezeImmuneThisTurn)
                    monster.FreezeImmuneThisTurn = false;
                if (monster.FreezeImmunityPending)
                {
                    monster.FreezeImmunityPending = false;
                    monster.FreezeImmuneThisTurn = true;
                }
                var remainingToBase = BoardState.DefaultTileCount - monster.DistanceTravelled;
                var startTileIndex = monster.CurrentTileIndex;
                monster.TouchedFireThisMove = false;
                var tileEffects = new List<TowerAttackResult>();
                if (monster.IsNewlySpawned)
                {
                    ApplyFireTile(state, monster, monster.CurrentTileIndex, tileEffects);
                    monster.IsNewlySpawned = false;
                }
                var cannotMove = monster.IsDead || monster.FrozenMovesRemaining > 0 ||
                                 monster.StunnedMovesRemaining > 0;
                if (monster.FrozenMovesRemaining > 0)
                {
                    monster.FrozenMovesRemaining--;
                    if (monster.FrozenMovesRemaining == 0)
                        monster.FreezeImmunityPending = true;
                }
                if (monster.StunnedMovesRemaining > 0) monster.StunnedMovesRemaining--;

                var onIce = HasIce(state, monster.CurrentTileIndex);
                var plannedDistance = cannotMove ? 0 : Math.Min(onIce ? 1 : monster.MoveDistance, remainingToBase);
                var distance = 0;
                if (monster.IsBoss)
                {
                    distance = plannedDistance;
                    if (distance > 0)
                    {
                        var destination = (startTileIndex + distance) % BoardState.DefaultTileCount;
                        ResolveEnteredTile(state, monster, destination, wallDamageByTowerInstanceId,
                            tileEffects, false);
                    }
                }
                else
                {
                    while (distance < plannedDistance && !monster.IsDead)
                    {
                        distance++;
                        var enteredTileIndex = (startTileIndex + distance) % BoardState.DefaultTileCount;
                        if (ResolveEnteredTile(state, monster, enteredTileIndex,
                                wallDamageByTowerInstanceId, tileEffects, true))
                        {
                            plannedDistance = distance;
                            break;
                        }
                        if (HasIce(state, enteredTileIndex) && distance < plannedDistance)
                            plannedDistance = Math.Min(plannedDistance, distance + 1);
                    }
                }

                monster.DistanceTravelled += distance;
                monster.CurrentTileIndex = (monster.CurrentTileIndex + distance) % BoardState.DefaultTileCount;
                var hasReachedBase = monster.DistanceTravelled >= BoardState.DefaultTileCount;
                results.Add(new MonsterMoveResult(monster.InstanceId, startTileIndex, distance, hasReachedBase,
                    monster.IsBoss, tileEffectResults: tileEffects));

                if (hasReachedBase)
                    reachedBase.Add(monster);
            }

            foreach (var monster in reachedBase)
                state.Monsters.Remove(monster);
            state.Monsters.RemoveAll(monster => monster.IsDead);

            return results;
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
            ApplyFireTile(state, monster, tileIndex, results);
            if (monster.IsDead || tileIndex < 0 || tileIndex >= state.Board.Tiles.Count) return monster.IsDead;
            var tower = state.Board.Tiles[tileIndex].Tower;
            if (tower == null || !tower.HasEffect(TowerEffectCatalog.Wall)) return false;
            var damage = wallDamageByTowerInstanceId != null &&
                         wallDamageByTowerInstanceId.TryGetValue(tower.InstanceId, out var configured)
                ? configured
                : 0f;
            var actual = monster.ReceiveDamage(damage, state.Difficulty);
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
            if (tileIndex < 0 || tileIndex >= state.Board.Tiles.Count ||
                state.Board.Tiles[tileIndex].FireTurnsRemaining <= 0)
                return;
            monster.BurnStacks++;
            monster.TouchedFireThisMove = true;
            var damage = monster.MaxHealth * .0025f * monster.BurnStacks;
            var actual = monster.ReceiveDamage(damage, state.Difficulty);
            results.Add(new TowerAttackResult(0, monster.InstanceId, actual, monster.IsDead,
                targetTileIndex: tileIndex));
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
