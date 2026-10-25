using System.Collections.Generic;

namespace GaeBullBing.Core.Towers
{
    public enum StoneExitAnimation
    {
        None,
        FallOffBoard,
        ShrinkOnZeroDamage
    }

    public sealed class TowerState
    {
        public int InstanceId { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public int UpgradeTier { get; set; } = 1;
        // Bonus tiles and equipped-dice passives can independently grant an
        // additional tier-3 choice. Track consumed grants as a count so those
        // sources can stack instead of incorrectly sharing a single flag.
        public int AdditionalTier3UpgradesClaimed { get; set; }
        public List<string> AppliedUpgradeIds { get; } = new();
        public List<string> AppliedEffectIds { get; } = new();
        public Dictionary<string, float> EffectValues { get; } = new();
        public List<int> TargetInstanceIds { get; } = new();
        public int AttackCooldownRounds { get; set; }
        public int BonusAttackCount { get; set; }
        public int BonusAttackTurnsRemaining { get; set; }
        public int PendingBonusAttackCount { get; set; }
        public int PendingBonusAttackTurns { get; set; }
        public bool StoneActive { get; set; }
        public int StoneTileIndex { get; set; }
        public float StoneDamageMultiplier { get; set; } = 1f;
        public int StoneBaseDamage { get; set; }
        public List<int> StoneTraversalTiles { get; } = new();
        public StoneExitAnimation StoneExitAnimation { get; set; }
        public int StoneExitTileIndex { get; set; } = -1;
        public bool IsFeatherSealed { get; set; }
        public int LastResolvedDamage { get; set; }
        public float PersonalDamageRateBonus { get; set; }
        public int PersonalDamageFlatBonus { get; set; }
        public int PermanentBonusAttackCount { get; set; }
        public TowerElement Element { get; set; }
        public int LastResolvedRange { get; set; }
        public int LastResolvedTargetCount { get; set; } = 1;
        public bool AttackCountLocked { get; set; }
        public int LastAttackTargetId { get; set; }
        public float ConsecutiveDamageBonus { get; set; }
        public int AttacksThisTurn { get; set; }
        public int OverheatAttackTurns { get; set; }
        public int PeriodicTurnCount { get; set; }
        // Missing entries are ready immediately, including effects acquired on
        // an old tower. Only a successful activation starts its own cooldown.
        public Dictionary<string, int> EffectReadyPhases { get; } = new();
        public bool IsEffectReady(string effectId) =>
            !EffectReadyPhases.TryGetValue(effectId, out var phase) || PeriodicTurnCount >= phase;
        public void StartEffectCooldown(string effectId, int interval) =>
            EffectReadyPhases[effectId] = PeriodicTurnCount + System.Math.Max(1, interval);
        public int ChargeCount { get; set; }
        public float ActiveChargeBonus { get; set; }
        public int StepDamageRound { get; set; } = -1;
        public int PassAttackRound { get; set; } = -1;
        public int LapGrowthDamage { get; set; }
        public List<TimedTowerDamageBuff> TemporaryDamageBuffs { get; } = new();

        public bool HasEffect(string effectId) =>
            !string.IsNullOrWhiteSpace(effectId) && AppliedEffectIds.Contains(effectId);

        public float GetEffectValue(string effectId, float fallback = 0f) =>
            EffectValues.TryGetValue(effectId, out var value) ? value : fallback;
    }

    public sealed class TimedTowerDamageBuff
    {
        public float Rate { get; set; }
        public int ExpiresAfterRound { get; set; }
    }
}
