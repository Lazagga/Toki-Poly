using System;
using UnityEngine;

namespace GaeBullBing.Core.Data
{
    [Serializable]
    public sealed class RegionDefinition
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public int Order = 1;
        public string GimmickId = string.Empty;
        public string Status = string.Empty;
        public string Type = string.Empty;
        [TextArea] public string Description = string.Empty;
        public string AppliedEffectId = string.Empty;
        public string Scope = string.Empty;
        public int TilesPerTurn;
        public int TargetTileIndex = -1;
        public string OverrideEffectId = string.Empty;
        public float OverrideEffectValue;
        public Sprite Background;
        public Color BackgroundTint = Color.white;
    }

    [CreateAssetMenu(menuName = "GaeBullBing/Data/Region Database", fileName = "RegionDatabase")]
    public sealed class RegionDatabaseDefinition : ScriptableObject
    {
        [SerializeField] private RegionDefinition[] regions = Array.Empty<RegionDefinition>();
        public RegionDefinition[] Regions => regions;

        public RegionDefinition Get(string id)
        {
            foreach (var region in regions)
                if (region != null && string.Equals(region.Id, id, StringComparison.Ordinal))
                    return region;
            return null;
        }
    }
}
