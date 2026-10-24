using System;
using System.Collections.Generic;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;

namespace GaeBullBing.Core.Towers
{
    public sealed partial class TowerEffectService
    {
        private ICollection<TileEffectVisualChange> activeVisualChanges;
        private HashSet<int> activeFieldCollisionTiles;
        private int activeTriggerTileIndex = -1;
        private TowerAttackVisualKind activeTriggerVisualKind = TowerAttackVisualKind.None;

        public IReadOnlyList<TowerAttackResult> PlaceFireField(GameState state, int tileIndex, int sourceTowerInstanceId = 0) =>
            PlaceTileField(state, tileIndex, true, sourceTowerInstanceId);

        public IReadOnlyList<TowerAttackResult> PlaceIceField(GameState state, int tileIndex, int sourceTowerInstanceId = 0) =>
            PlaceTileField(state, tileIndex, false, sourceTowerInstanceId);

        public IReadOnlyList<TowerAttackResult> ResolveAfterAttacks(
            GameState state,
            IReadOnlyList<TowerAttackResult> attacks,
            ICollection<TileEffectVisualChange> visualChanges = null)
        {
            activeVisualChanges = visualChanges;
            activeFieldCollisionTiles = new HashSet<int>();
            frostbiteBurstThisResolution.Clear();
            var extra = new List<TowerAttackResult>();
            activeTriggerTileIndex = -1;
            activeTriggerVisualKind = TowerAttackVisualKind.ChainLine;
            SpreadChainLineFieldsFromSnapshot(state, attacks, extra);
            foreach (var attack in attacks)
            {
                activeTriggerTileIndex = attack.TargetTileIndex;
                activeTriggerVisualKind = attack.VisualKind;
                // Area markers carry every tile covered by an area attack. They do not deal
                // damage again, but tile-field traits must use the same attacked tile set.
                if (attack.VisualKind == TowerAttackVisualKind.AreaTile)
                {
                    var markerTowerTile = FindTowerTile(state, attack.TowerInstanceId);
                    if (markerTowerTile != null && attack.TargetTileIndex >= 0 &&
                        attack.TargetTileIndex < state.Board.TileCount)
                    {
                        var markerTower = markerTowerTile.Tower;
                        if (HasEffect(markerTower, TowerEffectCatalog.TileBurn))
                            PlaceFields(state, new[] { attack.TargetTileIndex }, true,
                                attack.TowerInstanceId, extra);
                        if (HasEffect(markerTower, TowerEffectCatalog.TileFreeze))
                            PlaceFields(state, new[] { attack.TargetTileIndex }, false,
                                attack.TowerInstanceId, extra);
                    }
                    continue;
                }
                var towerTile = FindTowerTile(state, attack.TowerInstanceId); if (towerTile == null) continue;
                var target = FindMonster(state, attack.TargetInstanceId);
                var tower = towerTile.Tower;
                var attackTileIndex = attack.TargetTileIndex >= 0
                    ? attack.TargetTileIndex
                    : target != null ? target.CurrentTileIndex : -1;
                if (attackTileIndex < 0 || attackTileIndex >= state.Board.TileCount) continue;
                var attackedTiles = new HashSet<int> { attackTileIndex };
                if (HasEffect(tower, TowerEffectCatalog.FieldSpread) &&
                    attack.VisualKind != TowerAttackVisualKind.ChainLine)
                    SpreadTileField(state, attackTileIndex,
                        1 + Math.Max(0, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f))),
                        attack.TowerInstanceId, extra);
                // Phase 3: base persistent effects resolve after the primary hit.
                if (target != null)
                    ApplyBasePersistentEffects(state, tower, target, extra);
                if (target != null && !target.IsDead &&
                    HasEffect(tower, TowerEffectCatalog.StatusTransfer))
                    SpreadStatuses(state, tower, target, 1, extra);
                // Phase 4: non-conditional upgrade on-hit effects.
                using var hitTrace = CombatTrace.StepFor(state, 4, "upgrade on-hit", tower.InstanceId, target);
                if (target != null)
                    ApplyUpgradeOnHitEffects(state, tower, target);
                if (target != null && !attack.KnockbackApplied && !target.IsImmuneTo("knockback") &&
                    HasEffect(tower, TowerEffectCatalog.Knockback) &&
                    !target.KnockbackConsumed)
                {
                    KnockbackMonster(state, tower, target,
                        (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.Knockback, 1f)), extra);
                }
                if (HasEffect(tower, TowerEffectCatalog.ChainTile))
                {
                    var chainDistance = Math.Max(1, (int)Math.Round(
                        tower.GetEffectValue(TowerEffectCatalog.ChainTile, 3f)));
                    extra.Add(new TowerAttackResult(attack.TowerInstanceId, -1, 0f, false,
                        targetTileIndex: attackTileIndex, visualKind: TowerAttackVisualKind.ChainTile));
                    for (var distance = 1; distance <= chainDistance; distance++)
                    {
                        var chainedTile = (attackTileIndex - distance + state.Board.TileCount) % state.Board.TileCount;
                        attackedTiles.Add(chainedTile);
                        activeTriggerTileIndex = chainedTile;
                        activeTriggerVisualKind = TowerAttackVisualKind.ChainTile;
                        if (HasEffect(tower, TowerEffectCatalog.FieldSpread))
                            SpreadTileField(state, chainedTile,
                                1 + Math.Max(0, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f))),
                                attack.TowerInstanceId, extra);
                        var resultCountBeforeTile = extra.Count;
                        ResolveFollowupTile(state, tower, chainedTile, tower.LastResolvedDamage > 0 ? tower.LastResolvedDamage : attack.Damage,
                            TowerAttackVisualKind.ChainTile, extra);
                        if (extra.Count == resultCountBeforeTile)
                            extra.Add(new TowerAttackResult(attack.TowerInstanceId, -1, 0f, false,
                                targetTileIndex: chainedTile, visualKind: TowerAttackVisualKind.ChainTile));
                    }
                }
                activeTriggerTileIndex = attackTileIndex;
                activeTriggerVisualKind = attack.VisualKind;
                if (HasEffect(tower, TowerEffectCatalog.TileBurn))
                    PlaceAttackFields(state, attackedTiles, true, attack, tower, extra);
                if (HasEffect(tower, TowerEffectCatalog.TileFreeze))
                    PlaceAttackFields(state, attackedTiles, false, attack, tower, extra);
                if (target == null) continue;
                if (!target.IsDead && HasEffect(tower, TowerEffectCatalog.TileBreak) && state.Board.Tiles[target.CurrentTileIndex].IceTurnsRemaining>0)
                {
                    state.Board.Tiles[target.CurrentTileIndex].IceTurnsRemaining=0;
                    AddAreaTileMarkers(attack.TowerInstanceId, new[] { target.CurrentTileIndex }, extra);
                    DamageTile(state,target.CurrentTileIndex,attack.Damage,attack.TowerInstanceId,extra,
                        TowerAttackVisualKind.None);
                    ApplyHitEffectsOnTile(state, tower, target.CurrentTileIndex, extra);
                }
                // Phase 5: conditional effects use the state captured before this attack.
                ApplyAdditionalOnHitEffects(state, tower, target, attack, extra);
                hitTrace?.Dispose();
                using var conditionalTrace = CombatTrace.StepFor(state, 5, "conditional follow-up", tower.InstanceId, target);
                if (!target.IsDead && HasEffect(tower, TowerEffectCatalog.FreezeDamageMultiply) &&
                    attack.PreAttackFrozen)
                {
                    using var effectTrace = CombatTrace.StepFor(state, 5, TowerEffectCatalog.FreezeDamageMultiply, tower.InstanceId, target);
                    ApplyDamage(state, target, tower.LastResolvedDamage * Math.Max(0f,
                        tower.GetEffectValue(TowerEffectCatalog.FreezeDamageMultiply, 5f) - 1f),
                        attack.TowerInstanceId, extra);
                }
                if (!target.IsDead && HasEffect(tower, TowerEffectCatalog.BurnDamage) &&
                    attack.PreAttackBurnStacks > 0)
                {
                    using var effectTrace = CombatTrace.StepFor(state, 5, TowerEffectCatalog.BurnDamage, tower.InstanceId, target);
                    ApplyDamage(state, target, tower.LastResolvedDamage *
                        tower.GetEffectValue(TowerEffectCatalog.BurnDamage, .2f) * attack.PreAttackBurnStacks,
                        attack.TowerInstanceId, extra);
                }
                if (HasEffect(tower, TowerEffectCatalog.BurnExplode) &&
                    attack.PreAttackBurnStacks >= Math.Max(1, (int)Math.Round(
                        tower.GetEffectValue(TowerEffectCatalog.BurnExplode, 20f))))
                {
                    using var effectTrace = CombatTrace.StepFor(state, 5, TowerEffectCatalog.BurnExplode, tower.InstanceId, target);
                    AddAreaTiles(state, attackTileIndex, 1, attackedTiles);
                    AddAreaTileMarkers(attack.TowerInstanceId, attackedTiles, extra);
                    DamageArea(state, attackTileIndex, tower.LastResolvedDamage,
                        attack.TowerInstanceId, extra);
                }
                ApplyAdditionalConditionalEffects(state, tower, target, attack, extra);
                if (tower.HasEffect("pierce") && attack.TargetInstanceId > 0)
                {
                    for (var offset = 1; offset <= (int)Math.Round(tower.GetEffectValue("pierce")); offset++)
                    {
                        var piercedTile = attackTileIndex - offset;
                        if (piercedTile < TowerUpgradeRules.LineStart(attackTileIndex)) break;
                        ResolveFollowupTile(state, tower, piercedTile, tower.LastResolvedDamage,
                            TowerAttackVisualKind.Projectile, extra);
                    }
                }
            }
            var triggeredTowers = new HashSet<int>();
            foreach (var attack in attacks)
            {
                var tower = FindTowerTile(state, attack.TowerInstanceId)?.Tower;
                if (tower != null && triggeredTowers.Add(tower.InstanceId))
                    ApplyLineKnockback(state, tower, attacks, extra);
            }
            ApplyAttackedTileBuffs(state, attacks);
            ResolveDeathEffects(state, extra);
            state.Monsters.RemoveAll(m => m.IsDead);
            activeVisualChanges = null;
            activeFieldCollisionTiles = null;
            return extra;
        }

        private void PlaceAttackFields(
            GameState state,
            IEnumerable<int> attackedTiles,
            bool placeFire,
            TowerAttackResult attack,
            TowerState tower,
            ICollection<TowerAttackResult> results)
        {
            var tiles = new List<int>(attackedTiles);
            var revealIndividually = HasEffect(tower, TowerEffectCatalog.ChainTile);
            var revealAsArea = !revealIndividually && tiles.Count > 1;
            foreach (var tileIndex in tiles)
            {
                activeTriggerTileIndex = revealIndividually || revealAsArea
                    ? tileIndex
                    : attack.TargetTileIndex;
                activeTriggerVisualKind = revealIndividually
                    ? TowerAttackVisualKind.ChainTile
                    : revealAsArea
                        ? TowerAttackVisualKind.AreaTile
                        : attack.VisualKind;
                var fieldResults = placeFire
                    ? PlaceFireField(state, tileIndex, attack.TowerInstanceId)
                    : PlaceIceField(state, tileIndex, attack.TowerInstanceId);
                foreach (var result in fieldResults)
                    results.Add(result);
            }
        }

        public IReadOnlyList<TowerAttackResult> ResolveMonsterTurnEnd(GameState state)
        {
            var results = new List<TowerAttackResult>();
            foreach (var monster in new List<MonsterState>(state.Monsters))
            {
                if (!monster.DamageStoreMarked) continue;
                var damage = monster.StoredDamage * monster.DamageStoreMultiplier;
                var sourceId = monster.DamageStoreSourceTowerId;
                using var trace = CombatTrace.StepFor(state, 0, "damage_store release", sourceId, monster);
                monster.DamageStoreMarked = false;
                monster.StoredDamage = 0f;
                SecondaryHit(state, monster, damage, sourceId, results);
            }
            ResolveDeathEffects(state, results);
            state.Monsters.RemoveAll(monster => monster.IsDead);
            foreach (var tile in state.Board.Tiles)
                if (tile.HasTower) tile.Tower.TemporaryDamageBuffs.RemoveAll(buff => buff.ExpiresAfterRound <= state.Round);
            foreach (var tile in state.Board.Tiles) { if (tile.FireTurnsRemaining > 0) tile.FireTurnsRemaining--; if (tile.IceTurnsRemaining > 0) tile.IceTurnsRemaining--; }
            return results;
        }

        public IReadOnlyList<TowerAttackResult> ResolveMonsterStandby(GameState state)
        {
            var results = new List<TowerAttackResult>();
            var monsters = new List<MonsterState>(state.Monsters);
            monsters.Sort((left, right) => right.DistanceTravelled.CompareTo(left.DistanceTravelled));
            foreach (var monster in monsters)
            {
                if (monster.KnockbackImmunityPending)
                {
                    monster.KnockbackImmunityPending = false;
                    monster.KnockbackConsumed = true;
                }

                // Existing burn resolves before the current tile creates another stack.
                if (monster.BurnStacks > 0)
                    ApplyBurnTick(state, monster, results);
                if (monster.IsDead) continue;

                var tile = state.Board.Tiles[monster.CurrentTileIndex];
                if (tile.FireTurnsRemaining > 0)
                {
                    TowerUpgradeRules.AddBurn(state, monster, 1, tile.FireSourceTowerId);
                    ApplyBurnTick(state, monster, results);
                }
                monster.TouchedFireThisMove = false;
            }
            ResolveDeathEffects(state, results);
            state.Monsters.RemoveAll(monster => monster.IsDead);
            return results;
        }

        private static float BurnDamage(MonsterState m) => m.MaxHealth * .0025f * m.BurnStacks;
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
        private static void AddAreaTiles(
            GameState state,
            int centerTileIndex,
            int radius,
            ISet<int> tiles)
        {
            var tileCount = state.Board.TileCount;
            for (var offset = -radius; offset <= radius; offset++)
                tiles.Add((centerTileIndex + offset + tileCount) % tileCount);
        }

        private void PlaceFields(
            GameState state,
            IEnumerable<int> tileIndices,
            bool placeFire,
            int sourceTowerInstanceId,
            ICollection<TowerAttackResult> results)
        {
            foreach (var tileIndex in tileIndices)
            {
                var fieldResults = placeFire
                    ? PlaceFireField(state, tileIndex, sourceTowerInstanceId)
                    : PlaceIceField(state, tileIndex, sourceTowerInstanceId);
                foreach (var result in fieldResults)
                    results.Add(result);
            }
        }

private void SpreadTileField(
            GameState state,
            int sourceTileIndex,
            int radius,
            int sourceTowerInstanceId,
            ICollection<TowerAttackResult> results)
        {
            var sourceTile = state.Board.Tiles[sourceTileIndex];
            if (sourceTile.FireTurnsRemaining <= 0 && sourceTile.IceTurnsRemaining <= 0) return;
            var spreadTiles = new HashSet<int>();
            AddAreaTiles(state, sourceTileIndex, radius, spreadTiles);
            PlaceFields(state, spreadTiles, sourceTile.FireTurnsRemaining > 0,
                sourceTowerInstanceId, results);
        }

        private IReadOnlyList<TowerAttackResult> PlaceTileField(
            GameState state,
            int tileIndex,
            bool placeFire,
            int sourceTowerInstanceId)
        {
            var results = new List<TowerAttackResult>();
            var tile = state.Board.Tiles[tileIndex];
            state.CombatTrace?.Add($"Tile {tileIndex}: place {(placeFire ? "fire" : "ice")}, previous fire {tile.FireTurnsRemaining} / ice {tile.IceTurnsRemaining}", sourceTowerInstanceId);
            if (activeFieldCollisionTiles != null &&
                activeFieldCollisionTiles.Contains(tileIndex))
            {
                RecordTileVisualChange(
                    sourceTowerInstanceId,
                    tileIndex,
                    TileEffectVisualKind.Normal);
                return results;
            }
            var collides = placeFire ? tile.IceTurnsRemaining > 0 : tile.FireTurnsRemaining > 0;
            if (collides)
            {
                tile.FireTurnsRemaining = 0;
                tile.IceTurnsRemaining = 0;
                activeFieldCollisionTiles?.Add(tileIndex);
                RecordTileVisualChange(
                    sourceTowerInstanceId,
                    tileIndex,
                    TileEffectVisualKind.Normal);
                DamageTile(state, tileIndex, 15 + state.Difficulty.Level * 14, sourceTowerInstanceId, results);
                return results;
            }

            if (placeFire)
            {
                var duration = Board.TileState.OneTurnEffectDuration;
                foreach (var candidate in state.Board.Tiles)
                    if (candidate.HasTower && MonsterService.GetLine(candidate.Index) == MonsterService.GetLine(tileIndex))
                        duration += Math.Max(0, (int)Math.Round(candidate.Tower.GetEffectValue("line_fire_tile_duration_add")));
                tile.FireTurnsRemaining = duration;
                tile.FireSourceTowerId = sourceTowerInstanceId;
            }
            else tile.IceTurnsRemaining = Board.TileState.OneTurnEffectDuration;
            RecordTileVisualChange(
                sourceTowerInstanceId,
                tileIndex,
                placeFire ? TileEffectVisualKind.Fire : TileEffectVisualKind.Ice);
            return results;
        }

        private void RecordTileVisualChange(
            int towerInstanceId,
            int tileIndex,
            TileEffectVisualKind effect)
        {
            activeVisualChanges?.Add(new TileEffectVisualChange(
                towerInstanceId,
                tileIndex,
                effect,
                activeTriggerTileIndex,
                activeTriggerVisualKind));
        }
        private static void ApplyDamage(GameState state, MonsterState monster, float damage, int tower, ICollection<TowerAttackResult> results,
            TowerAttackVisualKind visualKind = TowerAttackVisualKind.Projectile)
        {
            var actual = TowerUpgradeRules.Receive(state, monster, damage, tower);
            results.Add(new TowerAttackResult(tower,monster.InstanceId,actual,monster.IsDead,targetTileIndex:monster.CurrentTileIndex,visualKind:tower>0?visualKind:TowerAttackVisualKind.None));
        }

        private void ApplyBasePersistentEffects(
            GameState state,
            TowerState tower,
            MonsterState target,
            ICollection<TowerAttackResult> results)
        {
            using var trace = CombatTrace.StepFor(state, 3, "base persistent effects", tower?.InstanceId ?? 0, target);
            if (tower == null || target == null || target.IsDead) return;
            if (tower.HasEffect("burn_max_stack_add"))
                target.MaxBurnStacks = Math.Max(target.MaxBurnStacks, 20 + (int)Math.Round(tower.GetEffectValue("burn_max_stack_add")));
            if (HasEffect(tower, TowerEffectCatalog.Burn))
                TowerUpgradeRules.AddBurn(state, target, Math.Max(1, (int)Math.Round(
                    tower.GetEffectValue(TowerEffectCatalog.Burn, 1f))), tower.InstanceId);
            if (HasEffect(tower, TowerEffectCatalog.Frostbite))
            {
                target.FrostbiteBurstThreshold = Math.Max(1, (int)Math.Round(
                    tower.GetEffectValue(TowerEffectCatalog.FrostbiteBurstThreshold, 3f)));
                target.FrostbiteBurstDamageMultiplier = Math.Max(0f,
                    tower.GetEffectValue(TowerEffectCatalog.FrostbiteBurstDamageMultiply, 3f));
                AddFrostbite(state, tower, target, Math.Max(1, (int)Math.Round(
                    tower.GetEffectValue(TowerEffectCatalog.Frostbite, 1f))), results);
            }
        }

        private static void ApplyUpgradeOnHitEffects(GameState state, TowerState tower, MonsterState target)
        {
            using var trace = CombatTrace.StepFor(state, 4, "burn/freeze/shock on-hit", tower?.InstanceId ?? 0, target);
            if (tower == null || target == null || target.IsDead) return;
            if (HasEffect(tower, TowerEffectCatalog.DoubleBurn) && !tower.HasEffect("disable_burn"))
                TowerUpgradeRules.AddBurn(state, target, target.BurnStacks * Math.Max(0, (int)Math.Round(
                    tower.GetEffectValue(TowerEffectCatalog.DoubleBurn, 1f))), tower.InstanceId);
            if (HasEffect(tower, TowerEffectCatalog.Freeze) && target.CanReceiveFreeze)
                target.FrozenMovesRemaining = Math.Max(target.FrozenMovesRemaining,
                    Math.Max(1, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.Freeze, 1f))));
            else if (HasEffect(tower, TowerEffectCatalog.Freeze))
                state.CombatTrace?.Add("freeze skipped: innate/temporary immunity", tower.InstanceId, target.InstanceId);
            if (HasEffect(tower, TowerEffectCatalog.Shock) && !target.IsImmuneTo("shock")) target.Shocked = true;
        }
        private static void DamageArea(GameState s,int center,float damage,int tower,ICollection<TowerAttackResult> r)
        { foreach(var m in s.Monsters) if(!m.IsDead&&Math.Min(Math.Abs(m.CurrentTileIndex-center),s.Board.TileCount-Math.Abs(m.CurrentTileIndex-center))<=1) ApplyDamage(s,m,damage,tower,r,TowerAttackVisualKind.None); }
        private void SpreadStatuses(GameState s, TowerState tower, MonsterState source,
            int radius, ICollection<TowerAttackResult> results)
        {
            using var trace = CombatTrace.StepFor(s, 3, "status_transfer", tower.InstanceId, source);
            var burn = source.BurnStacks;
            var frost = source.FrostbiteStacks;
            var shock = source.Shocked;
            var frozen = source.FrozenMovesRemaining;
            var add = (int)Math.Round(tower.GetEffectValue("transfer_stack_add"));
            var actualRadius = radius + Math.Max(0, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd)));
            foreach(var m in new List<MonsterState>(s.Monsters))
            {
                if(m.InstanceId==source.InstanceId || m.IsDead ||
                   Math.Min(Math.Abs(m.CurrentTileIndex-source.CurrentTileIndex),
                       s.Board.TileCount-Math.Abs(m.CurrentTileIndex-source.CurrentTileIndex))>actualRadius)
                    continue;
                using var recipientTrace = CombatTrace.StepFor(s, 3, "status_transfer recipient", tower.InstanceId, m);
                if (burn > 0) TowerUpgradeRules.AddBurn(s, m, burn + add, tower.InstanceId);
                if (frost > 0)
                {
                    m.FrostbiteBurstThreshold = source.FrostbiteBurstThreshold;
                    m.FrostbiteBurstDamageMultiplier = source.FrostbiteBurstDamageMultiplier;
                    AddFrostbite(s, tower, m, frost + add, results);
                }
                if (shock && !m.IsImmuneTo("shock")) { m.Shocked = true; m.ShockDamageBonus = Math.Max(m.ShockDamageBonus, source.ShockDamageBonus); }
                if(frozen>0 && m.CanReceiveFreeze) m.FrozenMovesRemaining=Math.Max(m.FrozenMovesRemaining, frozen);
                if (source.DamageStoreMarked && !m.DamageStoreMarked)
                {
                    m.DamageStoreMarked = true; m.StoredDamage = 0f;
                    m.DamageStoreMultiplier = source.DamageStoreMultiplier;
                    m.DamageStoreSourceTowerId = tower.InstanceId;
                }
                if (burn > 0 || frost > 0 || shock || frozen > 0 || source.DamageStoreMarked)
                {
                    SecondaryHit(s, m, tower.LastResolvedDamage * tower.GetEffectValue("transfer_damage") / 100f,
                        tower.InstanceId, results);
                    if (tower.HasEffect("status_count_damage"))
                    {
                        var count = (m.BurnStacks > 0 ? 1 : 0) + (m.FrostbiteStacks > 0 ? 1 : 0) +
                            (m.Shocked ? 1 : 0) + (m.FrozenMovesRemaining > 0 ? 1 : 0);
                        SecondaryHit(s, m, tower.LastResolvedDamage * count * tower.GetEffectValue("status_count_damage") / 100f,
                            tower.InstanceId, results);
                    }
                }
            }
        }

        private void AddFrostbite(GameState state, TowerState tower, MonsterState target,
            int stacks, ICollection<TowerAttackResult> results)
        {
            if (target == null || target.IsDead || stacks <= 0 || target.IsImmuneTo("frostbite"))
            { state.CombatTrace?.Add("frostbite skipped: dead / immune / no stacks", tower.InstanceId, target?.InstanceId ?? 0); return; }
            var before = target.FrostbiteStacks;
            target.FrostbiteStacks += stacks;
            var threshold = Math.Max(1, target.FrostbiteBurstThreshold);
            state.CombatTrace?.Add($"frostbite {before} -> {target.FrostbiteStacks}, immediate burst threshold {threshold}", tower.InstanceId, target.InstanceId);
            if (target.FrostbiteStacks < threshold) return;
            if (!frostbiteBurstThisResolution.Add(target.InstanceId)) return;
            target.FrostbiteStacks = Math.Max(0, (int)Math.Round(tower.GetEffectValue("frostbite_residual")));
            var multiplier = Math.Max(0f, tower.HasEffect("frostbite_burst_multiplier")
                ? tower.GetEffectValue("frostbite_burst_multiplier") / 100f : target.FrostbiteBurstDamageMultiplier);
            using var burstTrace = CombatTrace.StepFor(state, 3, "frostbite immediate burst", tower.InstanceId, target);
            ApplyDamage(state, target, tower.LastResolvedDamage * multiplier,
                tower.InstanceId, results, TowerAttackVisualKind.None);
            if (tower.HasEffect("frostbite_burst_spread"))
                foreach (var other in new List<MonsterState>(state.Monsters))
                    if (other != target && !other.IsDead && other.CurrentTileIndex == target.CurrentTileIndex)
                        AddFrostbite(state, tower, other, (int)Math.Round(tower.GetEffectValue("frostbite_burst_spread")), results);
        }

        private void ApplyHitEffectsOnTile(GameState state, TowerState tower, int tileIndex,
            ICollection<TowerAttackResult> results)
        {
            foreach (var monster in new List<MonsterState>(state.Monsters))
                if (!monster.IsDead && monster.CurrentTileIndex == tileIndex)
                {
                    ApplyBasePersistentEffects(state, tower, monster, results);
                    if (HasEffect(tower, TowerEffectCatalog.StatusTransfer))
                        SpreadStatuses(state, tower, monster, 1, results);
                    ApplyUpgradeOnHitEffects(state, tower, monster);
                }
        }
        private static MonsterState FindMonster(GameState s,int id)=>s.Monsters.Find(m=>m.InstanceId==id);
        private static Board.TileState FindTowerTile(GameState s,int id)=>s.Board.Tiles.Find(t=>t.HasTower&&t.Tower.InstanceId==id);
        private static void DamageTile(GameState s,int tile,float damage,int tower,ICollection<TowerAttackResult> r,
            TowerAttackVisualKind visualKind = TowerAttackVisualKind.Projectile)
        {foreach(var m in s.Monsters)if(!m.IsDead&&m.CurrentTileIndex==tile)ApplyDamage(s,m,damage,tower,r,visualKind);}
        public void ResolveStone(
            GameState state,
            TowerState stone,
            ICollection<TowerAttackResult> results,
            Func<IReadOnlyList<TowerAttackResult>, IReadOnlyList<TowerAttackResult>> effectResolver = null)
        {
                if (stone == null || !stone.StoneActive) return;
                stone.StoneTraversalTiles.Clear();
                stone.StoneExitAnimation = StoneExitAnimation.None;
                stone.StoneExitTileIndex = -1;

                // Resolve the whole roll in this turn. The final zero-damage step
                // is retained for presentation so the stone can shrink while moving.
                for (var step = 0; step < 12 && stone.StoneActive; step++)
                {
                    if (IsCorner(stone.StoneTileIndex))
                    {
                        stone.StoneExitAnimation = StoneExitAnimation.FallOffBoard;
                        stone.StoneActive = false;
                        break;
                    }

                    var nextTileIndex = (stone.StoneTileIndex + state.Board.TileCount - 1) % state.Board.TileCount;
                    var damage = (int)Math.Floor(stone.StoneBaseDamage * stone.StoneDamageMultiplier + .0001f);
                    if (damage <= 0)
                    {
                        stone.StoneExitAnimation = StoneExitAnimation.ShrinkOnZeroDamage;
                        stone.StoneExitTileIndex = nextTileIndex;
                        stone.StoneActive = false;
                        break;
                    }

                    stone.StoneTileIndex = nextTileIndex;
                    stone.StoneTraversalTiles.Add(stone.StoneTileIndex);
                    var batch = new List<TowerAttackResult>();
                    ResolveStoneAttack(state, stone, damage, batch);
                    IReadOnlyList<TowerAttackResult> resolved;
                    if (effectResolver != null) resolved = effectResolver(batch);
                    else
                    {
                        // Direct service callers still need knockback before the
                        // next stone step; other on-hit traits are resolved by their caller.
                        var direct = new List<TowerAttackResult>();
                        foreach (var hit in batch)
                        {
                            var target = FindMonster(state, hit.TargetInstanceId);
                            var movement = new List<TowerAttackResult>();
                            var from = target != null ? target.CurrentTileIndex : -1;
                            if (target != null && stone.HasEffect(TowerEffectCatalog.Knockback))
                                KnockbackMonster(state, stone, target, (int)Math.Round(
                                    stone.GetEffectValue(TowerEffectCatalog.Knockback, 1f)), movement);
                            direct.Add(target != null && from != target.CurrentTileIndex
                                ? hit.WithKnockback(from, target.CurrentTileIndex) : hit);
                            foreach (var item in movement) if (!item.KnockbackApplied) direct.Add(item);
                        }
                        resolved = direct;
                    }
                    foreach (var result in resolved) results.Add(result);
                    stone.StoneDamageMultiplier = Math.Max(0f, stone.StoneDamageMultiplier - .1f);
                    if (IsCorner(stone.StoneTileIndex))
                    {
                        stone.StoneExitAnimation = StoneExitAnimation.FallOffBoard;
                        stone.StoneActive = false;
                    }
                }

                stone.StoneActive = false;
            state.Monsters.RemoveAll(monster => monster.IsDead);
        }

        private static bool IsCorner(int tileIndex) =>
            tileIndex == 0 || tileIndex == 9 || tileIndex == 18 || tileIndex == 27;

        private static void ResolveStoneAttack(
            GameState state, TowerState sourceTower, int damage, ICollection<TowerAttackResult> results)
        {
            var hitMonster = false;
            foreach (var monster in new List<MonsterState>(state.Monsters))
            {
                if (monster.IsDead || monster.CurrentTileIndex != sourceTower.StoneTileIndex) continue;
                hitMonster = true;
                results.Add(TowerUpgradeRules.PrimaryHit(state, sourceTower, monster, damage,
                    TowerAttackVisualKind.RollingStone));
            }
            if (!hitMonster)
                results.Add(new TowerAttackResult(sourceTower.InstanceId, -1, 0f, false,
                    targetTileIndex: sourceTower.StoneTileIndex, visualKind: TowerAttackVisualKind.RollingStone));
        }

        private static void ResolveKnockbackDestination(
            GameState state,
            MonsterState monster,
            int tileIndex,
            int sourceTowerInstanceId,
            ICollection<TowerAttackResult> results)
        {
            if (monster == null || monster.IsDead || tileIndex < 0 || tileIndex >= state.Board.TileCount)
                return;
            var tile = state.Board.Tiles[tileIndex];
            if (tile.FireTurnsRemaining > 0)
            {
                TowerUpgradeRules.AddBurn(state, monster, 1, tile.FireSourceTowerId);
                TowerUpgradeRules.BurnTick(state, monster, tileIndex, results);
            }
            if (monster.IsDead || !tile.HasTower ||
                !HasEffect(tile.Tower, TowerEffectCatalog.Wall)) return;
            ApplyDamage(state, monster, tile.Tower.LastResolvedDamage, tile.Tower.InstanceId, results,
                TowerAttackVisualKind.Projectile);
        }
    

private void SpreadChainLineFieldsFromSnapshot(
            GameState state,
            IReadOnlyList<TowerAttackResult> attacks,
            ICollection<TowerAttackResult> results)
        {
            var processedTowers = new HashSet<int>();
            foreach (var attack in attacks)
            {
                if (attack.VisualKind != TowerAttackVisualKind.ChainLine ||
                    !processedTowers.Add(attack.TowerInstanceId)) continue;
                var towerTile = FindTowerTile(state, attack.TowerInstanceId);
                if (towerTile == null ||
                    !HasEffect(towerTile.Tower, TowerEffectCatalog.FieldSpread)) continue;

                var line = MonsterService.GetLine(towerTile.Index);
                var sources = new List<KeyValuePair<int, bool>>();
                foreach (var tile in state.Board.Tiles)
                {
                    if (MonsterService.GetLine(tile.Index) != line) continue;
                    if (tile.FireTurnsRemaining > 0) sources.Add(new KeyValuePair<int, bool>(tile.Index, true));
                    else if (tile.IceTurnsRemaining > 0) sources.Add(new KeyValuePair<int, bool>(tile.Index, false));
                }

                var radius = 1 + Math.Max(0, (int)Math.Round(
                    towerTile.Tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f)));
                foreach (var source in sources)
                {
                    var spreadTiles = new HashSet<int>();
                    AddAreaTiles(state, source.Key, radius, spreadTiles);
                    PlaceFields(state, spreadTiles, source.Value, attack.TowerInstanceId, results);
                }
            }
        }
}
}
