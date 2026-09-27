using System;
using GaeBullBing.Core.Data;

namespace GaeBullBing.Core.Dice
{
    [Serializable]
    public sealed class DiceState
    {
        public DiceState()
            : this("DICE_WHITE", "흰 주사위", new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 1, 1, 1, 1, 1, 1 }, 0.96f, 0.96f, 0.93f, "기본 주사위", "", 0f)
        {
        }

        public DiceState(int[] faces, int[] weights)
            : this("DICE_CUSTOM", "주사위", faces, weights, 0.96f, 0.96f, 0.93f, "", "", 0f)
        {
        }

        public DiceState(string id, string displayName, int[] faces, int[] weights,
            float red, float green, float blue, string passiveDescription,
            string passiveId, float passiveValue)
            : this(id, displayName, faces, weights, red, green, blue,
                passiveDescription, passiveId, passiveValue, DiceGrade.Common)
        {
        }

        public DiceState(string id, string displayName, int[] faces, int[] weights,
            float red, float green, float blue, string passiveDescription,
            string passiveId, float passiveValue, DiceGrade grade)
            : this(id, displayName, faces, weights, red, green, blue,
                passiveDescription, CreateLegacyEffects(passiveId, passiveValue), grade)
        {
        }

        public DiceState(string id, string displayName, int[] faces, int[] weights,
            float red, float green, float blue, string passiveDescription,
            TowerUpgradeEffect[] effects, DiceGrade grade)
        {
            if (faces == null || weights == null)
                throw new ArgumentNullException(faces == null ? nameof(faces) : nameof(weights));
            if (faces.Length == 0 || faces.Length != weights.Length)
                throw new ArgumentException("Faces and weights must have the same non-zero length.");

            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Faces = (int[])faces.Clone();
            Weights = (int[])weights.Clone();
            Red = red;
            Green = green;
            Blue = blue;
            PassiveDescription = passiveDescription ?? string.Empty;
            Effects = effects == null ? Array.Empty<TowerUpgradeEffect>() : (TowerUpgradeEffect[])effects.Clone();
            PassiveId = Effects.Length > 0 ? Effects[0].Id ?? string.Empty : string.Empty;
            PassiveValue = Effects.Length > 0 ? Effects[0].Value : 0f;
            Grade = grade;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int[] Faces { get; }
        public int[] Weights { get; }
        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
        public string PassiveDescription { get; }
        public string PassiveId { get; }
        public float PassiveValue { get; }
        public TowerUpgradeEffect[] Effects { get; }
        public DiceGrade Grade { get; }
        public bool UsesBlackPips => Id == "DICE_WHITE";

        public DiceState Clone() => new DiceState(Id, DisplayName, Faces, Weights,
            Red, Green, Blue, PassiveDescription, Effects, Grade);

        public bool HasEffect(string id) => GetEffectValue(id) != 0f;

        public float GetEffectValue(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return 0f;
            var value = 0f;
            foreach (var effect in Effects)
                if (string.Equals(effect.Id, id, StringComparison.Ordinal))
                    value += effect.Value;
            return value;
        }

        private static TowerUpgradeEffect[] CreateLegacyEffects(string id, float value) =>
            string.IsNullOrWhiteSpace(id)
                ? Array.Empty<TowerUpgradeEffect>()
                : new[] { new TowerUpgradeEffect { Id = id, Value = value } };

        public void SetWeight(int faceIndex, int weight)
        {
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight));
            Weights[faceIndex] = weight;
        }

        public static DiceState CreateBlack() => new DiceState(
            "DICE_BLACK", "검은 주사위", new[] { 1, 2, 3, 4, 5, 6 },
            new[] { 1, 1, 1, 1, 1, 1 }, 0.035f, 0.035f, 0.045f,
            "기본 주사위", "", 0f);
    }
}
