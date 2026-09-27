using System;
using System.Collections.Generic;
using System.IO;
using GaeBullBing.Core.Data;
using UnityEditor;
using UnityEngine;

namespace GaeBullBing.Editor
{
    public static class DiceJsonImporter
    {
        public const string JsonPath = "Assets/GaeBullBing/Data/Json/Dice.json";
        public const string OutputFolder = "Assets/GaeBullBing/Data/Dice";
        public const string RuntimeDatabasePath =
            "Assets/Resources/GaeBullBing/DiceDatabase.asset";

        [MenuItem("GaeBullBing/Data/Import Dice JSON")]
        public static void Import()
        {
            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (jsonAsset == null)
                throw new FileNotFoundException(JsonPath);

            var database = JsonUtility.FromJson<DiceDatabaseJson>(jsonAsset.text);
            if (database?.dice == null)
                throw new InvalidOperationException("dice 배열이 없습니다.");

            EnsureFolder();
            var importedIds = new HashSet<string>();
            var importedDefinitions = new List<DiceDefinition>();
            foreach (var source in database.dice)
            {
                Validate(source, importedIds);
                if (!Enum.TryParse(source.grade, true, out DiceGrade grade))
                    throw new InvalidOperationException($"{source.id}의 grade 값이 올바르지 않습니다: {source.grade}");
                if (!ColorUtility.TryParseHtmlString(source.color, out var color))
                    throw new InvalidOperationException($"{source.id}의 color 값이 올바르지 않습니다: {source.color}");

                var definition = FindOrCreate(source.id);
                var serialized = new SerializedObject(definition);
                serialized.FindProperty("id").stringValue = source.id;
                serialized.FindProperty("displayName").stringValue = source.name;
                serialized.FindProperty("grade").enumValueIndex = (int)grade;
                SetFaces(serialized.FindProperty("faces"), source.faces);
                serialized.FindProperty("color").colorValue = color;
                serialized.FindProperty("description").stringValue = source.description ?? string.Empty;
                SetEffects(serialized.FindProperty("effects"), source.effect_ids);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                definition.name = source.id;
                EditorUtility.SetDirty(definition);
                importedDefinitions.Add(definition);
            }

            var weights = ParseGradeWeights(database.metadata);
            UpdateRuntimeDatabase(importedDefinitions, weights);
            AssetDatabase.SaveAssets();
            GaeBullBing.Core.Dice.DiceCatalog.ClearCache();
            Debug.Log($"주사위 JSON 임포트 완료: {database.dice.Length}개 갱신");
        }

        private static void Validate(DiceJson source, ISet<string> ids)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.id) || !ids.Add(source.id))
                throw new InvalidOperationException($"비어 있거나 중복된 주사위 ID입니다: {source?.id}");
            if (string.IsNullOrWhiteSpace(source.name))
                throw new InvalidOperationException($"{source.id}의 name이 비어 있습니다.");
            if (source.faces == null || source.faces.Length != 6)
                throw new InvalidOperationException($"{source.id}의 faces는 정확히 6개여야 합니다.");
            foreach (var face in source.faces)
                if (face < 1 || face > 6)
                    throw new InvalidOperationException($"{source.id}의 주사위 눈은 1~6이어야 합니다: {face}");
        }

        private static DiceDefinition FindOrCreate(string id)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:DiceDefinition", new[] { OutputFolder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<DiceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Id == id)
                    return asset;
            }

            var created = ScriptableObject.CreateInstance<DiceDefinition>();
            AssetDatabase.CreateAsset(created, $"{OutputFolder}/{id}.asset");
            return created;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(OutputFolder)) return;
            Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();
        }

        private static float[] ParseGradeWeights(DiceMetadataJson[] metadata)
        {
            var source = metadata != null && metadata.Length > 0 ? metadata[0]?.weight : null;
            if (source == null)
                throw new InvalidOperationException("metadata[0].weight가 없습니다.");
            var result = new float[Enum.GetValues(typeof(DiceGrade)).Length];
            result[(int)DiceGrade.Common] = source.Common;
            result[(int)DiceGrade.Uncommon] = source.Uncommon;
            result[(int)DiceGrade.Rare] = source.Rare;
            result[(int)DiceGrade.Epic] = source.Epic;
            result[(int)DiceGrade.Legendary] = source.Legendary;
            var total = 0f;
            foreach (var weight in result)
            {
                if (weight < 0f) throw new InvalidOperationException("주사위 등급 weight는 음수일 수 없습니다.");
                total += weight;
            }
            if (total <= 0f) throw new InvalidOperationException("주사위 등급 weight 합계가 0입니다.");
            return result;
        }

        private static void UpdateRuntimeDatabase(
            IReadOnlyList<DiceDefinition> definitions,
            IReadOnlyList<float> gradeWeights)
        {
            var directory = Path.GetDirectoryName(RuntimeDatabasePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var database = AssetDatabase.LoadAssetAtPath<DiceDatabaseDefinition>(RuntimeDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<DiceDatabaseDefinition>();
                AssetDatabase.CreateAsset(database, RuntimeDatabasePath);
            }

            var serialized = new SerializedObject(database);
            var diceProperty = serialized.FindProperty("dice");
            diceProperty.arraySize = definitions.Count;
            for (var index = 0; index < definitions.Count; index++)
                diceProperty.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            var weightProperty = serialized.FindProperty("gradeWeights");
            weightProperty.arraySize = gradeWeights.Count;
            for (var index = 0; index < gradeWeights.Count; index++)
                weightProperty.GetArrayElementAtIndex(index).floatValue = gradeWeights[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static void SetFaces(SerializedProperty property, int[] faces)
        {
            property.arraySize = faces.Length;
            for (var index = 0; index < faces.Length; index++)
                property.GetArrayElementAtIndex(index).intValue = faces[index];
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

        [Serializable]
        private sealed class DiceDatabaseJson
        {
            public DiceMetadataJson[] metadata;
            public DiceJson[] dice;
        }

        [Serializable]
        private sealed class DiceMetadataJson { public DiceWeightJson weight; }

        [Serializable]
        private sealed class DiceWeightJson
        {
            public float Common;
            public float Uncommon;
            public float Rare;
            public float Epic;
            public float Legendary;
        }

        [Serializable]
        private sealed class DiceJson
        {
            public string id;
            public string name;
            public string grade;
            public int[] faces;
            public string color;
            public string description;
            public EffectJson[] effect_ids;
            // passive는 형식에만 존재하며 현재 단계에서는 의도적으로 파싱하지 않는다.
        }
        [Serializable] private sealed class EffectJson { public string id; public float value; }
    }

    public sealed class DiceJsonAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
                if (path == DiceJsonImporter.JsonPath)
                {
                    DiceJsonImporter.Import();
                    return;
                }
        }
    }
}
