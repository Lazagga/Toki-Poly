using System;
using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;

namespace GaeBullBing.Core.Towers
{
    public sealed partial class TowerEffectService
    {
        private readonly HashSet<int> frostbiteBurstThisResolution = new();

        private static void SecondaryHit(GameState state, MonsterState target, float damage,
            int sourceId, ICollection<TowerAttackResult> results)
        {
            if (target == null || target.IsDead || damage <= 0f) return;
            var actual = TowerUpgradeRules.Receive(state, target, damage, sourceId);
            results.Add(new TowerAttackResult(sourceId, target.InstanceId, actual, target.IsDead,
                targetTileIndex: target.CurrentTileIndex));
        }

        private void ApplyAdditionalOnHitEffects(GameState state, TowerState tower,
            MonsterState target, TowerAttackResult attack, ICollection<TowerAttackResult> results)
        {
            if (target == null) return;
            using var onHitTrace = CombatTrace.StepFor(state, 4, "additional upgrade on-hit", tower.InstanceId, target);
            if (tower.HasEffect("tile_burn_spread"))
                foreach (var other in new List<MonsterState>(state.Monsters))
                    if (other != target && !other.IsDead && other.CurrentTileIndex == attack.TargetTileIndex)
                        TowerUpgradeRules.AddBurn(state, other,
                            (int)Math.Round(tower.GetEffectValue("tile_burn_spread")), tower.InstanceId);
            if (!target.IsDead)
            {
                if (tower.HasEffect("burn_max_stack_add"))
                    target.MaxBurnStacks = Math.Max(target.MaxBurnStacks,
                        20 + (int)Math.Round(tower.GetEffectValue("burn_max_stack_add")));
                if (tower.HasEffect("shock_amplify") && !target.IsImmuneTo("shock"))
                {
                    target.Shocked = true;
                    target.ShockDamageBonus = Math.Max(target.ShockDamageBonus,
                        tower.GetEffectValue("shock_amplify") / 100f);
                }
                if (tower.HasEffect("move_reduce"))
                    target.NextMoveReduction = Math.Max(target.NextMoveReduction,
                        (int)Math.Round(tower.GetEffectValue("move_reduce")));
                if (tower.HasEffect("rolling_stone_stun") && attack.VisualKind == TowerAttackVisualKind.RollingStone)
                    target.StunnedMovesRemaining = Math.Max(target.StunnedMovesRemaining, 1);
                if (tower.HasEffect("teleport_line_start") && !target.LineTeleportConsumed)
                {
                    var start = TowerUpgradeRules.LineStart(target.CurrentTileIndex);
                    if (target.CurrentTileIndex != start)
                    {
                        var from = target.CurrentTileIndex;
                        target.LineTeleportConsumed = true;
                        target.DistanceTravelled = Math.Max(0, target.DistanceTravelled - (from - start));
                        target.CurrentTileIndex = start;
                        results.Add(new TowerAttackResult(tower.InstanceId, target.InstanceId, 0f, false,
                            true, from, start, from, teleportApplied: true));
                        ResolveKnockbackDestination(state, target, start, tower.InstanceId, results);
                        if (state.Board.Tiles[start].IceTurnsRemaining > 0)
                            target.NextMoveReduction = Math.Max(target.NextMoveReduction, Math.Max(0, target.MoveDistance - 1));
                    }
                }
            }

        }

        private void ApplyAdditionalConditionalEffects(GameState state, TowerState tower,
            MonsterState target, TowerAttackResult attack, ICollection<TowerAttackResult> results)
        {
            if (target == null) return;
            // Conditions were captured before this hit; changes from base effects
            // and on-hit effects cannot retroactively enable them.
            using var conditionalTrace = CombatTrace.StepFor(state, 5, "additional conditional effects", tower.InstanceId, target);
            var conditionalRate = 0f;
            if (attack.PreAttackFireTile) conditionalRate += tower.GetEffectValue("fire_tile_damage_bonus") / 100f;
            if (attack.PreAttackFrostbiteStacks > 0) conditionalRate += tower.GetEffectValue("frostbite_damage_bonus") / 100f;
            if (attack.PreAttackShocked) conditionalRate += tower.GetEffectValue("shock_damage_bonus") / 100f;
            if (attack.PreAttackHealthRatio <= .5f) conditionalRate += tower.GetEffectValue("low_hp_damage_bonus") / 100f;
            if (tower.HasEffect("status_count_damage"))
            {
                var count = (attack.PreAttackBurnStacks > 0 ? 1 : 0) +
                    (attack.PreAttackFrostbiteStacks > 0 ? 1 : 0) +
                    (attack.PreAttackFrozen ? 1 : 0) + (attack.PreAttackShocked ? 1 : 0);
                conditionalRate += count * tower.GetEffectValue("status_count_damage") / 100f;
            }
            if (tower.HasEffect("burn_consume_burst") && attack.PreAttackBurnStacks >= 10 && !target.IsDead)
            {
                // Consume only the snapshotted stacks. Newly applied stacks belong
                // to this attack and are retained for the next one.
                var consumed = Math.Min(target.BurnStacks, attack.PreAttackBurnStacks);
                target.BurnSourceTowerIds.RemoveRange(0, consumed);
                target.BurnStacks -= consumed;
                conditionalRate += attack.PreAttackBurnStacks * tower.GetEffectValue("burn_consume_burst") / 100f;
            }
            if (tower.HasEffect("execute_threshold") &&
                attack.PreAttackHealthRatio <= tower.GetEffectValue("execute_threshold") / 100f + .000001f)
            {
                if (target.IsBoss) conditionalRate += 1f;
                else if (!target.IsDead)
                {
                    var damage = target.CurrentHealth;
                    target.CurrentHealth = 0f;
                    target.LastDamageTowerId = tower.InstanceId;
                    state.CombatTrace?.Add($"execute: direct remaining HP removal {damage:0.###} (not a defense-scaled hit)", tower.InstanceId, target.InstanceId);
                    results.Add(new TowerAttackResult(tower.InstanceId, target.InstanceId, damage, true,
                        targetTileIndex: target.CurrentTileIndex));
                }
            }
            if (conditionalRate > 0f)
            {
                state.CombatTrace?.Add($"Conditional extra factor {conditionalRate:P0}; " +
                    $"fire-tile={attack.PreAttackFireTile}, frost={attack.PreAttackFrostbiteStacks}, shock={attack.PreAttackShocked}, HP%={attack.PreAttackHealthRatio:P1}, burn={attack.PreAttackBurnStacks}; " +
                    "rates are summed from the enabled pre-hit conditional effects", tower.InstanceId, target.InstanceId);
                SecondaryHit(state, target, tower.LastResolvedDamage * conditionalRate, tower.InstanceId, results);
            }
            if (tower.HasEffect("shock_link") && attack.PreAttackShocked)
            {
                var candidates = state.Monsters.FindAll(m => m != target && !m.IsDead && m.Shocked &&
                    MonsterService.GetLine(m.CurrentTileIndex) == MonsterService.GetLine(attack.TargetTileIndex));
                candidates.Sort((a, b) => b.DistanceTravelled != a.DistanceTravelled
                    ? b.DistanceTravelled.CompareTo(a.DistanceTravelled) : a.InstanceId.CompareTo(b.InstanceId));
                if (candidates.Count > 0)
                    SecondaryHit(state, candidates[0], attack.Damage * tower.GetEffectValue("shock_link") / 100f,
                        tower.InstanceId, results);
            }
        }

        private void ApplyLineKnockback(GameState state, TowerState tower,
            IReadOnlyList<TowerAttackResult> attacks, ICollection<TowerAttackResult> results)
        {
            if (!tower.HasEffect("knockback_line")) return;
            var lineTile = TowerUpgradeRules.FindTower(state, tower.InstanceId);
            if (lineTile == null) return;
            var line = MonsterService.GetLine(lineTile.Index);
            var triggers = new HashSet<int>();
            foreach (var hit in attacks) if (hit.TargetInstanceId > 0) triggers.Add(hit.TargetInstanceId);
            for (var trigger = 0; triggers.Count > 0 && trigger < Math.Max(1, tower.LastResolvedTargetCount); trigger++)
                foreach (var monster in new List<MonsterState>(state.Monsters))
                    if (!monster.IsDead && MonsterService.GetLine(monster.CurrentTileIndex) == line)
                    {
                        var moved = KnockbackMonster(state, tower, monster,
                            (int)Math.Round(tower.GetEffectValue("knockback_line")), results);
                        if (moved)
                            TowerUpgradeRules.AddBurn(state, monster,
                                (int)Math.Round(tower.GetEffectValue("knockback_line_burn")), tower.InstanceId);
                    }
        }

        private static void ApplyAttackedTileBuffs(GameState state, IReadOnlyList<TowerAttackResult> attacks)
        {
            var visited = new HashSet<string>();
            foreach (var attack in attacks)
            {
                if (attack.TargetTileIndex < 0 || attack.TargetTileIndex >= state.Board.TileCount) continue;
                var source = TowerUpgradeRules.FindTower(state, attack.TowerInstanceId)?.Tower;
                var tile = state.Board.Tiles[attack.TargetTileIndex];
                if (source == null || !tile.HasTower || !source.HasEffect("tile_tower_damage_buff") ||
                    !visited.Add(source.InstanceId + ":" + tile.Index)) continue;
                tile.Tower.TemporaryDamageBuffs.Add(new TimedTowerDamageBuff {
                    Rate = source.GetEffectValue("tile_tower_damage_buff") / 100f,
                    ExpiresAfterRound = state.Round + 1 });
            }
        }

        private void ResolveFollowupTile(GameState state, TowerState tower, int tileIndex,
            float damage, TowerAttackVisualKind visualKind, ICollection<TowerAttackResult> results)
        {
            var hits = new List<TowerAttackResult>();
            foreach (var monster in new List<MonsterState>(state.Monsters))
            {
                if (monster.IsDead || monster.CurrentTileIndex != tileIndex) continue;
                var hit = TowerUpgradeRules.PrimaryHit(state, tower, monster, damage, visualKind);
                hits.Add(hit);
                results.Add(hit);
                ApplyBasePersistentEffects(state, tower, monster, results);
                if (!monster.IsDead && tower.HasEffect(TowerEffectCatalog.StatusTransfer))
                    SpreadStatuses(state, tower, monster, 1, results);
                using var onHitTrace = CombatTrace.StepFor(state, 4, "chain/pierce on-hit", tower.InstanceId, monster);
                ApplyUpgradeOnHitEffects(state, tower, monster);
                if (tower.HasEffect(TowerEffectCatalog.Knockback))
                    KnockbackMonster(state, tower, monster,
                        (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.Knockback)), results);
                ApplyAdditionalOnHitEffects(state, tower, monster, hit, results);
                onHitTrace?.Dispose();
                using var conditionalTrace = CombatTrace.StepFor(state, 5, "chain/pierce conditional", tower.InstanceId, monster);
                if (hit.PreAttackFrozen && tower.HasEffect(TowerEffectCatalog.FreezeDamageMultiply))
                    SecondaryHit(state, monster, tower.LastResolvedDamage * Math.Max(0f,
                        tower.GetEffectValue(TowerEffectCatalog.FreezeDamageMultiply) - 1f), tower.InstanceId, results);
                if (hit.PreAttackBurnStacks > 0)
                    SecondaryHit(state, monster, tower.LastResolvedDamage * hit.PreAttackBurnStacks *
                        tower.GetEffectValue(TowerEffectCatalog.BurnDamage), tower.InstanceId, results);
                if (tower.HasEffect(TowerEffectCatalog.BurnExplode) && hit.PreAttackBurnStacks >=
                    tower.GetEffectValue(TowerEffectCatalog.BurnExplode))
                    DamageArea(state, tileIndex, tower.LastResolvedDamage, tower.InstanceId, results);
                ApplyAdditionalConditionalEffects(state, tower, monster, hit, results);
            }
            ApplyAttackedTileBuffs(state, hits);
        }

        private void ApplyBurnTick(GameState state, MonsterState monster, ICollection<TowerAttackResult> results) =>
            TowerUpgradeRules.BurnTick(state, monster, monster.CurrentTileIndex, results);

        private static bool KnockbackMonster(GameState state, TowerState tower, MonsterState monster,
            int distance, ICollection<TowerAttackResult> results)
        {
            using var trace = CombatTrace.StepFor(state, 4, "knockback", tower.InstanceId, monster);
            if (monster.IsDead || monster.IsImmuneTo("knockback") || monster.KnockbackConsumed)
            { state.CombatTrace?.Add("knockback skipped: dead / innate or temporary immunity", tower.InstanceId, monster.InstanceId); return false; }
            var from = monster.CurrentTileIndex;
            var lineStart = TowerUpgradeRules.LineStart(from);
            for (var step = 0; step < Math.Max(1, distance) && monster.CurrentTileIndex > lineStart && !monster.IsDead; step++)
            {
                var previous = monster.CurrentTileIndex;
                monster.CurrentTileIndex--;
                monster.DistanceTravelled = Math.Max(0, monster.DistanceTravelled - 1);
                results.Add(new TowerAttackResult(tower.InstanceId, monster.InstanceId, 0f, false,
                    true, previous, monster.CurrentTileIndex, previous));
                ResolveKnockbackDestination(state, monster, monster.CurrentTileIndex, tower.InstanceId, results);
                if (tower.HasEffect("knockback_impact"))
                    foreach (var other in new List<MonsterState>(state.Monsters))
                        if (other != monster && !other.IsDead && other.CurrentTileIndex == monster.CurrentTileIndex)
                            SecondaryHit(state, other, tower.LastResolvedDamage *
                                tower.GetEffectValue("knockback_impact") / 100f, tower.InstanceId, results);
            }
            if (monster.CurrentTileIndex == from) return false;
            monster.KnockbackImmunityPending = true;
            monster.KnockbackImmunityLine = MonsterService.GetLine(monster.CurrentTileIndex);
            return true;
        }

        public void ResolveDeathEffects(GameState state, ICollection<TowerAttackResult> results)
        {
            // Each monster is processed once; a death explosion may kill another
            // monster, which is picked up in the next pass.
            while (true)
            {
                var dead = state.Monsters.Find(m => m.IsDead && !m.DeathEffectsHandled);
                if (dead == null) break;
                using var trace = CombatTrace.StepFor(state, 0, "death follow-up effects", dead.LastDamageTowerId, dead);
                dead.DeathEffectsHandled = true;
                var tower = TowerUpgradeRules.FindTower(state, dead.LastDamageTowerId)?.Tower;
                if (tower == null) continue;
                var tiles = new HashSet<int>();
                AddAreaTiles(state, dead.CurrentTileIndex, 1, tiles);
                if (dead.BurnStacks > 0 && tower.HasEffect("burn_inherit_on_kill"))
                    foreach (var other in new List<MonsterState>(state.Monsters))
                        if (!other.IsDead && tiles.Contains(other.CurrentTileIndex))
                            TowerUpgradeRules.AddBurn(state, other, dead.BurnStacks, tower.InstanceId);
                if (dead.BurnStacks > 0 && tower.HasEffect("burn_kill_fire_tile"))
                    PlaceFields(state, tiles, true, tower.InstanceId, results);
                if (dead.FrozenMovesRemaining > 0 && tower.HasEffect("shatter_on_kill"))
                    foreach (var other in new List<MonsterState>(state.Monsters))
                        if (!other.IsDead && tiles.Contains(other.CurrentTileIndex))
                            SecondaryHit(state, other, dead.MaxHealth * tower.GetEffectValue("shatter_on_kill") / 100f,
                                tower.InstanceId, results);
            }
        }

        public IReadOnlyList<TowerAttackResult> ResolvePlayerArrival(GameState state, int tileIndex)
        {
            var results = new List<TowerAttackResult>();
            var tile = state.Board.Tiles[tileIndex];
            if (!tile.HasTower) return results;
            var tower = tile.Tower;
            if (tower.HasEffect("path_damage"))
                state.Player.PathDamageBuffs.Add(new PlayerPathDamageBuff {
                    SourceTowerId = tower.InstanceId, Damage = tower.LastResolvedDamage });
            if (tower.HasEffect("step_damage_double")) tower.StepDamageRound = state.Round;
            if (tower.HasEffect("quake_line_stun"))
                foreach (var monster in state.Monsters)
                    if (MonsterService.GetLine(monster.CurrentTileIndex) == MonsterService.GetLine(tileIndex) &&
                        !monster.IsImmuneTo("stun"))
                        monster.StunnedMovesRemaining = Math.Max(monster.StunnedMovesRemaining, 1);
            return results;
        }

        public IReadOnlyList<TowerAttackResult> ResolvePlayerPath(GameState state, int start, int distance, bool teleport)
        {
            var results = new List<TowerAttackResult>();
            if (teleport) return results;
            for (var step = 0; step < distance; step++)
            {
                var tileIndex = (start + step) % state.Board.TileCount;
                if (step > 0 && tileIndex == 0) state.Player.PathDamageBuffs.Clear();
                foreach (var monster in new List<MonsterState>(state.Monsters))
                    if (!monster.IsDead && monster.CurrentTileIndex == tileIndex)
                        foreach (var buff in state.Player.PathDamageBuffs)
                            SecondaryHit(state, monster, buff.Damage, buff.SourceTowerId, results);
                var entered = (tileIndex + 1) % state.Board.TileCount;
                if (entered == 0) state.Player.PathDamageBuffs.Clear();
                var tower = state.Board.Tiles[entered].Tower;
                if (tower != null && tower.HasEffect("pass_attack_count")) tower.PassAttackRound = state.Round;
            }
            ResolveDeathEffects(state, results);
            state.Monsters.RemoveAll(m => m.IsDead);
            return results;
        }

        public IReadOnlyList<TowerAttackResult> ResolveTurnStart(GameState state)
        {
            var results = new List<TowerAttackResult>();
            foreach (var tile in state.Board.Tiles)
            {
                var tower = tile.Tower;
                if (tower == null || tower.IsFeatherSealed || !tower.HasEffect("shock_aura")) continue;
                foreach (var monster in state.Monsters)
                    if (!monster.IsDead && !monster.IsImmuneTo("shock") &&
                        TowerCombatService.GetBoardDistance(tile.Index, monster.CurrentTileIndex) <= tower.LastResolvedRange)
                        monster.Shocked = true;
            }
            return results;
        }

        public IReadOnlyList<TowerAttackResult> ResolvePeriodicAttack(GameState state, TowerState tower)
        {
            var results = new List<TowerAttackResult>();
            tower.PeriodicTurnCount++;
            var tile = TowerUpgradeRules.FindTower(state, tower.InstanceId);
            if (tile == null || tower.IsFeatherSealed) return results;
            if (tower.HasEffect("blizzard_line") && tower.IsEffectReady("blizzard_line"))
            {
                foreach (var monster in new List<MonsterState>(state.Monsters))
                    if (!monster.IsDead && MonsterService.GetLine(monster.CurrentTileIndex) == MonsterService.GetLine(tile.Index))
                    {
                        results.Add(TowerUpgradeRules.PrimaryHit(state, tower, monster,
                            tower.LastResolvedDamage, TowerAttackVisualKind.ChainLine));
                    }
                if (results.Count > 0)
                    tower.StartEffectCooldown("blizzard_line", (int)Math.Round(tower.GetEffectValue("blizzard_line")));
            }
            if (tower.HasEffect("lightning_strike") && tower.IsEffectReady("lightning_strike"))
            {
                var candidates = state.Monsters.FindAll(m => !m.IsDead);
                candidates.Sort((a, b) => b.CurrentHealth != a.CurrentHealth
                    ? b.CurrentHealth.CompareTo(a.CurrentHealth) : a.InstanceId.CompareTo(b.InstanceId));
                if (candidates.Count > 0)
                {
                    var monster = candidates[0];
                    results.Add(TowerUpgradeRules.PrimaryHit(state, tower, monster,
                        tower.LastResolvedDamage * tower.GetEffectValue("lightning_strike") / 100f,
                        TowerAttackVisualKind.Projectile));
                    if (!monster.IsImmuneTo("shock")) monster.Shocked = true;
                    tower.StartEffectCooldown("lightning_strike", 3);
                }
            }
            return results;
        }
    }
}
