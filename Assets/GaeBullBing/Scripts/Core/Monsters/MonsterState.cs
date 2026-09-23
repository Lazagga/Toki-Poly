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
            set => burnStacks = value < 0 ? 0 : value > 20 ? 20 : value;
        }
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

        public bool IsDead => CurrentHealth <= 0;
        public bool IsBoss => Tier == MonsterTier.Boss;
        public bool IsImmuneTo(string statusId) =>
            !string.IsNullOrWhiteSpace(statusId) && StatusImmunities.Contains(statusId);

        public float GetDefense(DifficultyState difficulty)
        {
            var levelBonus = difficulty == null || IsBoss
                ? 0f
                : Math.Max(0, difficulty.Level - 1) * Math.Max(0f, difficulty.DefensePerLevel);
            return Math.Max(0f, BaseDefense + levelBonus);
        }

        public float ReceiveDamage(float rawDamage, DifficultyState difficulty)
        {
            var modifiedDamage = Math.Max(0f, rawDamage) * (Shocked ? 1.3f : 1f);
            var defense = GetDefense(difficulty);
            var actualDamage = modifiedDamage * 100f / (defense + 100f);
            CurrentHealth -= actualDamage;
            return actualDamage;
        }
    }
}
