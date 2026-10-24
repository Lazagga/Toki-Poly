using System;
using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;

namespace GaeBullBing.Core.Towers
{
    public enum TowerAttackVisualKind
    {
        None,
        Projectile,
        AreaTile,
        ChainLine,
        ChainTile,
        RollingStone
    }

    public enum TileEffectVisualKind
    {
        Normal,
        Fire,
        Ice
    }

    public readonly struct TileEffectVisualChange
    {
        public TileEffectVisualChange(
            int towerInstanceId,
            int tileIndex,
            TileEffectVisualKind effect,
            int triggerTileIndex,
            TowerAttackVisualKind triggerVisualKind)
        {
            TowerInstanceId = towerInstanceId;
            TileIndex = tileIndex;
            Effect = effect;
            TriggerTileIndex = triggerTileIndex;
            TriggerVisualKind = triggerVisualKind;
        }

        public int TowerInstanceId { get; }
        public int TileIndex { get; }
        public TileEffectVisualKind Effect { get; }
        public int TriggerTileIndex { get; }
        public TowerAttackVisualKind TriggerVisualKind { get; }
    }

    public readonly struct TowerCombatStats
    {
        public TowerCombatStats(int damage, int range, int targetCount, int attackCount = 1)
        {
            Damage = damage;
            Range = range;
            TargetCount = targetCount;
            AttackCount = attackCount;
        }

        public int Damage { get; }
        public int Range { get; }
        public int TargetCount { get; }
        public int AttackCount { get; }
    }

    public readonly struct TowerAttackResult
    {
        public TowerAttackResult(int towerInstanceId, int targetInstanceId, float damage, bool killed,
            bool knockbackApplied = false, int knockbackFromTile = -1, int knockbackToTile = -1,
            int targetTileIndex = -1, TowerAttackVisualKind visualKind = TowerAttackVisualKind.None,
            IReadOnlyList<TileEffectVisualChange> tileEffectVisualChanges = null,
            int preAttackBurnStacks = 0, int preAttackFrostbiteStacks = 0,
            bool preAttackFrozen = false, bool preAttackShocked = false,
            float preAttackHealthRatio = 1f, bool preAttackFireTile = false,
            bool teleportApplied = false)
        {
            TowerInstanceId = towerInstanceId;
            TargetInstanceId = targetInstanceId;
            Damage = damage;
            Killed = killed;
            KnockbackApplied = knockbackApplied;
            KnockbackFromTile = knockbackFromTile;
            KnockbackToTile = knockbackToTile;
            TargetTileIndex = targetTileIndex;
            VisualKind = visualKind;
            TileEffectVisualChanges = tileEffectVisualChanges ?? Array.Empty<TileEffectVisualChange>();
            PreAttackBurnStacks = preAttackBurnStacks;
            PreAttackFrostbiteStacks = preAttackFrostbiteStacks;
            PreAttackFrozen = preAttackFrozen;
            PreAttackShocked = preAttackShocked;
            PreAttackHealthRatio = preAttackHealthRatio;
            PreAttackFireTile = preAttackFireTile;
            TeleportApplied = teleportApplied;
        }

        public int TowerInstanceId { get; }
        public int TargetInstanceId { get; }
        public float Damage { get; }
        public bool Killed { get; }
        public bool KnockbackApplied { get; }
        public int KnockbackFromTile { get; }
        public int KnockbackToTile { get; }
        public int TargetTileIndex { get; }
        public TowerAttackVisualKind VisualKind { get; }
        public IReadOnlyList<TileEffectVisualChange> TileEffectVisualChanges { get; }
        public int PreAttackBurnStacks { get; }
        public int PreAttackFrostbiteStacks { get; }
        public bool PreAttackFrozen { get; }
        public bool PreAttackShocked { get; }
        public float PreAttackHealthRatio { get; }
        public bool PreAttackFireTile { get; }
        public bool TeleportApplied { get; }
        public TowerAttackResult WithTeleport(int fromTile, int toTile) =>
            new(TowerInstanceId, TargetInstanceId, Damage, Killed, true, fromTile, toTile,
                TargetTileIndex, VisualKind, TileEffectVisualChanges,
                PreAttackBurnStacks, PreAttackFrostbiteStacks, PreAttackFrozen,
                PreAttackShocked, PreAttackHealthRatio, PreAttackFireTile, true);
        public TowerAttackResult WithKnockback(int fromTile, int toTile) =>
            new(TowerInstanceId, TargetInstanceId, Damage, Killed, true, fromTile, toTile,
                TargetTileIndex, VisualKind, TileEffectVisualChanges,
                PreAttackBurnStacks, PreAttackFrostbiteStacks, PreAttackFrozen,
                PreAttackShocked, PreAttackHealthRatio, PreAttackFireTile, TeleportApplied);
        public TowerAttackResult WithTileEffectVisualChanges(
            IReadOnlyList<TileEffectVisualChange> changes) =>
            new(TowerInstanceId, TargetInstanceId, Damage, Killed, KnockbackApplied,
                KnockbackFromTile, KnockbackToTile, TargetTileIndex, VisualKind, changes,
                PreAttackBurnStacks, PreAttackFrostbiteStacks, PreAttackFrozen,
                PreAttackShocked, PreAttackHealthRatio, PreAttackFireTile, TeleportApplied);
    }

    public sealed class TowerCombatService
    {
        public IReadOnlyList<TowerAttackResult> Resolve(
            GameState state,
            IReadOnlyDictionary<string, TowerCombatStats> statsByDefinitionId)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (statsByDefinitionId == null)
                throw new ArgumentNullException(nameof(statsByDefinitionId));

            var results = new List<TowerAttackResult>();
            // Monsters advance clockwise from low to high tile indices, so towers
            // nearer the end of the route resolve first.
            for (var tileIndex = state.Board.Tiles.Count - 1; tileIndex >= 0; tileIndex--)
            {
                var tile = state.Board.Tiles[tileIndex];
                if (!tile.HasTower || tile.Tower.IsFeatherSealed ||
                    !statsByDefinitionId.TryGetValue(tile.Tower.DefinitionId, out var stats))
                    continue;

                for (var attack = 0; attack < Math.Max(1, stats.AttackCount); attack++)
                    ResolveTowerAttack(state, tile, stats, results);
            }
            return results;
        }

        public IReadOnlyList<TowerAttackResult> ResolveByTower(
            GameState state,
            IReadOnlyDictionary<int, TowerCombatStats> statsByTowerInstanceId,
            Func<IReadOnlyList<TowerAttackResult>, IReadOnlyList<TowerAttackResult>> effectResolver = null,
            int preferredTargetInstanceId = 0,
            bool bypassRestrictions = false,
            bool attackCountIncludesBonuses = false)
        {
            var results = new List<TowerAttackResult>();
            ActivatePendingAttackBonuses(state);
            for (var tileIndex = state.Board.Tiles.Count - 1; tileIndex >= 0; tileIndex--)
            {
                var tile = state.Board.Tiles[tileIndex];
                if (!tile.HasTower ||
                    !statsByTowerInstanceId.TryGetValue(tile.Tower.InstanceId, out var stats))
                    continue;
                var tower = tile.Tower; var upgrades = tower.AppliedUpgradeIds;
                tower.LastResolvedDamage = stats.Damage;
                tower.LastResolvedRange = stats.Range;
                tower.LastResolvedTargetCount = stats.TargetCount;
                tower.AttacksThisTurn = 0;
                tower.ActiveChargeBonus = 0f;
                var cooldownBlocked = tower.AttackCooldownRounds > 0;
                if (cooldownBlocked && !bypassRestrictions) tower.AttackCooldownRounds--;
                if (!bypassRestrictions && (tower.IsFeatherSealed || cooldownBlocked))
                {
                    state.CombatTrace?.Add(tower.IsFeatherSealed ? "No ordinary attack: feather sealed" : "No ordinary attack: cooldown", tower.InstanceId);
                    continue;
                }
                if (!bypassRestrictions && tower.HasEffect("overheat_cycle") && tower.OverheatAttackTurns >=
                    Math.Max(1, (int)Math.Round(tower.GetEffectValue("overheat_cycle"))))
                {
                    tower.OverheatAttackTurns = 0;
                    state.CombatTrace?.Add("No ordinary attack: overheat rest phase", tower.InstanceId);
                    continue;
                }
                var hasTargetInRange = state.Monsters.Exists(m => !m.IsDead &&
                    GetBoardDistance(tile.Index, m.CurrentTileIndex) <= stats.Range);
                if (HasEffect(tower, TowerEffectCatalog.ChainLine))
                    hasTargetInRange = state.Monsters.Exists(m => !m.IsDead &&
                        MonsterService.GetLine(tile.Index) == MonsterService.GetLine(m.CurrentTileIndex));
                if (tower.HasEffect("charge_damage") && !hasTargetInRange)
                {
                    tower.ChargeCount = Math.Min((int)Math.Round(tower.GetEffectValue("charge_cap", 3f)),
                        tower.ChargeCount + 1);
                    state.CombatTrace?.Add($"No target in range: charge -> {tower.ChargeCount}", tower.InstanceId);
                }
                if (!hasTargetInRange && !HasEffect(tower, TowerEffectCatalog.RollingStone) &&
                    !HasEffect(tower, TowerEffectCatalog.ChainLine) && !HasEffect(tower, TowerEffectCatalog.ChainLightning))
                {
                    state.CombatTrace?.Add("No ordinary attack: no living target in range", tower.InstanceId);
                    continue;
                }
                if (HasEffect(tower, TowerEffectCatalog.RollingStone) && !tower.StoneActive)
                {
                    TowerUpgradeRules.BeginVolley(tower);
                    tower.StoneActive = true;
                    tower.StoneTileIndex = tile.Index;
                    tower.StoneDamageMultiplier = 1f;
                    tower.StoneBaseDamage = stats.Damage;
                    tower.StoneExitAnimation = StoneExitAnimation.None;
                    tower.StoneExitTileIndex = -1;
                    FinishTowerAttack(tower);
                    continue;
                }
                if (HasEffect(tower, TowerEffectCatalog.Wall) ||
                    HasEffect(tower, TowerEffectCatalog.LineTowerBuff))
                {
                    state.CombatTrace?.Add("No ordinary attack: passive wall / line-buff tower", tower.InstanceId);
                    continue;
                }
                var volleyCount = Math.Max(1, stats.AttackCount +
                    (attackCountIncludesBonuses || tower.AttackCountLocked ? 0 : tower.BonusAttackCount));
                state.CombatTrace?.Add($"Ordinary attack volleys {volleyCount}; targeting " +
                    (preferredTargetInstanceId > 0 ? $"preferred #{preferredTargetInstanceId} (if eligible)" : "automatic: boss > distance > HP"), tower.InstanceId);
                if (HasEffect(tower, TowerEffectCatalog.ChainLine))
                {
                    var attacked = false;
                    for (var attack = 0; attack < volleyCount; attack++)
                    {
                        TowerUpgradeRules.BeginVolley(tower);
                        var batch = new List<TowerAttackResult>();
                        ResolveLineAttack(state, tile, stats.Damage, batch);
                        if (batch.Count == 0) continue;
                        attacked = true;
                        BuffAttackedTileTowers(state, batch, 0, tower);
                        CommitBatch(results, batch, effectResolver);
                    }
                    if (attacked)
                    {
                        FinishTowerAttack(tower);
                    }
                    continue;
                }
                if (HasEffect(tower, TowerEffectCatalog.ChainLightning))
                {
                    TowerUpgradeRules.BeginVolley(tower);
                    var batch = new List<TowerAttackResult>();
                    ResolveRandomElectric(state, tile, stats.Damage,
                        Math.Max(1, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.ChainLightning, 3f))), batch);
                    if (batch.Count > 0)
                    {
                        BuffAttackedTileTowers(state, batch, 0, tower);
                        CommitBatch(results, batch, effectResolver);
                        FinishTowerAttack(tower);
                    }
                    continue;
                }
                var attackedNormally = false;
                for (var attack = 0; attack < volleyCount; attack++)
                {
                    TowerUpgradeRules.BeginVolley(tower);
                    var batch = new List<TowerAttackResult>();
                    if (HasEffect(tower, TowerEffectCatalog.Explode))
                        ResolveExplodeAttack(state, tile, stats, batch, preferredTargetInstanceId);
                    else if (HasEffect(tower, TowerEffectCatalog.RangeAttack))
                        ResolveRangeAttack(state, tile, stats, batch);
                    else if (HasEffect(tower, TowerEffectCatalog.AreaTile))
                        ResolveTileAreaAttack(state, tile, stats, batch, preferredTargetInstanceId);
                    else
                        ResolveTowerAttack(state,tile,stats,batch,preferredTargetInstanceId);
                    if (batch.Count == 0) continue;
                    attackedNormally = true;
                    BuffAttackedTileTowers(state, batch, 0, tower);
                    CommitBatch(results, batch, effectResolver);
                }
                if (attackedNormally)
                {
                    FinishTowerAttack(tower);
                }
            }
            return results;
        }

        private static void CommitBatch(
            ICollection<TowerAttackResult> destination,
            IReadOnlyList<TowerAttackResult> batch,
            Func<IReadOnlyList<TowerAttackResult>, IReadOnlyList<TowerAttackResult>> effectResolver)
        {
            var resolved = effectResolver != null ? effectResolver(batch) : batch;
            foreach (var result in resolved) destination.Add(result);
        }

        private static void FinishTowerAttack(TowerState tower)
        {
            if (tower.HasEffect("overheat_cycle")) tower.OverheatAttackTurns++;
            if (HasEffect(tower, TowerEffectCatalog.Cooldown))
                tower.AttackCooldownRounds = Math.Max(1,
                    (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.Cooldown, 2f)));
            if (tower.BonusAttackTurnsRemaining <= 0) return;
            tower.BonusAttackTurnsRemaining--;
            if (tower.BonusAttackTurnsRemaining == 0) tower.BonusAttackCount = 0;
        }

        private static void ResolvePhysicsGuard(GameState s,TileState tile,int damage,ICollection<TowerAttackResult> r)
        { foreach(var m in new List<MonsterState>(s.Monsters)) if(!m.IsBoss&&!m.IsDead&&m.CurrentTileIndex==tile.Index&&m.PhysicsGuardTriggeredThisTurn){m.PhysicsGuardTriggeredThisTurn=false;Damage(s,m,damage,tile.Tower.InstanceId,r,TowerAttackVisualKind.Projectile);} }
        private static void ResolveLineAttack(GameState s,TileState tile,int damage,ICollection<TowerAttackResult> r)
        { var line=MonsterService.GetLine(tile.Index);foreach(var m in new List<MonsterState>(s.Monsters))if(!m.IsDead&&MonsterService.GetLine(m.CurrentTileIndex)==line)Damage(s,m,damage,tile.Tower.InstanceId,r,TowerAttackVisualKind.ChainLine); }
        private static readonly Random EffectRandom=new Random();
        private static void ResolveRandomElectric(GameState s,TileState source,int damage,int attackCount,ICollection<TowerAttackResult> r)
        {
            var tiles=s.Board.Tiles.FindAll(t=>t.HasTower&&t.Tower.DefinitionId=="TOW_04"&&t.Tower.InstanceId!=source.Tower.InstanceId);
            if(tiles.Count==0)return;
            var occupied=tiles.FindAll(t=>s.Monsters.Exists(m=>!m.IsDead&&m.CurrentTileIndex==t.Index));
            var candidates=occupied.Count>0?occupied:tiles;
            for(var i=0;i<attackCount;i++)
            {
                var chosen=candidates[EffectRandom.Next(candidates.Count)];
                foreach(var m in new List<MonsterState>(s.Monsters))if(m.CurrentTileIndex==chosen.Index)Damage(s,m,damage,source.Tower.InstanceId,r,TowerAttackVisualKind.Projectile);
            }
        }
        private static void ActivatePendingAttackBonuses(GameState state)
        {
            foreach (var tile in state.Board.Tiles)
            {
                if (!tile.HasTower || tile.Tower.PendingBonusAttackCount <= 0) continue;
                tile.Tower.BonusAttackCount = Math.Max(
                    tile.Tower.BonusAttackCount, tile.Tower.PendingBonusAttackCount);
                tile.Tower.BonusAttackTurnsRemaining = Math.Max(
                    tile.Tower.BonusAttackTurnsRemaining, tile.Tower.PendingBonusAttackTurns);
                tile.Tower.PendingBonusAttackCount = 0;
                tile.Tower.PendingBonusAttackTurns = 0;
            }
        }

        private static void BuffAttackedTileTowers(
            GameState state,
            IReadOnlyList<TowerAttackResult> results,
            int firstResultIndex,
            TowerState source)
        {
            if (!HasEffect(source, TowerEffectCatalog.TowerBuff)) return;
            var bonus = Math.Max(1, (int)Math.Round(
                source.GetEffectValue(TowerEffectCatalog.TowerBuff, 1f)));
            var affectedTiles = new HashSet<int>();
            for (var index = firstResultIndex; index < results.Count; index++)
            {
                var tileIndex = results[index].TargetTileIndex;
                if (tileIndex < 0 || tileIndex >= state.Board.TileCount ||
                    !affectedTiles.Add(tileIndex)) continue;
                var targetTile = state.Board.Tiles[tileIndex];
                if (!targetTile.HasTower) continue;
                targetTile.Tower.PendingBonusAttackCount = Math.Max(
                    targetTile.Tower.PendingBonusAttackCount, bonus);
                targetTile.Tower.PendingBonusAttackTurns = Math.Max(
                    targetTile.Tower.PendingBonusAttackTurns, 2);
            }
        }
        private static void Damage(GameState state,MonsterState monster,float damage,int tower,ICollection<TowerAttackResult> results,
            TowerAttackVisualKind visualKind = TowerAttackVisualKind.None)
        {
            var source = TowerUpgradeRules.FindTower(state, tower)?.Tower;
            if (source == null || monster.IsDead) return;
            results.Add(TowerUpgradeRules.PrimaryHit(state, source, monster, damage, visualKind));
        }

        private static void ResolveTowerAttack(
            GameState state,
            TileState tile,
            TowerCombatStats stats,
            ICollection<TowerAttackResult> results,
            int preferredTargetInstanceId = 0)
        {
            var tower = tile.Tower;
            var candidates = new List<MonsterState>();
            foreach (var monster in state.Monsters)
            {
                if (!monster.IsDead && GetBoardDistance(tile.Index, monster.CurrentTileIndex) <= stats.Range)
                    candidates.Add(monster);
            }

            candidates.Sort(CompareTargets);
            MovePreferredTargetFirst(candidates, preferredTargetInstanceId);
            tower.TargetInstanceIds.Clear();

            var selectedCount = Math.Min(stats.TargetCount, candidates.Count);
            for (var index = 0; index < selectedCount; index++)
            {
                var target = candidates[index];
                Damage(state, target, stats.Damage, tower.InstanceId, results, TowerAttackVisualKind.Projectile);
                if (!target.IsDead)
                    tower.TargetInstanceIds.Add(target.InstanceId);
            }

        }

        private static void ResolveTileAreaAttack(
            GameState state,
            TileState tile,
            TowerCombatStats stats,
            ICollection<TowerAttackResult> results,
            int preferredTargetInstanceId = 0)
        {
            var tower = tile.Tower;
            var candidates = new List<MonsterState>();
            foreach (var monster in state.Monsters)
                if (!monster.IsDead && GetBoardDistance(tile.Index, monster.CurrentTileIndex) <= stats.Range)
                    candidates.Add(monster);
            candidates.Sort(CompareTargets);
            MovePreferredTargetFirst(candidates, preferredTargetInstanceId);

            var selectedTiles = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                if (selectedTiles.Count >= stats.TargetCount) break;
                selectedTiles.Add(candidate.CurrentTileIndex);
            }

            tower.TargetInstanceIds.Clear();
            AddAreaTileMarkers(tower.InstanceId, selectedTiles, results);
            foreach (var monster in new List<MonsterState>(state.Monsters))
            {
                if (monster.IsDead || !selectedTiles.Contains(monster.CurrentTileIndex)) continue;
                Damage(state, monster, stats.Damage, tile.Tower.InstanceId, results);
                if (!monster.IsDead) tile.Tower.TargetInstanceIds.Add(monster.InstanceId);
            }
        }

        private static void ResolveExplodeAttack(
            GameState state,
            TileState tile,
            TowerCombatStats stats,
            ICollection<TowerAttackResult> results,
            int preferredTargetInstanceId = 0)
        {
            var candidates = new List<MonsterState>();
            foreach (var monster in state.Monsters)
                if (!monster.IsDead && GetBoardDistance(tile.Index, monster.CurrentTileIndex) <= stats.Range)
                    candidates.Add(monster);
            candidates.Sort(CompareTargets);
            MovePreferredTargetFirst(candidates, preferredTargetInstanceId);

            var attackedTiles = new HashSet<int>();
            for (var index = 0; index < Math.Min(stats.TargetCount, candidates.Count); index++)
            {
                var center = candidates[index].CurrentTileIndex;
                attackedTiles.Add(center);
                attackedTiles.Add((center + state.Board.TileCount - 1) % state.Board.TileCount);
                attackedTiles.Add((center + 1) % state.Board.TileCount);
            }

            tile.Tower.TargetInstanceIds.Clear();
            AddAreaTileMarkers(tile.Tower.InstanceId, attackedTiles, results);
            foreach (var monster in new List<MonsterState>(state.Monsters))
            {
                if (monster.IsDead || !attackedTiles.Contains(monster.CurrentTileIndex)) continue;
                Damage(state, monster, stats.Damage, tile.Tower.InstanceId, results);
                if (!monster.IsDead) tile.Tower.TargetInstanceIds.Add(monster.InstanceId);
            }
        }

        private static void ResolveRangeAttack(
            GameState state,
            TileState tile,
            TowerCombatStats stats,
            ICollection<TowerAttackResult> results)
        {
            var targets = new List<MonsterState>();
            var attackedTiles = new HashSet<int>();
            foreach (var monster in state.Monsters)
                if (!monster.IsDead && GetBoardDistance(tile.Index, monster.CurrentTileIndex) <= stats.Range)
                {
                    targets.Add(monster);
                    attackedTiles.Add(monster.CurrentTileIndex);
                }
            targets.Sort(CompareTargets);
            AddAreaTileMarkers(tile.Tower.InstanceId, attackedTiles, results);
            tile.Tower.TargetInstanceIds.Clear();
            foreach (var monster in targets)
            {
                Damage(state, monster, stats.Damage, tile.Tower.InstanceId, results);
                if (!monster.IsDead) tile.Tower.TargetInstanceIds.Add(monster.InstanceId);
            }
        }

        private static void AddAreaTileMarkers(
            int towerInstanceId,
            IEnumerable<int> tileIndices,
            ICollection<TowerAttackResult> results)
        {
            foreach (var tileIndex in tileIndices)
                results.Add(new TowerAttackResult(towerInstanceId, 0, 0f, false,
                    targetTileIndex: tileIndex, visualKind: TowerAttackVisualKind.AreaTile));
        }

        private static bool HasEffect(TowerState tower, string effectId) =>
            tower != null && tower.HasEffect(effectId);

        private static int CompareTargets(MonsterState left, MonsterState right)
        {
            var comparison = right.IsBoss.CompareTo(left.IsBoss);
            if (comparison != 0)
                return comparison;

            comparison = right.DistanceTravelled.CompareTo(left.DistanceTravelled);
            if (comparison != 0)
                return comparison;

            comparison = right.CurrentHealth.CompareTo(left.CurrentHealth);
            return comparison != 0 ? comparison : left.InstanceId.CompareTo(right.InstanceId);
        }

        private static void MovePreferredTargetFirst(
            List<MonsterState> candidates, int preferredTargetInstanceId)
        {
            if (preferredTargetInstanceId <= 0) return;
            var index = candidates.FindIndex(monster =>
                monster.InstanceId == preferredTargetInstanceId);
            if (index <= 0) return;
            var preferred = candidates[index];
            candidates.RemoveAt(index);
            candidates.Insert(0, preferred);
        }

        public static int GetBoardDistance(int firstTileIndex, int secondTileIndex)
        {
            var directDistance = Math.Abs(firstTileIndex - secondTileIndex);
            return Math.Min(directDistance, BoardState.DefaultTileCount - directDistance);
        }
    }
}
