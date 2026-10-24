#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using GaeBullBing.Core.Data;
using UnityEditor;
using UnityEngine;

namespace GaeBullBing.Editor
{
    public static class RegionJsonImporter
    {
        public const string JsonPath = "Assets/GaeBullBing/Data/Json/Region.json";
        private const string MonsterJsonPath = "Assets/GaeBullBing/Data/Json/Monster.json";
        public const string RuntimeDatabasePath = "Assets/Resources/GaeBullBing/RegionDatabase.asset";

        [MenuItem("GaeBullBing/Data/Import Region JSON")]
        public static void Import()
        {
            var regionAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            var monsterAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(MonsterJsonPath);
            if (regionAsset == null) throw new FileNotFoundException(JsonPath);
            if (monsterAsset == null) throw new FileNotFoundException(MonsterJsonPath);
            // Validate the complete input before touching the generated asset.
            var values = Parse(regionAsset.text, monsterAsset.text);

            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeDatabasePath));
            var database = AssetDatabase.LoadAssetAtPath<RegionDatabaseDefinition>(RuntimeDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<RegionDatabaseDefinition>();
                AssetDatabase.CreateAsset(database, RuntimeDatabasePath);
            }
            var serialized = new SerializedObject(database);
            var regions = serialized.FindProperty("regions");
            regions.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) Write(regions.GetArrayElementAtIndex(i), values[i]);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"Region JSON 임포트 완료: {values.Length}개 지역");
        }

        public static RegionDefinition[] Parse(string regionJson, string monsterJson)
        {
            if (string.IsNullOrWhiteSpace(regionJson))
                throw new InvalidOperationException("Region.json의 내용이 비어 있습니다.");
            if (string.IsNullOrWhiteSpace(monsterJson))
                throw new InvalidOperationException("Monster.json의 내용이 비어 있습니다.");
            RegionJsonRoot source;
            MonsterJsonRoot monsterSource;
            try { source = JsonUtility.FromJson<RegionJsonRoot>(regionJson); }
            catch (ArgumentException exception)
            { throw new InvalidOperationException("Region.json의 JSON 형식이 올바르지 않습니다.", exception); }
            try { monsterSource = JsonUtility.FromJson<MonsterJsonRoot>(monsterJson); }
            catch (ArgumentException exception)
            { throw new InvalidOperationException("Monster.json의 JSON 형식이 올바르지 않습니다.", exception); }
            if (source?.region_gimmicks == null || source.region_gimmicks.Length == 0)
                throw new InvalidOperationException("Region.json의 region_gimmicks 배열이 없거나 비어 있습니다.");

            var metadata = new Dictionary<string, RegionMetadata>();
            if (monsterSource?.region_definitions != null)
                foreach (var definition in monsterSource.region_definitions)
                    if (definition != null && !string.IsNullOrWhiteSpace(definition.id)) metadata[definition.id] = definition;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var values = new RegionDefinition[source.region_gimmicks.Length];
            for (var i = 0; i < source.region_gimmicks.Length; i++)
            {
                var item = source.region_gimmicks[i];
                if (item == null || string.IsNullOrWhiteSpace(item.region_id))
                    throw new InvalidOperationException($"Region.json의 region_gimmicks[{i}]에 region_id가 없습니다.");
                if (!ids.Add(item.region_id))
                    throw new InvalidOperationException($"Region.json의 region_id가 중복되었습니다: {item.region_id}");
                metadata.TryGetValue(item.region_id ?? string.Empty, out var info);
                values[i] = new RegionDefinition
                {
                    Id = item.region_id ?? string.Empty,
                    DisplayName = info?.name ?? item.name ?? item.region_id,
                    Order = info != null ? Mathf.Max(1, info.order) : i + 1,
                    GimmickId = item.gimmick_id ?? string.Empty,
                    Status = item.status ?? string.Empty,
                    Type = item.type ?? string.Empty,
                    Description = item.description ?? string.Empty,
                    AppliedEffectId = item.parameters?.applied_effect_id ?? string.Empty,
                    Scope = item.parameters?.scope ?? string.Empty,
                    TilesPerTurn = item.parameters?.tiles_per_turn ?? 0,
                    TargetTileIndex = item.parameters?.target_tile_index ?? -1,
                    TargetTileIndices = item.parameters?.target_tile_indices ?? Array.Empty<int>(),
                    OverrideEffectId = item.parameters?.override_effect_id ?? string.Empty,
                    OverrideEffectValue = item.parameters?.override_effect_value ?? 0f,
                    DurationTurns = item.parameters?.duration_turns ?? 0,
                    DisallowBuild = item.parameters?.restriction?.disallow_build ?? false,
                    DisallowUpgrade = item.parameters?.restriction?.disallow_upgrade ?? false,
                    TriggerCondition = item.parameters?.trigger_condition ?? string.Empty,
                    RewardEffectId = item.parameters?.reward_effect_id ?? string.Empty,
                    RewardEffectValue = item.parameters?.reward_effect_value ?? 0f,
                    BackgroundTint = GetFallbackTint(item.region_id)
                };
            }

            return values;
        }

        private static Color GetFallbackTint(string id) => id switch
        {
            "REGION_02" => new Color(0.78f, 0.9f, 1f),
            "REGION_03" => new Color(1f, 0.86f, 0.62f),
            "REGION_04" => new Color(0.58f, 0.76f, 0.58f),
            _ => Color.white
        };

        private static void Write(SerializedProperty target, RegionDefinition value)
        {
            target.FindPropertyRelative("Id").stringValue = value.Id;
            target.FindPropertyRelative("DisplayName").stringValue = value.DisplayName;
            target.FindPropertyRelative("Order").intValue = value.Order;
            target.FindPropertyRelative("GimmickId").stringValue = value.GimmickId;
            target.FindPropertyRelative("Status").stringValue = value.Status;
            target.FindPropertyRelative("Type").stringValue = value.Type;
            target.FindPropertyRelative("Description").stringValue = value.Description;
            target.FindPropertyRelative("AppliedEffectId").stringValue = value.AppliedEffectId;
            target.FindPropertyRelative("Scope").stringValue = value.Scope;
            target.FindPropertyRelative("TilesPerTurn").intValue = value.TilesPerTurn;
            target.FindPropertyRelative("TargetTileIndex").intValue = value.TargetTileIndex;
            var indices = target.FindPropertyRelative("TargetTileIndices");
            indices.arraySize = value.TargetTileIndices.Length;
            for (var i = 0; i < value.TargetTileIndices.Length; i++)
                indices.GetArrayElementAtIndex(i).intValue = value.TargetTileIndices[i];
            target.FindPropertyRelative("OverrideEffectId").stringValue = value.OverrideEffectId;
            target.FindPropertyRelative("OverrideEffectValue").floatValue = value.OverrideEffectValue;
            target.FindPropertyRelative("DurationTurns").intValue = value.DurationTurns;
            target.FindPropertyRelative("DisallowBuild").boolValue = value.DisallowBuild;
            target.FindPropertyRelative("DisallowUpgrade").boolValue = value.DisallowUpgrade;
            target.FindPropertyRelative("TriggerCondition").stringValue = value.TriggerCondition;
            target.FindPropertyRelative("RewardEffectId").stringValue = value.RewardEffectId;
            target.FindPropertyRelative("RewardEffectValue").floatValue = value.RewardEffectValue;
            target.FindPropertyRelative("BackgroundTint").colorValue = value.BackgroundTint;
        }

        [Serializable] private sealed class RegionJsonRoot { public RegionJson[] region_gimmicks; }
        [Serializable] private sealed class RegionJson { public string region_id; public string gimmick_id; public string name; public string status; public string type; public string description; public ParametersJson parameters; }
        [Serializable] private sealed class ParametersJson
        {
            public string applied_effect_id; public string scope; public int tiles_per_turn;
            public int target_tile_index = -1; public string override_effect_id; public float override_effect_value;
            public int[] target_tile_indices;
            public int duration_turns; public RestrictionJson restriction;
            public string trigger_condition; public string reward_effect_id; public float reward_effect_value;
        }
        [Serializable] private sealed class RestrictionJson { public bool disallow_build; public bool disallow_upgrade; }
        [Serializable] private sealed class MonsterJsonRoot { public RegionMetadata[] region_definitions; }
        [Serializable] private sealed class RegionMetadata { public string id; public string name; public int order; }
    }

    public sealed class RegionJsonAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                if (path == RegionJsonImporter.JsonPath || path == "Assets/GaeBullBing/Data/Json/Monster.json")
                { RegionJsonImporter.Import(); return; }
        }
    }
}
#endif
