using System;
using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;

namespace GaeBullBing.Core.Towers
{
    // Shared entry points for primary hits, stats and damage from every source.
    // Secondary damage never calls PrimaryHit: this prevents recursive on-hit loops.
    public static class TowerUpgradeRules
    {
        public static TileState FindTower(GameState state, int instanceId) =>
            state.Board.Tiles.Find(tile => tile.HasTower && tile.Tower.InstanceId == instanceId);

        public static int LineStart(int tileIndex) =>
            tileIndex <= 9 ? 0 : tileIndex <= 18 ? 10 : tileIndex <= 27 ? 19 : 28;

        public static void AddBurn(GameState state, MonsterState target, int stacks, int sourceId)
        {
            var source = FindTower(state, sourceId)?.Tower;
            if (source != null && source.HasEffect("disable_burn"))
            { state.CombatTrace?.Add("burn skipped: disable_burn", sourceId, target.InstanceId); return; }
            if (target.IsImmuneTo("burn") || stacks <= 0)
            { state.CombatTrace?.Add("burn skipped: immune or no stacks", sourceId, target.InstanceId); return; }
            var previous = target.BurnStacks;
            target.BurnStacks += stacks;
            for (var index = previous; index < target.BurnStacks; index++) target.BurnSourceTowerIds[index] = sourceId;
            target.BurnSourceTowerId = sourceId;
            state.CombatTrace?.Add($"burn {previous} -> {target.BurnStacks} (requested +{stacks})", sourceId, target.InstanceId);
        }

        public static float Receive(GameState state, MonsterState target, float rawDamage, int sourceId)
        {
            if (target.IsDead) return 0f;
            target.LastDamageTowerId = sourceId;
            target.TileVulnerability = 0f;
            if (state.Board.Tiles[target.CurrentTileIndex].IceTurnsRemaining > 0)
                foreach (var tile in state.Board.Tiles)
                    if (tile.HasTower && !tile.Tower.IsFeatherSealed &&
                        tile.Tower.HasEffect("ice_tile_vulnerability") &&
                        TowerCombatService.GetBoardDistance(tile.Index, target.CurrentTileIndex) <= tile.Tower.LastResolvedRange)
                        target.TileVulnerability += tile.Tower.GetEffectValue("ice_tile_vulnerability") / 100f;
            var health = target.CurrentHealth;
            var damage = target.ReceiveDamage(rawDamage, state.Difficulty);
            if (state.CombatTrace != null)
                state.CombatTrace.Add($"damage raw {rawDamage:0.###} * incoming {(1f + (target.Shocked ? target.ShockDamageBonus : 0f) + target.TileVulnerability):0.###}" +
                    $" * 100 / (100 + defense {target.GetDefense(state.Difficulty):0.###}) = {damage:0.###}; HP {health:0.###} -> {target.CurrentHealth:0.###}", sourceId, target.InstanceId);
            return damage;
        }

        public static void BurnTick(GameState state, MonsterState target, int tileIndex,
            ICollection<TowerAttackResult> results)
        {
            if (target.IsDead || target.BurnStacks <= 0) return;
            var sourceId = target.BurnSourceTowerId;
            using var trace = CombatTrace.StepFor(state, state.CombatTrace?.Phase ?? 0,
                "burn tick: max HP * 0.25% * stacks", sourceId, target);
            var originalTile = target.CurrentTileIndex;
            target.CurrentTileIndex = tileIndex;
            float damage;
            try { damage = Receive(state, target, target.MaxHealth * .0025f * target.BurnStacks, sourceId); }
            finally { target.CurrentTileIndex = originalTile; }
            results.Add(new TowerAttackResult(sourceId, target.InstanceId, damage, target.IsDead, targetTileIndex: tileIndex));
            var contributions = new Dictionary<int, int>();
            foreach (var owner in target.BurnSourceTowerIds)
            {
                var id = owner == 0 ? sourceId : owner;
                contributions[id] = contributions.TryGetValue(id, out var count) ? count + 1 : 1;
            }
            foreach (var pair in contributions)
            {
                var tower = FindTower(state, pair.Key)?.Tower;
                if (tower == null || !tower.HasEffect("burn_tick_splash")) continue;
                foreach (var other in new List<MonsterState>(state.Monsters))
                    if (other != target && !other.IsDead && other.CurrentTileIndex == tileIndex)
                    {
                        other.LastDamageTowerId = pair.Key;
                        var actual = other.ReceiveResolvedDamage(damage * pair.Value / target.BurnStacks);
                        state.CombatTrace?.Add($"burn splash resolved damage {actual:0.###} (no second defense/multiplier)", pair.Key, other.InstanceId);
                        results.Add(new TowerAttackResult(pair.Key, other.InstanceId, actual, other.IsDead,
                            targetTileIndex: tileIndex));
                    }
            }
        }

        public static TowerAttackResult PrimaryHit(GameState state, TowerState tower, MonsterState target,
            float rawDamage, TowerAttackVisualKind visualKind)
        {
            if (target.IsDead) return new TowerAttackResult(tower.InstanceId, target.InstanceId, 0f, false,
                targetTileIndex: target.CurrentTileIndex, visualKind: visualKind);
            if (tower.ChargeCount > 0)
            {
                tower.ActiveChargeBonus = tower.ChargeCount * tower.GetEffectValue("charge_damage") / 100f;
                tower.ChargeCount = 0;
            }
            var burn = target.BurnStacks;
            var frost = target.FrostbiteStacks;
            var frozen = target.FrozenMovesRemaining > 0;
            var shock = target.Shocked;
            var healthRatio = target.MaxHealth > 0f ? target.CurrentHealth / target.MaxHealth : 0f;
            var fire = state.Board.Tiles[target.CurrentTileIndex].FireTurnsRemaining > 0;
            if (state.CombatTrace != null)
            {
                using var conditionTrace = state.CombatTrace.Enter(1, "pre-hit conditions", tower.InstanceId, target);
                TraceCondition(state, tower, target, TowerEffectCatalog.BurnExplode, burn >= Math.Max(1, tower.GetEffectValue(TowerEffectCatalog.BurnExplode, 20)));
                TraceCondition(state, tower, target, TowerEffectCatalog.BurnDamage, burn > 0);
                TraceCondition(state, tower, target, TowerEffectCatalog.FreezeDamageMultiply, frozen);
                TraceCondition(state, tower, target, "burn_consume_burst", burn >= 10);
                TraceCondition(state, tower, target, "fire_tile_damage_bonus", fire);
                TraceCondition(state, tower, target, "frostbite_damage_bonus", frost > 0);
                TraceCondition(state, tower, target, "shock_damage_bonus", shock);
                TraceCondition(state, tower, target, "shock_link", shock);
                TraceCondition(state, tower, target, "low_hp_damage_bonus", healthRatio <= .5f);
                TraceCondition(state, tower, target, "execute_threshold", healthRatio <= tower.GetEffectValue("execute_threshold") / 100f + .000001f);
                TraceCondition(state, tower, target, "status_count_damage", burn > 0 || frost > 0 || frozen || shock);
            }
            using var primaryTrace = CombatTrace.StepFor(state, 2, "primary " + visualKind, tower.InstanceId, target);
            if (tower.HasEffect("damage_store") && !target.DamageStoreMarked)
            {
                target.DamageStoreMarked = true;
                target.DamageStoreMultiplier = tower.GetEffectValue("damage_store") / 100f;
                target.DamageStoreSourceTowerId = tower.InstanceId;
                target.StoredDamage = 0f;
            }
            var bonus = tower.ActiveChargeBonus +
                Math.Max(0, tower.AttacksThisTurn - 1) * tower.GetEffectValue("attack_ramp_turn") / 100f;
            if (tower.HasEffect("consecutive_target_damage"))
            {
                tower.ConsecutiveDamageBonus = tower.LastAttackTargetId == target.InstanceId
                    ? Math.Min(tower.GetEffectValue("consecutive_target_damage_cap", 100f) / 100f,
                        tower.ConsecutiveDamageBonus + tower.GetEffectValue("consecutive_target_damage") / 100f)
                    : 0f;
                tower.LastAttackTargetId = target.InstanceId;
                bonus += tower.ConsecutiveDamageBonus;
            }
            if (bonus != 0f) state.CombatTrace?.Add($"Primary modifiers: base {rawDamage:0.###}, charge {tower.ActiveChargeBonus:P0}, " +
                $"turn volley {tower.AttacksThisTurn}, consecutive {tower.ConsecutiveDamageBonus:P0}; summed hit bonus {bonus:P0}", tower.InstanceId, target.InstanceId);
            var damage = Receive(state, target, rawDamage * (1f + bonus), tower.InstanceId);
            return new TowerAttackResult(tower.InstanceId, target.InstanceId, damage, target.IsDead,
                targetTileIndex: target.CurrentTileIndex, visualKind: visualKind,
                preAttackBurnStacks: burn, preAttackFrostbiteStacks: frost, preAttackFrozen: frozen,
                preAttackShocked: shock, preAttackHealthRatio: healthRatio, preAttackFireTile: fire);
        }

        private static void TraceCondition(GameState state, TowerState tower, MonsterState target, string effect, bool met)
        {
            if (tower.HasEffect(effect)) state.CombatTrace.Add(effect + (met ? ": MET (before hit)" : ": NOT MET (before hit)"), tower.InstanceId, target.InstanceId);
        }

        public static float ExtraBaseDamage(GameState state, TowerState tower)
        {
            var count = 0;
            foreach (var tile in state.Board.Tiles)
                if (tile.HasTower && tile.Tower.HasEffect("upgrade_count_damage")) count++;
            return tower.LapGrowthDamage + count * tower.GetEffectValue("upgrade_count_damage");
        }

        public static float ExtraDamageRate(GameState state, TileState tile)
        {
            var tower = tile.Tower;
            var rate = 0f;
            foreach (var buff in tower.TemporaryDamageBuffs)
                if (buff.ExpiresAfterRound >= state.Round) rate += buff.Rate;
            if (tower.HasEffect("step_damage_double") && tower.StepDamageRound == state.Round)
                rate += Math.Max(0f, tower.GetEffectValue("step_damage_double") - 1f);
            if (tower.HasEffect("line_element_count_damage"))
                foreach (var candidate in state.Board.Tiles)
                    if (candidate.HasTower && candidate.Tower.Element == tower.Element &&
                        MonsterService.GetLine(candidate.Index) == MonsterService.GetLine(tile.Index))
                        rate += tower.GetEffectValue("line_element_count_damage") / 100f;
            return rate;
        }

        public static int ExtraAttackCount(GameState state, TowerState tower) =>
            tower.PassAttackRound == state.Round ? (int)Math.Round(tower.GetEffectValue("pass_attack_count")) : 0;

        public static void BeginVolley(TowerState tower)
        {
            tower.AttacksThisTurn++;
            tower.ActiveChargeBonus = 0f;
        }
    }
}
