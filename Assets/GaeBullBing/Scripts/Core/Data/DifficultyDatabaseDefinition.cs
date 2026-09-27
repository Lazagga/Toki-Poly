using System;
using GaeBullBing.Core.Monsters;
using UnityEngine;

namespace GaeBullBing.Core.Data
{
    public sealed class DifficultyDatabaseDefinition : ScriptableObject
    {
        [SerializeField] private RegionDifficultyData[] regions =
            Array.Empty<RegionDifficultyData>();

        public RegionDifficultyData[] Regions => regions;
        public DifficultyPatternData[] Patterns => regions.Length > 0 ? regions[0].Patterns : Array.Empty<DifficultyPatternData>();
        public int KillsPerLevel => regions.Length > 0 ? regions[0].KillsPerLevel : 1;
        public float HealthMultiplierPerLevel => regions.Length > 0 ? regions[0].HealthMultiplierPerLevel : 1f;
        public float DefensePerLevel => regions.Length > 0 ? regions[0].DefensePerLevel : 0f;

        public RegionDifficultyData GetRegion(string regionId)
        {
            foreach (var region in regions)
                if (region != null && string.Equals(region.RegionId, regionId, StringComparison.Ordinal))
                    return region;
            return null;
        }
    }
}
