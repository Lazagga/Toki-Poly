using System;
using UnityEngine;

namespace GaeBullBing.Core.Data
{
    [CreateAssetMenu(menuName = "GaeBullBing/Data/Tower", fileName = "TowerDefinition")]
    public sealed class TowerDefinition : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private TowerElement element;
        [SerializeField, Min(1)] private int tier = 1;
        [SerializeField, Min(0)] private int damage;
        [SerializeField, Min(0)] private int range;
        [SerializeField, Min(1)] private int targetCount = 1;
        [SerializeField, Min(1)] private int attackCount = 1;
        [SerializeField] private TowerUpgradeEffect[] baseEffects = Array.Empty<TowerUpgradeEffect>();

        public string Id => id;
        public string DisplayName => displayName;
        public TowerElement Element => element;
        public int Tier => tier;
        public int Damage => damage;
        public int Range => range;
        public int TargetCount => targetCount;
        public int AttackCount => attackCount;
        public TowerUpgradeEffect[] BaseEffects => baseEffects;
    }

}
