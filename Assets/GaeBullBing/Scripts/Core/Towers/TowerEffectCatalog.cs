using System;
using System.Collections.Generic;

namespace GaeBullBing.Core.Towers
{
    // Upgrade JSON only selects an effect by ID. Actual behavior belongs in C# effect handlers.
    public static class TowerEffectCatalog
    {
        public const string Explode = "explode";
        public const string Burn = "burn";
        public const string Freeze = "freeze";
        public const string RangeAttack = "range_attack";
        public const string Knockback = "knockback";
        public const string StatusTransfer = "status_transfer";
        public const string Shock = "shock";
        public const string BurnDamage = "burn_damage";
        public const string BurnExplode = "burn_explode";
        public const string ChainLightning = "chain_lightning";
        public const string ChainLine = "chain_line";
        public const string ChainTile = "chain_tile";
        public const string Cooldown = "cooldown";
        public const string DoubleBurn = "double_burn";
        public const string FreezeDamageMultiply = "freeze_damage_multiply";
        public const string RollingStone = "rolling_stone";
        public const string TileBreak = "tile_break";
        public const string TileBurn = "tile_burn";
        public const string TileFreeze = "tile_freeze";
        public const string TowerBuff = "tower_buff";
        public const string Wall = "wall";
        public const string AreaTile = "aoe_tile";
        public const string LineTowerBuff = "line_tower_buff";
        public const string SpreadRangeAdd = "spread_range_add";
        public const string TileStepLineBuff = "tile_step_line_buff";
        public const string FieldSpread = "field_spread";
        public const string Frostbite = "frostbite";
        public const string FrostbiteBurstThreshold = "frostbite_burst_threshold";
        public const string FrostbiteBurstDamageMultiply = "frostbite_burst_damage_multiply";

        // This is the single source of truth for effects with runtime handlers.
        public static readonly IReadOnlyCollection<string> ImplementedEffectIds = Array.AsReadOnly(new[]
        {
            Explode, Burn, Freeze, RangeAttack, Knockback, StatusTransfer, Shock,
            BurnDamage, BurnExplode, ChainLightning, ChainLine, ChainTile, Cooldown,
            DoubleBurn, FreezeDamageMultiply, RollingStone, TileBreak, TileBurn,
            TileFreeze, TowerBuff, Wall, AreaTile, LineTowerBuff, SpreadRangeAdd,
            TileStepLineBuff, FieldSpread, Frostbite, FrostbiteBurstThreshold,
            FrostbiteBurstDamageMultiply,
            "burn_inherit_on_kill",
            "knockback_line",
            "knockback_line_burn",
            "damage_store",
            "path_damage",
            "tile_tower_damage_buff",
            "teleport_line_start",
            "disable_burn",
            "status_count_damage",
            "upgrade_count_damage",
            "burn_consume_burst",
            "fire_tile_damage_bonus",
            "consecutive_target_damage",
            "consecutive_target_damage_cap",
            "burn_tick_splash",
            "burn_max_stack_add",
            "burn_kill_fire_tile",
            "overheat_cycle",
            "line_fire_tile_duration_add",
            "tile_burn_spread",
            "frostbite_burst_multiplier",
            "frostbite_burst_spread",
            "frostbite_residual",
            "shatter_on_kill",
            "ice_tile_vulnerability",
            "move_reduce",
            "blizzard_line",
            "frostbite_damage_bonus",
            "execute_threshold",
            "pierce",
            "quake_line_stun",
            "knockback_impact",
            "attack_ramp_turn",
            "rolling_stone_stun",
            "lap_damage_stack",
            "low_hp_damage_bonus",
            "step_damage_double",
            "shock_amplify",
            "shock_link",
            "shock_aura",
            "line_element_count_damage",
            "lightning_strike",
            "transfer_damage",
            "pass_attack_count",
            "charge_damage",
            "charge_cap",
            "shock_damage_bonus",
            "transfer_stack_add"
        });

        public static bool IsImplemented(string id)
        {
            foreach (var implemented in ImplementedEffectIds)
                if (string.Equals(implemented, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
