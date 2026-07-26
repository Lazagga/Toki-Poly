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
        public bool BonusTier3UpgradeClaimed { get; set; }
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

        public bool HasEffect(string effectId) =>
            !string.IsNullOrWhiteSpace(effectId) && AppliedEffectIds.Contains(effectId);

        public float GetEffectValue(string effectId, float fallback = 0f) =>
            EffectValues.TryGetValue(effectId, out var value) ? value : fallback;
    }
}
