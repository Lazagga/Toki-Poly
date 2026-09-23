using System;
using System.Collections.Generic;
using System.IO;
using GaeBullBing.Core;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Towers;
using UnityEditor;
using UnityEngine;

namespace GaeBullBing.Editor
{
    public static class TileTowerJsonImporter
    {
        public const string TileJsonPath = "Assets/GaeBullBing/Data/Json/Tile.json";
        public const string TowerJsonPath = "Assets/GaeBullBing/Data/Json/Tower.json";
        public const string UpgradeJsonPath = "Assets/GaeBullBing/Data/Json/Upgrade.json";
        public const string TileFolder = "Assets/GaeBullBing/Data/Tiles";
        public const string TowerFolder = "Assets/GaeBullBing/Data/Towers";
        public const string UpgradeFolder = "Assets/GaeBullBing/Data/Upgrades";
        public const string BoardAssetPath = "Assets/GaeBullBing/Data/Board.asset";
        public const string RuntimeUpgradeDatabasePath =
            "Assets/Resources/GaeBullBing/TowerUpgradeDatabase.asset";
        public const string RuntimeTowerDatabasePath =
            "Assets/Resources/GaeBullBing/TowerDatabase.asset";

        [MenuItem("GaeBullBing/Data/Import Tiles and Towers JSON")]
        public static void Import()
        {
            EnsureFolder(TileFolder);
            EnsureFolder(TowerFolder);
            EnsureFolder(UpgradeFolder);
            var towers = ImportTowers();
            UpdateRuntimeTowerDatabase(towers.Definitions);
            var upgrades = ImportUpgrades();
            UpdateRuntimeUpgradeDatabase(upgrades);
            ImportTiles(towers.Ids);
            AssetDatabase.SaveAssets();
        }

        private static TowerImportResult ImportTowers()
        {
            var source = ReadJson<TowerDatabaseJson>(TowerJsonPath);
            var ids = new HashSet<string>();
            var definitions = new List<TowerDefinition>();
            if (source?.tower_database == null)
                throw new InvalidOperationException("tower_database 배열이 없습니다.");

            foreach (var item in source.tower_database)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id))
                    throw new InvalidOperationException($"비어 있거나 중복된 타워 ID입니다: {item?.id}");
                if (!Enum.TryParse(item.element, true, out TowerElement element))
                    throw new InvalidOperationException($"{item.id}의 속성이 올바르지 않습니다: {item.element}");
                if (item.base_stats == null)
                    throw new InvalidOperationException($"{item.id}의 base_stats가 없습니다.");
                foreach (var effectId in Array.ConvertAll(
                             item.base_effect_ids ?? Array.Empty<EffectJson>(), effect => effect.id))
                    if (!TowerEffectCatalog.IsImplemented(effectId))
                        throw new InvalidOperationException($"{item.id}의 기본 효과가 구현되지 않았습니다: {effectId}");

                var definition = FindOrCreate<TowerDefinition>(TowerFolder, item.id);
                var serialized = new SerializedObject(definition);
                serialized.FindProperty("id").stringValue = item.id;
                serialized.FindProperty("displayName").stringValue = item.name;
                serialized.FindProperty("element").enumValueIndex = (int)element;
                serialized.FindProperty("tier").intValue = Math.Max(1, item.tier);
                serialized.FindProperty("damage").intValue = Math.Max(0, item.base_stats.damage);
                serialized.FindProperty("range").intValue = Math.Max(0, item.base_stats.range);
                serialized.FindProperty("targetCount").intValue = Math.Max(1, item.base_stats.target_count);
                serialized.FindProperty("attackCount").intValue = Math.Max(1, item.base_stats.attack_count);
                SetEffects(serialized.FindProperty("baseEffects"), item.base_effect_ids);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                definition.name = item.id;
                EditorUtility.SetDirty(definition);
                definitions.Add(definition);
            }
            return new TowerImportResult(ids, definitions);
        }

        private static void UpdateRuntimeTowerDatabase(
            IReadOnlyList<TowerDefinition> definitions)
        {
            var directory = Path.GetDirectoryName(RuntimeTowerDatabasePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }
            var database = AssetDatabase.LoadAssetAtPath<TowerDatabaseDefinition>(
                RuntimeTowerDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<TowerDatabaseDefinition>();
                AssetDatabase.CreateAsset(database, RuntimeTowerDatabasePath);
            }
            var serialized = new SerializedObject(database);
            var towers = serialized.FindProperty("towers");
            towers.arraySize = definitions.Count;
            for (var index = 0; index < definitions.Count; index++)
                towers.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static List<TowerUpgradeDefinition> ImportUpgrades()
        {
            var source = ReadJson<UpgradeDatabaseJson>(UpgradeJsonPath);
            var ids = new HashSet<string>();
            var definitions = new List<TowerUpgradeDefinition>();
            if (source?.tower_upgrade_database == null)
                throw new InvalidOperationException("tower_upgrade_database is missing.");
            foreach (var item in source.tower_upgrade_database)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id))
                    throw new InvalidOperationException($"Invalid or duplicate upgrade id: {item?.id}");
                if (!Enum.TryParse(item.element, true, out TowerElement element))
                    throw new InvalidOperationException($"Invalid upgrade element: {item.element}");
                foreach (var effect in item.effect_ids ?? Array.Empty<EffectJson>())
                {
                    if (!TowerEffectCatalog.IsImplemented(effect.id))
                        throw new InvalidOperationException($"{item.id}의 효과가 구현되지 않았습니다: {effect.id}");
                    if (effect.value < 0f)
                        throw new InvalidOperationException($"{item.id}의 효과 수치는 음수일 수 없습니다: {effect.id}");
                }
                foreach (var modifier in item.stat_modifiers ?? Array.Empty<ModifierJson>())
                {
                    var stat = modifier.stat?.ToLowerInvariant();
                    if (stat != "damage" && stat != "range" && stat != "target_count" &&
                        stat != "attack_count")
                        throw new InvalidOperationException($"{item.id}의 지원하지 않는 스탯입니다: {modifier.stat}");
                    var operation = modifier.operation?.ToLowerInvariant();
                    if (operation != "add" && operation != "multiply" && operation != "set")
                        throw new InvalidOperationException($"{item.id}의 지원하지 않는 연산입니다: {modifier.operation}");
                }
                var definition = FindOrCreate<TowerUpgradeDefinition>(UpgradeFolder, item.id);
                var serialized = new SerializedObject(definition);
                serialized.FindProperty("id").stringValue = item.id;
                serialized.FindProperty("displayName").stringValue = item.name;
                serialized.FindProperty("description").stringValue = item.description;
                serialized.FindProperty("element").enumValueIndex = (int)element;
                serialized.FindProperty("tier").intValue = Math.Max(2, item.tier);
                serialized.FindProperty("weight").intValue = Math.Max(0, item.weight);
                SetModifiers(serialized.FindProperty("statModifiers"), item.stat_modifiers);
                SetEffects(serialized.FindProperty("effects"), item.effect_ids);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                definition.name = item.id;
                EditorUtility.SetDirty(definition);
                definitions.Add(definition);
            }
            return definitions;
        }

        private static void UpdateRuntimeUpgradeDatabase(
            IReadOnlyList<TowerUpgradeDefinition> definitions)
        {
            var directory = Path.GetDirectoryName(RuntimeUpgradeDatabasePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }
            var database = AssetDatabase.LoadAssetAtPath<TowerUpgradeDatabaseDefinition>(
                RuntimeUpgradeDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<TowerUpgradeDatabaseDefinition>();
                AssetDatabase.CreateAsset(database, RuntimeUpgradeDatabasePath);
            }
            var serialized = new SerializedObject(database);
            var property = serialized.FindProperty("upgrades");
            property.arraySize = definitions.Count;
            for (var index = 0; index < definitions.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static void SetModifiers(SerializedProperty property, ModifierJson[] values)
        {
            values ??= Array.Empty<ModifierJson>(); property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) { var e = property.GetArrayElementAtIndex(i); e.FindPropertyRelative("Stat").stringValue = values[i].stat; e.FindPropertyRelative("Operation").stringValue = values[i].operation; e.FindPropertyRelative("Value").floatValue = values[i].value; }
        }

        private static void SetEffects(SerializedProperty property, EffectJson[] values)
        {
            values ??= Array.Empty<EffectJson>(); property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) { var e = property.GetArrayElementAtIndex(i); e.FindPropertyRelative("Id").stringValue = values[i].id; e.FindPropertyRelative("Value").floatValue = values[i].value; }
        }

        private static void ImportTiles(ISet<string> towerIds)
        {
            var source = ReadJson<TileDatabaseJson>(TileJsonPath);
            if (source?.tile_definitions == null || source.board_layout == null)
                throw new InvalidOperationException("tile_definitions 또는 board_layout 배열이 없습니다.");

            var definitions = new Dictionary<string, TileDefinition>();
            foreach (var item in source.tile_definitions)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || definitions.ContainsKey(item.id))
                    throw new InvalidOperationException($"비어 있거나 중복된 타일 ID입니다: {item?.id}");
                if (!Enum.TryParse(item.type, true, out TileType type) ||
                    !Enum.TryParse(item.element, true, out TowerElement element))
                    throw new InvalidOperationException($"{item.id}의 type 또는 element가 올바르지 않습니다.");
                if (!string.IsNullOrEmpty(item.build_tower_id) && !towerIds.Contains(item.build_tower_id))
                    throw new InvalidOperationException($"{item.id}가 존재하지 않는 타워를 참조합니다: {item.build_tower_id}");

                var definition = FindOrCreate<TileDefinition>(TileFolder, item.id);
                var serialized = new SerializedObject(definition);
                serialized.FindProperty("id").stringValue = item.id;
                serialized.FindProperty("displayName").stringValue = item.name;
                serialized.FindProperty("type").enumValueIndex = (int)type;
                serialized.FindProperty("element").enumValueIndex = (int)element;
                serialized.FindProperty("buildTowerDefinitionId").stringValue = item.build_tower_id ?? string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                definition.name = item.id;
                EditorUtility.SetDirty(definition);
                definitions.Add(item.id, definition);
            }

            if (source.board_layout.Length != BoardState.DefaultTileCount)
                throw new InvalidOperationException($"board_layout은 {BoardState.DefaultTileCount}칸이어야 합니다.");
            var board = AssetDatabase.LoadAssetAtPath<BoardDefinition>(BoardAssetPath);
            if (board == null)
            {
                board = ScriptableObject.CreateInstance<BoardDefinition>();
                AssetDatabase.CreateAsset(board, BoardAssetPath);
            }
            var boardSerialized = new SerializedObject(board);
            var tiles = boardSerialized.FindProperty("tiles");
            tiles.arraySize = BoardState.DefaultTileCount;
            var usedIndices = new HashSet<int>();
            foreach (var placement in source.board_layout)
            {
                if (placement.index < 0 || placement.index >= BoardState.DefaultTileCount || !usedIndices.Add(placement.index))
                    throw new InvalidOperationException($"잘못되거나 중복된 보드 인덱스입니다: {placement.index}");
                if (!definitions.TryGetValue(placement.tile_id, out var definition))
                    throw new InvalidOperationException($"존재하지 않는 타일 ID입니다: {placement.tile_id}");
                var entry = tiles.GetArrayElementAtIndex(placement.index);
                entry.FindPropertyRelative("Index").intValue = placement.index;
                entry.FindPropertyRelative("Definition").objectReferenceValue = definition;
            }
            boardSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(board);
            Debug.Log($"타일 {definitions.Count}종, 보드 {usedIndices.Count}칸, 타워 {towerIds.Count}종 임포트 완료");
        }

        private static T ReadJson<T>(string path) where T : class
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) throw new FileNotFoundException(path);
            return JsonUtility.FromJson<T>(asset.text);
        }

        private static T FindOrCreate<T>(string folder, string id) where T : ScriptableObject
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(asset);
                if (serialized.FindProperty("id")?.stringValue == id) return asset;
            }
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, $"{folder}/{id}.asset");
            return created;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var separator = path.LastIndexOf('/');
            var parent = path.Substring(0, separator);
            var folderName = path.Substring(separator + 1);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        [Serializable] private sealed class TowerDatabaseJson { public TowerJson[] tower_database; }
        [Serializable] private sealed class TowerJson { public string id; public string name; public string element; public int tier = 1; public TowerStatsJson base_stats; public EffectJson[] base_effect_ids; }
        [Serializable] private sealed class TowerStatsJson { public int damage; public int range; public int target_count = 1; public int attack_count = 1; }
        [Serializable] private sealed class UpgradeDatabaseJson { public UpgradeJson[] tower_upgrade_database; }
        [Serializable] private sealed class UpgradeJson { public string id; public string name; public string description; public string element; public int tier; public int weight = 1; public ModifierJson[] stat_modifiers; public EffectJson[] effect_ids; }
        [Serializable] private sealed class ModifierJson { public string stat; public string operation; public float value; }
        [Serializable] private sealed class EffectJson { public string id; public float value; }
        [Serializable] private sealed class TileDatabaseJson { public TileJson[] tile_definitions; public PlacementJson[] board_layout; }
        [Serializable] private sealed class TileJson { public string id; public string name; public string type; public string element; public string build_tower_id; }
        [Serializable] private sealed class PlacementJson { public int index; public string tile_id; }
        private readonly struct TowerImportResult
        {
            public TowerImportResult(HashSet<string> ids, List<TowerDefinition> definitions)
            {
                Ids = ids;
                Definitions = definitions;
            }
            public HashSet<string> Ids { get; }
            public List<TowerDefinition> Definitions { get; }
        }
    }

    public sealed class TileTowerJsonAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                if (path == TileTowerJsonImporter.TileJsonPath || path == TileTowerJsonImporter.TowerJsonPath || path == TileTowerJsonImporter.UpgradeJsonPath)
                {
                    TileTowerJsonImporter.Import();
                    return;
                }
        }
    }
}
