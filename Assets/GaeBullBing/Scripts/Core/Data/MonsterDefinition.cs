using System;
using UnityEngine;

namespace GaeBullBing.Core.Data
{
    [CreateAssetMenu(menuName = "GaeBullBing/Data/Monster", fileName = "MonsterDefinition")]
    public sealed class MonsterDefinition : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private MonsterTier tier;
        [SerializeField] private string regionId = "REGION_01";
        [SerializeField, TextArea] private string story = string.Empty;
        [SerializeField, Min(1)] private int appearanceWave = 1;
        [SerializeField, Min(1)] private int maxHp = 1;
        [SerializeField, Min(1)] private int moveDistance = 1;
        [SerializeField, Min(0f)] private float baseDefense;
        [SerializeField] private string[] statusImmunities = Array.Empty<string>();
        [SerializeField] private TowerUpgradeEffect[] passiveEffects = Array.Empty<TowerUpgradeEffect>();
        [SerializeField, Min(0)] private int killRewardDicePoints;

        public string Id => id;
        public string DisplayName => displayName;
        public MonsterTier Tier => tier;
        public string RegionId => regionId;
        public string Story => story;
        public int AppearanceWave => appearanceWave;
        public int MaxHp => maxHp;
        public int MoveDistance => moveDistance;
        public float BaseDefense => baseDefense;
        public string[] StatusImmunities => statusImmunities;
        public TowerUpgradeEffect[] PassiveEffects => passiveEffects;
        public int KillRewardDicePoints => killRewardDicePoints;
    }
}
