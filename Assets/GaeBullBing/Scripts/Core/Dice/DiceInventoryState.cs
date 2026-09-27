using System;
using System.Collections.Generic;
using GaeBullBing.Core.Data;
using UnityEngine;

namespace GaeBullBing.Core.Dice
{
    [Serializable]
    public sealed class DiceInventoryState
    {
        public const int EquippedCount = 2;
        public const int Capacity = 4;

        public List<DiceState> Dice { get; } = new();

        public bool QueueEquip(IList<DiceState> equipped, int slotIndex, int inventoryIndex)
        {
            if (equipped == null || slotIndex < 0 || slotIndex >= EquippedCount ||
                inventoryIndex < 0 || inventoryIndex >= Dice.Count)
                return false;
            var selected = Dice[inventoryIndex];
            for (var slot = 0; slot < equipped.Count; slot++)
                if (slot != slotIndex && ReferenceEquals(equipped[slot], selected))
                    return false;
            equipped[slotIndex] = selected;
            return true;
        }

        public bool TryStoreReward(DiceState reward)
        {
            if (reward == null || Dice.Count >= Capacity) return false;
            Dice.Add(reward);
            return true;
        }

        public bool Replace(int index, DiceState reward, IList<DiceState> equipped)
        {
            if (reward == null || index < 0 || index >= Dice.Count) return false;
            var replaced = Dice[index];
            Dice[index] = reward;
            for (var slot = 0; slot < EquippedCount && slot < equipped.Count; slot++)
                if (ReferenceEquals(equipped[slot], replaced)) equipped[slot] = reward;
            return true;
        }

        public void ResetToDefaults()
        {
            Dice.Clear();
            var white = DiceCatalog.GetById("DICE_WHITE");
            var black = DiceCatalog.GetById("DICE_BLACK");
            if (white == null || black == null)
                throw new InvalidOperationException(
                    "DiceDatabase에 DICE_WHITE와 DICE_BLACK이 모두 필요합니다.");
            Dice.Add(white);
            Dice.Add(black);
        }
    }

    public static class DiceCatalog
    {
        private const string RuntimeDatabaseResourcePath = "GaeBullBing/DiceDatabase";
        private static DiceDefinition[] definitions;
        private static DiceDatabaseDefinition runtimeDatabase;

        public static void ClearCache()
        {
            definitions = null;
            runtimeDatabase = null;
        }

        public static bool TryInitialize(out string error)
        {
            ClearCache();
            var loaded = GetDefinitions();
            if (runtimeDatabase == null)
            {
                error = "Resources/GaeBullBing/DiceDatabase.asset이 없습니다. Dice.json을 임포트하세요.";
                return false;
            }
            if (loaded.Length == 0)
            {
                error = "DiceDatabase가 비어 있습니다. Dice.json 내용을 확인하세요.";
                return false;
            }
            if (GetById("DICE_WHITE") == null || GetById("DICE_BLACK") == null)
            {
                error = "DiceDatabase에 DICE_WHITE와 DICE_BLACK이 모두 필요합니다.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static DiceState GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var definition in GetDefinitions())
                if (definition != null && definition.Id == id)
                    return CreateState(definition);
            return null;
        }

        public static DiceState GetReward(int index) =>
            GetRewardForRoll(index, UnityEngine.Random.value,
                UnityEngine.Random.Range(0, int.MaxValue));

        public static DiceState GetRewardForRoll(
            int unusedFallbackIndex,
            float gradeRoll,
            int withinGradeIndex)
        {
            var rewards = new List<DiceDefinition>();
            foreach (var definition in GetDefinitions())
                if (definition != null && definition.Id != "DICE_WHITE" &&
                    definition.Id != "DICE_BLACK")
                    rewards.Add(definition);
            if (rewards.Count == 0)
                throw new InvalidOperationException(
                    "DiceDatabase에 보상으로 사용할 주사위가 없습니다.");

            var availableWeights = new float[Enum.GetValues(typeof(DiceGrade)).Length];
            var totalWeight = 0f;
            foreach (var reward in rewards)
            {
                var gradeIndex = (int)reward.Grade;
                if (availableWeights[gradeIndex] > 0f) continue;
                var weight = Math.Max(0f, runtimeDatabase.GetGradeWeight(reward.Grade));
                availableWeights[gradeIndex] = weight;
                totalWeight += weight;
            }
            if (totalWeight <= 0f)
                throw new InvalidOperationException(
                    "DiceDatabase의 사용 가능한 등급 weight 합계가 0입니다.");

            var threshold = Math.Max(0f, Math.Min(.999999f, gradeRoll)) * totalWeight;
            var selectedGrade = DiceGrade.Common;
            for (var gradeIndex = 0; gradeIndex < availableWeights.Length; gradeIndex++)
            {
                threshold -= availableWeights[gradeIndex];
                if (threshold >= 0f) continue;
                selectedGrade = (DiceGrade)gradeIndex;
                break;
            }
            var gradeRewards = rewards.FindAll(definition => definition.Grade == selectedGrade);
            var selectedIndex = (withinGradeIndex & int.MaxValue) % gradeRewards.Count;
            return CreateState(gradeRewards[selectedIndex]);
        }

        private static DiceDefinition[] GetDefinitions()
        {
            if (definitions != null) return definitions;
            runtimeDatabase = Resources.Load<DiceDatabaseDefinition>(RuntimeDatabaseResourcePath);
            definitions = runtimeDatabase == null || runtimeDatabase.Dice == null
                ? Array.Empty<DiceDefinition>()
                : runtimeDatabase.Dice;
            return definitions;
        }

        private static DiceState CreateState(DiceDefinition definition)
        {
            if (definition == null || definition.Faces == null || definition.Faces.Length == 0)
                return null;
            var weights = new int[definition.Faces.Length];
            for (var index = 0; index < weights.Length; index++) weights[index] = 1;
            return new DiceState(
                definition.Id,
                definition.DisplayName,
                definition.Faces,
                weights,
                definition.Color.r,
                definition.Color.g,
                definition.Color.b,
                definition.Description,
                definition.Effects,
                definition.Grade);
        }
    }
}
