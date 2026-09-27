using System;
using System.Collections.Generic;
using System.IO;
using GaeBullBing.Core;
using GaeBullBing.Core.Data;
using UnityEditor;
using UnityEngine;

namespace GaeBullBing.Editor
{
    public static class MonsterJsonImporter
    {
        public const string JsonPath = "Assets/GaeBullBing/Data/Json/Monster.json";
        public const string OutputFolder = "Assets/GaeBullBing/Data/Monsters";
        public const string RuntimeDatabasePath =
            "Assets/Resources/GaeBullBing/MonsterDatabase.asset";

        [MenuItem("GaeBullBing/Data/Import Monsters JSON")]
        public static void Import()
        {
            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (jsonAsset == null)
            {
                Debug.LogError($"Monster JSON을 찾을 수 없습니다: {JsonPath}");
                return;
            }

            var database = JsonUtility.FromJson<MonsterDatabaseJson>(jsonAsset.text);
            if (database?.monster_database == null)
            {
                Debug.LogError($"monster_database 배열을 읽을 수 없습니다: {JsonPath}");
                return;
            }

            EnsureOutputFolder();
            var existingAssets = FindExistingAssets();
            var importedIds = new HashSet<string>();
            var importedDefinitions = new List<MonsterDefinition>();
            var importedCount = 0;

            foreach (var source in database.monster_database)
            {
                if (!Validate(source, importedIds))
                    continue;

                if (!Enum.TryParse(source.tier, true, out MonsterTier tier))
                {
                    Debug.LogError($"몬스터 {source.id}의 tier 값이 올바르지 않습니다: {source.tier}");
                    continue;
                }

                if (!existingAssets.TryGetValue(source.id, out var definition))
                {
                    definition = ScriptableObject.CreateInstance<MonsterDefinition>();
                    AssetDatabase.CreateAsset(definition, AssetDatabase.GenerateUniqueAssetPath(
                        $"{OutputFolder}/{source.id}.asset"));
                }

                var serialized = new SerializedObject(definition);
                serialized.FindProperty("id").stringValue = source.id;
                serialized.FindProperty("displayName").stringValue = source.name;
                serialized.FindProperty("tier").enumValueIndex = (int)tier;
                serialized.FindProperty("regionId").stringValue = source.region_id ?? string.Empty;
                serialized.FindProperty("story").stringValue = source.story ?? string.Empty;
                serialized.FindProperty("appearanceWave").intValue = Mathf.Max(1, source.appearance_wave);
                serialized.FindProperty("maxHp").intValue = source.base_stats.max_hp;
                serialized.FindProperty("moveDistance").intValue = source.base_stats.move_speed;
                serialized.FindProperty("baseDefense").floatValue = source.base_stats.base_defense;
                SetStringArray(serialized.FindProperty("statusImmunities"), source.status_immunities);
                SetEffects(serialized.FindProperty("passiveEffects"), source.passive_effect_ids);
                serialized.FindProperty("killRewardDicePoints").intValue =
                    Mathf.Max(0, source.kill_rewards?.dice_points ?? 0);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                definition.name = $"{source.id}_{SanitizeFileName(source.name)}";
                EditorUtility.SetDirty(definition);
                importedDefinitions.Add(definition);
                importedCount++;
            }

            UpdateRuntimeDatabase(importedDefinitions);
            AssetDatabase.SaveAssets();
            Debug.Log($"Monster JSON 임포트 완료: {importedCount}개 갱신");
        }

        private static bool Validate(MonsterJson source, ISet<string> importedIds)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.id))
            {
                Debug.LogError("id가 없는 몬스터 데이터는 임포트할 수 없습니다.");
                return false;
            }
            if (!importedIds.Add(source.id))
            {
                Debug.LogError($"중복된 몬스터 id입니다: {source.id}");
                return false;
            }
            if (source.base_stats == null || source.base_stats.max_hp < 1 || source.base_stats.move_speed < 1)
            {
                Debug.LogError($"몬스터 {source.id}의 max_hp와 move_speed는 1 이상이어야 합니다.");
                return false;
            }
            if (source.base_stats.base_defense < 0f)
            {
                Debug.LogError($"몬스터 {source.id}의 base_defense는 0 이상이어야 합니다.");
                return false;
            }
            return true;
        }

        private static Dictionary<string, MonsterDefinition> FindExistingAssets()
        {
            var assets = new Dictionary<string, MonsterDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonsterDefinition", new[] { OutputFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (definition != null && !string.IsNullOrEmpty(definition.Id))
                    assets[definition.Id] = definition;
            }
            return assets;
        }

        private static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                Directory.CreateDirectory(OutputFolder);
        }

        private static void UpdateRuntimeDatabase(IReadOnlyList<MonsterDefinition> definitions)
        {
            var directory = Path.GetDirectoryName(RuntimeDatabasePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }
            var database = AssetDatabase.LoadAssetAtPath<MonsterDatabaseDefinition>(
                RuntimeDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<MonsterDatabaseDefinition>();
                AssetDatabase.CreateAsset(database, RuntimeDatabasePath);
            }
            var serialized = new SerializedObject(database);
            var monsters = serialized.FindProperty("monsters");
            monsters.arraySize = definitions.Count;
            for (var index = 0; index < definitions.Count; index++)
                monsters.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static void SetStringArray(SerializedProperty property, string[] values)
        {
            values ??= Array.Empty<string>();
            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).stringValue = values[index];
        }

        private static void SetEffects(SerializedProperty property, EffectJson[] values)
        {
            values ??= Array.Empty<EffectJson>();
            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                var element = property.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("Id").stringValue = values[index]?.id ?? string.Empty;
                element.FindPropertyRelative("Value").floatValue = values[index]?.value ?? 0f;
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Monster";
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidCharacter, '_');
            return value;
        }

        [Serializable]
        private sealed class MonsterDatabaseJson
        {
            public MonsterJson[] monster_database;
        }

        [Serializable]
        private sealed class MonsterJson
        {
            public string id;
            public string name;
            public string tier;
            public string region_id;
            public string story;
            public int appearance_wave = 1;
            public MonsterBaseStatsJson base_stats;
            public string[] status_immunities;
            public EffectJson[] passive_effect_ids;
            public KillRewardsJson kill_rewards;
        }

        [Serializable]
        private sealed class EffectJson { public string id; public float value; }

        [Serializable]
        private sealed class MonsterBaseStatsJson
        {
            public int max_hp;
            public int move_speed;
            public float base_defense;
        }

        [Serializable]
        private sealed class KillRewardsJson
        {
            public int dice_points;
        }
    }

    public sealed class MonsterJsonAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
            {
                if (path == MonsterJsonImporter.JsonPath)
                {
                    MonsterJsonImporter.Import();
                    return;
                }
            }
        }
    }
}
