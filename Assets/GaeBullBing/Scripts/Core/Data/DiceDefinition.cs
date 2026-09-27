using System;
using UnityEngine;

namespace GaeBullBing.Core.Data
{
    public enum DiceGrade
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    [CreateAssetMenu(menuName = "GaeBullBing/Data/Dice", fileName = "DiceDefinition")]
    public sealed class DiceDefinition : ScriptableObject
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private DiceGrade grade;
        [SerializeField] private int[] faces = Array.Empty<int>();
        [SerializeField] private Color color = Color.white;
        [SerializeField, TextArea] private string description = string.Empty;
        [SerializeField] private TowerUpgradeEffect[] effects = Array.Empty<TowerUpgradeEffect>();

        public string Id => id;
        public string DisplayName => displayName;
        public DiceGrade Grade => grade;
        public int[] Faces => faces;
        public Color Color => color;
        public string Description => description;
        public TowerUpgradeEffect[] Effects => effects;
    }
}
