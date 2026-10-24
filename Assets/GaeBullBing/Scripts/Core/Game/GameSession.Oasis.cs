using System;
using System.Collections.Generic;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Game
{
    public sealed class OasisUpgradeAction
    {
        public int TileIndex { get; internal set; }
        public int RemainingChoices { get; internal set; }
        public bool BuildsTower { get; internal set; }
        internal int Round;
        internal string RegionId;
    }

    public sealed partial class GameSession
    {
        public OasisUpgradeAction PendingOasisUpgrade { get; private set; }
        public bool IsOasisTile(int tileIndex) => RegionGimmickService.IsOasisTile(currentRegion, tileIndex);
        public void CancelOasisUpgrade() => PendingOasisUpgrade = null;

        /// <summary>Full eligible pool; presentation may sample three choices from this pool.</summary>
        public IReadOnlyList<TowerUpgradeDefinition> GetOasisUpgradeOptions(int tileIndex,
            IReadOnlyList<TowerDefinition> definitions, IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            var options = new List<TowerUpgradeDefinition>();
            if (currentRegion?.GimmickId != RegionGimmickService.OasisId ||
                tileIndex < 0 || tileIndex >= State.Board.TileCount) return options;
            var tile = State.Board.Tiles[tileIndex];
            if (tile.IsUpgradeRestricted || !tile.HasTower && (!tile.CanBuildTower || tile.IsBuildRestricted)) return options;
            var definition = FindOasisTowerDefinition(tileIndex, definitions);
            if (definition == null) return options;
            var targetTier = tile.HasTower ? Math.Min(3, tile.Tower.UpgradeTier + 1) : 2;
            foreach (var upgrade in upgrades)
                if (upgrade != null && upgrade.Weight > 0 && upgrade.Element == definition.Element &&
                    upgrade.Tier == targetTier && (!tile.HasTower || !tile.Tower.AppliedUpgradeIds.Contains(upgrade.Id)))
                    options.Add(upgrade);
            return options;
        }

        public void BeginOasisUpgrade(int tileIndex, IReadOnlyList<TowerDefinition> definitions,
            IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            if (PendingOasisUpgrade != null) throw new InvalidOperationException("진행 중인 오아시스 강화를 먼저 완료하세요.");
            if (!IsOasisTile(State.Player.CurrentTileIndex)) throw new InvalidOperationException("오아시스 타일에 도착해야 사용할 수 있습니다.");
            if (GetOasisUpgradeOptions(tileIndex, definitions, upgrades).Count == 0)
                throw new InvalidOperationException("이 타일에는 적용 가능한 오아시스 강화가 없습니다.");
            var buildsTower = !State.Board.Tiles[tileIndex].HasTower;
            var count = buildsTower ? 1 : (int)Math.Round(currentRegion.OverrideEffectValue);
            if (count < 1) throw new InvalidOperationException("오아시스의 override_effect_value는 1 이상이어야 합니다.");
            PendingOasisUpgrade = new OasisUpgradeAction { TileIndex = tileIndex, RemainingChoices = count,
                BuildsTower = buildsTower, Round = State.Round, RegionId = State.CurrentRegionId };
            State.CombatTrace?.Add($"오아시스 대상: 타일 {tileIndex}, {(buildsTower ? "즉시 2티어 건설" : $"강화 {count}회")}");
        }

        public TowerState ApplyOasisUpgrade(TowerUpgradeDefinition selected, IReadOnlyList<TowerDefinition> definitions,
            IReadOnlyList<TowerUpgradeDefinition> upgrades)
        {
            var action = PendingOasisUpgrade;
            if (action == null || action.Round != State.Round || action.RegionId != State.CurrentRegionId)
                throw new InvalidOperationException("진행 중인 오아시스 강화가 없습니다.");
            TowerUpgradeDefinition canonical = null;
            foreach (var option in GetOasisUpgradeOptions(action.TileIndex, definitions, upgrades))
                if (selected != null && option.Id == selected.Id) { canonical = option; break; }
            if (canonical == null) throw new InvalidOperationException("선택한 강화는 현재 오아시스 대상에 적용할 수 없습니다.");
            var tile = State.Board.Tiles[action.TileIndex];
            if (!tile.HasTower) BuildTower(action.TileIndex, FindOasisTowerDefinition(action.TileIndex, definitions));
            var tower = ApplyTowerUpgrade(action.TileIndex, canonical, false);
            action.RemainingChoices--;
            State.CombatTrace?.Add($"오아시스 강화: 타일 {action.TileIndex}, {canonical.Id}, 남은 선택 {action.RemainingChoices}회", tower.InstanceId);
            if (action.RemainingChoices <= 0) CancelOasisUpgrade();
            else if (GetOasisUpgradeOptions(action.TileIndex, definitions, upgrades).Count == 0)
            {
                State.CombatTrace?.Add("오아시스: 미선택 강화가 없어 남은 선택을 종료합니다.", tower.InstanceId);
                CancelOasisUpgrade();
            }
            return tower;
        }

        private TowerDefinition FindOasisTowerDefinition(int tileIndex, IReadOnlyList<TowerDefinition> definitions)
        {
            var tile = State.Board.Tiles[tileIndex];
            var id = tile.HasTower ? tile.Tower.DefinitionId : tile.BuildTowerDefinitionId;
            foreach (var definition in definitions) if (definition != null && definition.Id == id) return definition;
            return null;
        }
    }
}
