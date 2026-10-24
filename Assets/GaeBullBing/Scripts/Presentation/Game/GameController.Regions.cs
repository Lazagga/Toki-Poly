using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Presentation.Game
{
    public sealed partial class GameController
    {
        private IEnumerator ResolvePlayerTurnStartRoutine()
        {
            var results = Session.ResolveTurnStartEffects();
            var killed = 0;
            foreach (var result in results)
            {
                yield return PlayAttackResult(result, new HashSet<int>());
                if (result.Killed) killed++;
            }
            Session.CollectKillRewards(results);
            CommitCapturedKills(ref killed);
            boardView.RefreshTileEffects(State.Board);
            if (State.IsVictory) FinishVictory();
        }

        private void ShowOasisUpgradeChoices()
        {
            var action = Session.PendingOasisUpgrade;
            if (action == null) return;
            var tile = State.Board.Tiles[action.TileIndex];
            var definition = FindTowerDefinition(tile.HasTower ? tile.Tower.DefinitionId : tile.BuildTowerDefinitionId);
            var tier = tile.HasTower ? System.Math.Min(3, tile.Tower.UpgradeTier + 1) : 2;
            State.CurrentPhase = TurnPhase.TowerSelection;
            radialMenu.ShowUpgradeChoices(GetUpgradeChoices(definition, tier, tile.Tower?.AppliedUpgradeIds), SelectUpgrade);
        }

        // The lab uses the same action and presentation, but never advances the enemy turn.
        public IEnumerator TestLabApplyOasisChoice(TowerUpgradeDefinition upgrade)
            => ResolveOasisChoiceRoutine(upgrade, true);

        private IEnumerator ResolveOasisChoiceRoutine(TowerUpgradeDefinition upgrade, bool lab)
        {
            var action = Session.PendingOasisUpgrade;
            if (action == null) yield break;
            var index = action.TileIndex;
            var wasBuilt = State.Board.Tiles[index].HasTower;
            var tower = Session.ApplyOasisUpgrade(upgrade, towerDefinitions, towerUpgradeDefinitions);
            var definition = FindTowerDefinition(tower.DefinitionId);
            if (!wasBuilt) ApplyDiceBuildPassives(State.Board.Tiles[index], definition);
            Session.CalculateTowerStats(definition, State.Board.Tiles[index], towerUpgradeDefinitions);
            if (towerPresenter != null)
            {
                if (wasBuilt) yield return towerPresenter.PlayUpgradeAnimation(index, definition, tower.UpgradeTier);
                else yield return towerPresenter.PlayBuildAnimation(index, definition, tower.UpgradeTier);
            }
            if (lab)
            {
                State.CurrentPhase = TurnPhase.TileResolve;
                TestLabRefreshViews();
                yield break;
            }
            if (Session.PendingOasisUpgrade != null) ShowOasisUpgradeChoices();
            else yield return CompleteTileActionRoutine();
        }
    }
}
