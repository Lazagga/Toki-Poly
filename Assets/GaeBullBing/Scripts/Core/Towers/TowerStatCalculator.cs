using System;
using System.Collections.Generic;
using System.Globalization;
using GaeBullBing.Core.Data;

namespace GaeBullBing.Core.Towers
{
    public readonly struct TowerStatCalculation
    {
        public TowerStatCalculation(TowerCombatStats combatStats, string damageFormula)
        {
            CombatStats = combatStats;
            DamageFormula = damageFormula;
        }

        public TowerCombatStats CombatStats { get; }
        public string DamageFormula { get; }
    }

    public static class TowerStatCalculator
    {
        public static TowerStatCalculation Calculate(
            TowerDefinition definition,
            TowerState tower,
            IReadOnlyList<TowerUpgradeDefinition> upgrades,
            float damageRateBonus,
            int postMultiplierDamageBonus,
            int bonusAttackCount = 0)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (tower == null) throw new ArgumentNullException(nameof(tower));

            var damage = new StatParts();
            var range = new StatParts();
            var targets = new StatParts();
            var attacks = new StatParts();

            foreach (var appliedId in tower.AppliedUpgradeIds)
            {
                var upgrade = FindUpgrade(upgrades, appliedId);
                if (upgrade == null) continue;
                foreach (var modifier in upgrade.StatModifiers ?? Array.Empty<TowerStatModifier>())
                {
                    switch (modifier.Stat?.ToLowerInvariant())
                    {
                        case "damage": damage.Apply(modifier); break;
                        case "range": range.Apply(modifier); break;
                        case "target_count": targets.Apply(modifier); break;
                        case "attack_count": attacks.Apply(modifier); break;
                    }
                }
            }

            // 공격력 배율 업그레이드는 타일/완주 배율과 같은 배율 묶음에서 합산한다.
            // 예: 3배 업그레이드 + 50% 맵 보너스 = 3.5배.
            var combinedDamageMultiplier = Math.Max(
                0f,
                damage.AdditiveMultiplierFactor + damageRateBonus);
            var resolvedDamage = damage.Set ??
                (definition.Damage + damage.Add) * combinedDamageMultiplier +
                postMultiplierDamageBonus;
            var resolvedRange = range.Resolve(definition.Range);
            var resolvedTargets = targets.Resolve(definition.TargetCount);
            var resolvedAttacks = attacks.Resolve(definition.AttackCount);
            var stats = new TowerCombatStats(
                Math.Max(0, (int)Math.Round(resolvedDamage)),
                Math.Max(0, (int)Math.Round(resolvedRange)),
                Math.Max(1, (int)Math.Round(resolvedTargets)),
                Math.Max(1, (int)Math.Round(resolvedAttacks) + bonusAttackCount));

            var formula = damage.Set.HasValue
                ? Format(damage.Set.Value)
                : $"({Format(definition.Damage)} + {Format(damage.Add)}) * " +
                  $"{Format(combinedDamageMultiplier * 100f)}% + " +
                  $"{postMultiplierDamageBonus}";
            return new TowerStatCalculation(stats, formula);
        }

        private static TowerUpgradeDefinition FindUpgrade(
            IReadOnlyList<TowerUpgradeDefinition> upgrades,
            string id)
        {
            if (upgrades == null) return null;
            for (var index = 0; index < upgrades.Count; index++)
                if (upgrades[index] != null &&
                    string.Equals(upgrades[index].Id, id, StringComparison.Ordinal))
                    return upgrades[index];
            return null;
        }

        private static string Format(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private struct StatParts
        {
            public float Add;
            public float Multiply;
            public float MultiplySum;
            public int MultiplyCount;
            public float? Set;
            public float Factor => Multiply == 0f ? 1f : Multiply;
            public float AdditiveMultiplierFactor => MultiplyCount == 0 ? 1f : MultiplySum;

            public void Apply(TowerStatModifier modifier)
            {
                if (Multiply == 0f) Multiply = 1f;
                if (string.Equals(modifier.Operation, "set", StringComparison.OrdinalIgnoreCase))
                    Set = modifier.Value;
                else if (string.Equals(modifier.Operation, "multiply", StringComparison.OrdinalIgnoreCase))
                {
                    Multiply *= modifier.Value;
                    MultiplySum += modifier.Value;
                    MultiplyCount++;
                }
                else
                    Add += modifier.Value;
            }

            public float Resolve(float baseValue)
            {
                return Set ?? (baseValue + Add) * Factor;
            }
        }
    }
}
