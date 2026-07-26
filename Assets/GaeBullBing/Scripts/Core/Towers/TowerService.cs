using System;
using GaeBullBing.Core.Board;

namespace GaeBullBing.Core.Towers
{
    public sealed class TowerService
    {
        private int nextInstanceId = 1;

        public TowerState Build(TileState tile, string definitionId)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (tile.HasTower)
                throw new InvalidOperationException("The tile already has a tower.");
            if (!string.Equals(tile.BuildTowerDefinitionId, definitionId, StringComparison.Ordinal))
                throw new InvalidOperationException("This tower cannot be built on the selected tile.");

            tile.Tower = new TowerState
            {
                InstanceId = nextInstanceId++,
                DefinitionId = definitionId
            };
            return tile.Tower;
        }

        public TowerState Upgrade(TileState tile, string upgradeDefinitionId, int upgradeTier)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (!tile.HasTower)
                throw new InvalidOperationException("The tile does not have a tower to upgrade.");
            if (string.IsNullOrWhiteSpace(upgradeDefinitionId))
                throw new ArgumentException("An upgrade definition id is required.", nameof(upgradeDefinitionId));
            if (upgradeTier != tile.Tower.UpgradeTier + 1)
                throw new InvalidOperationException("The upgrade tier must be the tower's next tier.");
            tile.Tower.AppliedUpgradeIds.Add(upgradeDefinitionId);
            tile.Tower.UpgradeTier = upgradeTier;
            return tile.Tower;
        }

        public TowerState ApplyBonusTier3Upgrade(
            TileState tile,
            string upgradeDefinitionId,
            int upgradeTier)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (!tile.HasTower)
                throw new InvalidOperationException("The tile does not have a tower to upgrade.");
            if (!tile.IsBonusTile)
                throw new InvalidOperationException("An additional tier 3 upgrade requires a bonus tile.");
            if (tile.Tower.UpgradeTier != 3 || upgradeTier != 3)
                throw new InvalidOperationException("The additional bonus upgrade must be tier 3.");
            if (tile.Tower.BonusTier3UpgradeClaimed)
                throw new InvalidOperationException("The bonus tier 3 upgrade has already been claimed.");
            if (string.IsNullOrWhiteSpace(upgradeDefinitionId))
                throw new ArgumentException("An upgrade definition id is required.", nameof(upgradeDefinitionId));
            if (tile.Tower.AppliedUpgradeIds.Contains(upgradeDefinitionId))
                throw new InvalidOperationException("The selected upgrade is already applied.");

            tile.Tower.AppliedUpgradeIds.Add(upgradeDefinitionId);
            tile.Tower.BonusTier3UpgradeClaimed = true;
            return tile.Tower;
        }
    }
}
