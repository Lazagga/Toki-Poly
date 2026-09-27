#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using UnityEditor;
using UnityEngine;

namespace GaeBullBing.Editor
{
    public static class DifficultyJsonImporter
    {
        public const string JsonPath = "Assets/GaeBullBing/Data/Json/Pattern.json";
        public const string RuntimeDatabasePath = "Assets/Resources/GaeBullBing/DifficultyDatabase.asset";

        [MenuItem("GaeBullBing/Data/Import Difficulty JSON")]
        public static void Import()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (json == null) throw new FileNotFoundException(JsonPath);
            var source = JsonUtility.FromJson<DifficultyDatabaseJson>(json.text);
            if (source?.wavedata == null || source.wavedata.Length == 0 || source.wave_patterns == null)
                throw new InvalidOperationException("Pattern.json의 지역/웨이브 데이터가 비어 있습니다.");
            var monsterDatabase = AssetDatabase.LoadAssetAtPath<MonsterDatabaseDefinition>(MonsterJsonImporter.RuntimeDatabasePath);
            if (monsterDatabase == null) throw new InvalidOperationException("Monster.json을 먼저 임포트해야 합니다.");
            var monsterIds = new HashSet<string>();
            foreach (var monster in monsterDatabase.Monsters) if (monster != null) monsterIds.Add(monster.Id);

            var regions = new List<RegionDifficultyData>();
            foreach (var common in source.wavedata)
            {
                if (common == null || string.IsNullOrWhiteSpace(common.region_id)) continue;
                var regionPatterns = new List<DifficultyPatternJson>();
                foreach (var pattern in source.wave_patterns)
                    if (pattern != null && pattern.region_id == common.region_id) regionPatterns.Add(pattern);
                regionPatterns.Sort((a, b) => a.level.CompareTo(b.level));
                if (regionPatterns.Count == 0) throw new InvalidOperationException($"{common.region_id}의 웨이브 패턴이 없습니다.");
                var patterns = new DifficultyPatternData[regionPatterns.Count];
                for (var i = 0; i < regionPatterns.Count; i++)
                {
                    var pattern = regionPatterns[i];
                    if (pattern.spawn_pattern == null || pattern.spawn_pattern.Length == 0)
                        throw new InvalidOperationException($"{common.region_id} level {pattern.level} 패턴이 비었습니다.");
                    foreach (var id in pattern.spawn_pattern)
                        if (!monsterIds.Contains(id)) throw new InvalidOperationException($"존재하지 않는 몬스터 ID: {id}");
                    patterns[i] = new DifficultyPatternData
                    {
                        Level = Mathf.Max(1, pattern.level),
                        RequiredKills = i * Mathf.Max(1, common.required_kills),
                        HealthMultiplier = Mathf.Pow(common.multiplier > 0f ? common.multiplier : 1f, i),
                        MonsterIds = pattern.spawn_pattern
                    };
                }
                regions.Add(new RegionDifficultyData
                {
                    RegionId = common.region_id,
                    KillsPerLevel = Mathf.Max(1, common.required_kills),
                    HealthMultiplierPerLevel = common.multiplier > 0f ? common.multiplier : 1f,
                    DefensePerLevel = Mathf.Max(0f, common.defense_per_wave),
                    Patterns = patterns
                });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeDatabasePath));
            var database = AssetDatabase.LoadAssetAtPath<DifficultyDatabaseDefinition>(RuntimeDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<DifficultyDatabaseDefinition>();
                AssetDatabase.CreateAsset(database, RuntimeDatabasePath);
            }
            var serialized = new SerializedObject(database);
            WriteRegions(serialized.FindProperty("regions"), regions);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"Difficulty JSON 임포트 완료: {regions.Count}개 지역");
        }

        private static void WriteRegions(SerializedProperty destination, List<RegionDifficultyData> source)
        {
            destination.arraySize = source.Count;
            for (var i = 0; i < source.Count; i++)
            {
                var target = destination.GetArrayElementAtIndex(i);
                target.FindPropertyRelative("RegionId").stringValue = source[i].RegionId;
                target.FindPropertyRelative("KillsPerLevel").intValue = source[i].KillsPerLevel;
                target.FindPropertyRelative("HealthMultiplierPerLevel").floatValue = source[i].HealthMultiplierPerLevel;
                target.FindPropertyRelative("DefensePerLevel").floatValue = source[i].DefensePerLevel;
                var patterns = target.FindPropertyRelative("Patterns");
                patterns.arraySize = source[i].Patterns.Length;
                for (var j = 0; j < source[i].Patterns.Length; j++)
                {
                    var pattern = patterns.GetArrayElementAtIndex(j);
                    var value = source[i].Patterns[j];
                    pattern.FindPropertyRelative("Level").intValue = value.Level;
                    pattern.FindPropertyRelative("RequiredKills").intValue = value.RequiredKills;
                    pattern.FindPropertyRelative("HealthMultiplier").floatValue = value.HealthMultiplier;
                    var ids = pattern.FindPropertyRelative("MonsterIds");
                    ids.arraySize = value.MonsterIds.Length;
                    for (var k = 0; k < value.MonsterIds.Length; k++) ids.GetArrayElementAtIndex(k).stringValue = value.MonsterIds[k];
                }
            }
        }

        [Serializable] private sealed class DifficultyDatabaseJson { public DifficultyCommonJson[] wavedata; public DifficultyPatternJson[] wave_patterns; }
        [Serializable] private sealed class DifficultyCommonJson { public string region_id; public int required_kills; public float multiplier; public float defense_per_wave; }
        [Serializable] private sealed class DifficultyPatternJson { public string region_id; public int level; public string[] spawn_pattern; }
    }

    public sealed class DifficultyJsonAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                if (path == DifficultyJsonImporter.JsonPath) { DifficultyJsonImporter.Import(); return; }
        }
    }
}
#endif
