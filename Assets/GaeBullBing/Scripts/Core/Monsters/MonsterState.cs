using System;
using System.Collections.Generic;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Monsters
{
    public sealed class CarriedTower
    {
        public TowerState Tower;
        public int OriginTileIndex;
    }

    public sealed class MonsterState
    {
        public int InstanceId { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public MonsterTier Tier { get; set; }
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; set; }
        public float BaseDefense { get; set; }
        public int CurrentTileIndex { get; set; }
        public int MoveDistance { get; set; }
        public int DistanceTravelled { get; set; }
        private int burnStacks;
        public int BurnStacks
        {
            get => burnStacks;
            set
            {
                burnStacks = Math.Max(0, Math.Min(MaxBurnStacks, value));
                while (BurnSourceTowerIds.Count < burnStacks) BurnSourceTowerIds.Add(0);
                if (BurnSourceTowerIds.Count > burnStacks)
                    BurnSourceTowerIds.RemoveRange(burnStacks, BurnSourceTowerIds.Count - burnStacks);
            }
        }
        public List<int> BurnSourceTowerIds { get; } = new();
        public int MaxBurnStacks { get; set; } = 20;
        public int BurnSourceTowerId { get; set; }
        public float ShockDamageBonus { get; set; } = .3f;
        public float TileVulnerability { get; set; }
        public bool DamageStoreMarked { get; set; }
        public float StoredDamage { get; set; }
        public float DamageStoreMultiplier { get; set; }
        public int DamageStoreSourceTowerId { get; set; }
        public bool LineTeleportConsumed { get; set; }
        public int NextMoveReduction { get; set; }
        public int KnockbackImmunityLine { get; set; } = -1;
        public int LastDamageTowerId { get; set; }
        public bool DeathEffectsHandled { get; set; }
        public int FrostbiteStacks { get; set; }
        public int FrostbiteBurstThreshold { get; set; } = 3;
        public float FrostbiteBurstDamageMultiplier { get; set; } = 3f;
        public bool TouchedFireThisMove { get; set; }
        public bool Shocked { get; set; }
        public int FrozenMovesRemaining { get; set; }
        public bool FreezeImmunityPending { get; set; }
        public bool FreezeImmuneThisTurn { get; set; }
        public bool CanReceiveFreeze => !FreezeImmunityPending && !FreezeImmuneThisTurn && !IsImmuneTo("freeze");
        public int StunnedMovesRemaining { get; set; }
        public bool KnockbackConsumed { get; set; }
        public bool KnockbackImmunityPending { get; set; }
        public bool PhysicsGuardConsumed { get; set; }
        public bool PhysicsGuardTriggeredThisTurn { get; set; }
        public bool BossInitialFeatherPlaced { get; set; }
        public bool IsNewlySpawned { get; set; }
        public HashSet<string> StatusImmunities { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, float> PassiveEffects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int PassiveTurn { get; private set; } = 1;
        private int passiveRound = int.MinValue;
        private int blockedStatuses;
        public MonsterPassiveRules PassiveRules { get; set; } = new();
        public bool BushHealedThisTurn { get; set; }
        public List<CarriedTower> CarriedTowers { get; } = new();
        public bool IsSubmerged => GetPassiveValue("submerge_cycle_length_turns") >= 1 &&
            (PassiveTurn - 1) % Math.Max(1, (int)GetPassiveValue("submerge_cycle_length_turns")) <
            Math.Max(0, (int)GetPassiveValue("submerge_duration_turns"));
        public bool IsInvincible => IsSubmerged && HasPassive("submerged_invincible");
        public bool IsTargetable => !IsDead && !(IsSubmerged && PassiveRules.ExcludeSubmergedFromTargeting);
        public bool IgnoresTileEffects => IsSubmerged && HasPassive("submerged_tile_effect_immune");
        public bool IgnoresMovementTileEffects => IgnoresTileEffects || HasPassive("move_tile_effect_immune");
        // The legacy boss movement/feather pattern belongs to the crow, not every boss.
        public bool UsesCrowPattern => IsBoss && (DefinitionId == "BOSS_001" || string.IsNullOrEmpty(DefinitionId));
        public bool SkipsIntermediateTiles => UsesCrowPattern || HasPassive("move_tile_effect_immune");
        public bool IsPeriodicallyStopped
        {
            get
            {
                var interval = (int)GetPassiveValue("periodic_stop_interval_turns");
                return interval > 0 && PassiveTurn >= interval &&
                    (PassiveTurn - interval) % interval < Math.Max(0, (int)GetPassiveValue("periodic_stop_duration_turns"));
            }
        }
        public bool OnIceOverlay { get; set; }
        public void BeginPassiveTurn(int round)
        {
            if (passiveRound == round) return;
            if (passiveRound != int.MinValue) PassiveTurn++;
            passiveRound = round;
            blockedStatuses = 0;
            BushHealedThisTurn = false;
        }
        public bool TryReceiveStatus(string status, bool transferred = false)
        {
            if (IsDead || IsImmuneTo(status)) return false;
            if ((transferred && !PassiveRules.MoltBlocksTransferredStatuses) ||
                (status == "burn" && !PassiveRules.MoltBlocksBurnStacks)) return true;
            if (blockedStatuses >= Math.Max(0, (int)GetPassiveValue("molt_status_block_per_turn"))) return true;
            blockedStatuses++;
            return false;
        }
        private float PassiveDamageMultiplier
        {
            get
            {
                var multiplier = 1f;
                if (OnIceOverlay && !IgnoresTileEffects)
                    multiplier *= 1f - Math.Max(0f, Math.Min(100f, GetPassiveValue("ice_overlay_damage_reduction_percent"))) / 100f;
                if (!HasPassive("stop_damage_reduction_only_while_stopped") || IsPeriodicallyStopped)
                    multiplier *= 1f - Math.Max(0f, Math.Min(100f, GetPassiveValue("stop_damage_reduction_percent"))) / 100f;
                return multiplier;
            }
        }

        public bool IsDead => CurrentHealth <= 0;
        public bool IsBoss => Tier == MonsterTier.Boss;
        public bool IsImmuneTo(string statusId) =>
            !string.IsNullOrWhiteSpace(statusId) && StatusImmunities.Contains(statusId);

        public bool HasPassive(string id) => GetPassiveValue(id) != 0f;

        public float GetPassiveValue(string id) =>
            !string.IsNullOrWhiteSpace(id) && PassiveEffects.TryGetValue(id, out var value) ? value : 0f;

        public float GetDefense(DifficultyState difficulty)
        {
            var levelBonus = difficulty == null || IsBoss
                ? 0f
                : Math.Max(0, difficulty.Level - 1) * Math.Max(0f, difficulty.DefensePerLevel);
            return Math.Max(0f, BaseDefense + levelBonus);
        }

        public float ReceiveDamage(float rawDamage, DifficultyState difficulty)
        {
            var modifiedDamage = Math.Max(0f, rawDamage) *
                (1f + (Shocked ? ShockDamageBonus : 0f) + TileVulnerability);
            var defense = GetDefense(difficulty);
            var actualDamage = modifiedDamage * 100f / (defense + 100f);
            return ReceiveResolvedDamage(actualDamage);
        }

        // Damage copied from an already resolved hit must not apply defense or
        // incoming multipliers a second time. Keep damage-store accounting.
        public float ReceiveResolvedDamage(float damage)
        {
            var actualDamage = IsInvincible ? 0f : Math.Max(0f, damage) * PassiveDamageMultiplier;
            CurrentHealth -= actualDamage;
            if (DamageStoreMarked) StoredDamage += actualDamage;
            return actualDamage;
        }
    }
}
