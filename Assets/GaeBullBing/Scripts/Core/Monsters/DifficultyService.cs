using System;
using System.Collections.Generic;
using GaeBullBing.Core.Data;

namespace GaeBullBing.Core.Monsters
{
    [Serializable]
    public sealed class DifficultyPatternData
    {
        public int Level = 1;
        public int RequiredKills;
        public float HealthMultiplier = 1f;
        public string[] MonsterIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class RegionDifficultyData
    {
        public string RegionId = string.Empty;
        public int KillsPerLevel = 1;
        public float HealthMultiplierPerLevel = 1f;
        public float DefensePerLevel;
        public DifficultyPatternData[] Patterns = Array.Empty<DifficultyPatternData>();
    }

    public sealed class DifficultyState
    {
        public int KillCount { get; set; }
        public int Level { get; set; } = 1;
        public int PatternIndex { get; set; }
        public float DefensePerLevel { get; set; }
    }

    public sealed class MonsterDatabase
    {
        private readonly Dictionary<string, MonsterDefinition> definitions = new();

        public MonsterDatabase(IEnumerable<MonsterDefinition> source)
        {
            foreach (var definition in source)
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Id))
                    definitions[definition.Id] = definition;
        }

        public MonsterDefinition Get(string id) =>
            definitions.TryGetValue(id, out var definition)
                ? definition
                : throw new KeyNotFoundException($"몬스터 ID를 찾을 수 없습니다: {id}");
    }

    public sealed class DifficultyService
    {
        private readonly DifficultyPatternData[] patterns;
        private readonly int killsPerLevel;
        private readonly float healthMultiplierPerLevel;
        private readonly float baseHealthMultiplier;
        private readonly float defensePerLevel;
        private readonly int bossLevel;

        public DifficultyService(
            DifficultyPatternData[] patterns,
            int killsPerLevel = 0,
            float healthMultiplierPerLevel = 0f,
            float defensePerLevel = 0f,
            int bossLevel = FinalBossLevel)
        {
            this.patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
            if (patterns.Length == 0)
                throw new ArgumentException("난이도 패턴이 하나 이상 필요합니다.", nameof(patterns));
            Array.Sort(this.patterns, (left, right) => left.RequiredKills.CompareTo(right.RequiredKills));
            this.killsPerLevel = killsPerLevel > 0
                ? killsPerLevel
                : InferKillsPerLevel(this.patterns);
            this.healthMultiplierPerLevel = healthMultiplierPerLevel > 0f
                ? healthMultiplierPerLevel
                : InferHealthMultiplier(this.patterns);
            this.defensePerLevel = Math.Max(0f, defensePerLevel);
            this.bossLevel = Math.Max(1, bossLevel);
            baseHealthMultiplier = Math.Max(0f, this.patterns[0].HealthMultiplier);
        }

        public void Reset(DifficultyState state)
        {
            state.KillCount = 0;
            state.Level = 1;
            state.PatternIndex = 0;
            state.DefensePerLevel = defensePerLevel;
        }

        public void AddKills(DifficultyState state, int count)
        {
            var previousPatternIndex = GetPatternIndex(state.KillCount);
            state.KillCount += Math.Max(0, count);
            var nextPatternIndex = GetPatternIndex(state.KillCount);
            if (previousPatternIndex != nextPatternIndex)
                state.PatternIndex = 0;
            state.Level = GetLevel(state.KillCount);
        }

        public string GetNextMonsterId(DifficultyState state)
        {
            var patternIndex = GetPatternIndex(state.KillCount);
            var pattern = patterns[patternIndex];
            if (pattern.MonsterIds == null || pattern.MonsterIds.Length == 0)
                throw new InvalidOperationException($"난이도 {patternIndex + 1}의 몬스터 패턴이 비어 있습니다.");
            var id = pattern.MonsterIds[state.PatternIndex % pattern.MonsterIds.Length];
            state.PatternIndex = (state.PatternIndex + 1) % pattern.MonsterIds.Length;
            return id;
        }

        public float GetHealthMultiplier(DifficultyState state) =>
            baseHealthMultiplier * (float)Math.Pow(
                healthMultiplierPerLevel,
                GetLevel(state.KillCount) - 1);

        public bool IsFinalPattern(DifficultyState state) =>
            GetPatternIndex(state.KillCount) >= patterns.Length - 1;

        public const int FinalBossLevel = 6;
        public int BossLevel => bossLevel;

        public bool IsBossLevel(DifficultyState state) =>
            GetLevel(state.KillCount) >= BossLevel;

        public int GetRemainingKills(DifficultyState state)
        {
            if (IsBossLevel(state)) return 0;
            var killsIntoCurrentLevel = Math.Max(0, state.KillCount) % killsPerLevel;
            return killsPerLevel - killsIntoCurrentLevel;
        }

        private int GetPatternIndex(int kills)
        {
            return Math.Min(GetLevel(kills) - 1, patterns.Length - 1);
        }

        private int GetLevel(int kills) => Math.Max(0, kills) / killsPerLevel + 1;

        private static int InferKillsPerLevel(DifficultyPatternData[] source) =>
            source.Length > 1
                ? Math.Max(1, source[1].RequiredKills - source[0].RequiredKills)
                : Math.Max(1, source[0].RequiredKills);

        private static float InferHealthMultiplier(DifficultyPatternData[] source)
        {
            if (source.Length < 2 || source[0].HealthMultiplier <= 0f)
                return 1f;
            return Math.Max(0.0001f, source[1].HealthMultiplier / source[0].HealthMultiplier);
        }
    }
}
