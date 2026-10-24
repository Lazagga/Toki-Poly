using System;
using System.Collections.Generic;

namespace GaeBullBing.Core.Monsters
{
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
            CurrentHealth -= actualDamage;
            if (DamageStoreMarked) StoredDamage += actualDamage;
            return actualDamage;
        }
    }
}
