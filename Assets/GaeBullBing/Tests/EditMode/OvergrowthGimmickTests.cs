using System;
using System.Linq;
using GaeBullBing.Core;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Dice;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using NUnit.Framework;
using UnityEngine;

namespace GaeBullBing.Tests.EditMode
{
    public sealed class OvergrowthGimmickTests
    {
        private GameState state;
        private RegionDefinition region;
        private RegionGimmickService gimmick;
        private GameSession session;

        [SetUp]
        public void Setup()
        {
            state = new GameState { Round = 1 };
            new BoardService(new System.Random(1)).Initialize(state.Board);
            region = new RegionDefinition { Id = "REGION_04", GimmickId = RegionGimmickService.OvergrowthId,
                Scope = "board", TilesPerTurn = 8, DurationTurns = 1, DisallowBuild = true, DisallowUpgrade = true,
                TriggerCondition = "stop_exactly_on_tile", RewardEffectId = "extra_turn", RewardEffectValue = 1 };
            gimmick = new RegionGimmickService(new System.Random(7));
            session = new GameSession(state, new BoardService(), new PlayerMovementService(),
                new WeightedDiceRoller(new SystemDiceRandom(new System.Random(1))), new MonsterService(), new TowerService(), new TowerCombatService());
        }
        private int[] Patches() => state.Board.Tiles.Where(t => t.HasOvergrowth).Select(t => t.Index).ToArray();

        [Test] public void SelectsEightDistinctTilesAndAppliesJsonRestrictions()
        {
            gimmick.BeginTurn(state, region);
            Assert.That(Patches().Length, Is.EqualTo(8));
            Assert.That(state.Board.Tiles.Where(t => t.HasOvergrowth).All(t => t.IsBuildRestricted && t.IsUpgradeRestricted), Is.True);
        }
        [TestCase(0, 0)] [TestCase(3, 3)] [TestCase(100, 36)]
        public void PatchCountComesFromJsonAndCannotExceedBoard(int requested, int expected)
        { region.TilesPerTurn = requested; gimmick.BeginTurn(state, region); Assert.That(Patches().Length, Is.EqualTo(expected)); }

        [Test] public void BoardScopeIncludesCornerTiles()
        { region.TilesPerTurn = 36; gimmick.BeginTurn(state, region); Assert.That(new[] {0,9,18,27}.All(i => state.Board.Tiles[i].HasOvergrowth), Is.True); }

        [Test] public void RepeatedStartInSameRoundDoesNotRerollOrUnlockNewRewards()
        {
            gimmick.BeginTurn(state, region); var patches = Patches(); state.Player.PendingExtraRolls = 2;
            gimmick.BeginTurn(state, region);
            Assert.That(Patches(), Is.EqualTo(patches)); Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(2));
            Assert.That(state.Player.AvailableExtraRolls, Is.Zero);
        }
        [Test] public void EndExpiresPatchesButPreservesFireIceFeathersAndBonusTiles()
        {
            gimmick.BeginTurn(state, region); var tile = state.Board.Tiles[Patches()[0]];
            tile.FireTurnsRemaining = 2; tile.IceTurnsRemaining = 2; tile.HasBossFeather = true; tile.IsBonusTile = true;
            gimmick.EndTurn(state);
            Assert.That(Patches(), Is.Empty); Assert.That(tile.IsBuildRestricted || tile.IsUpgradeRestricted, Is.False);
            Assert.That(tile.FireTurnsRemaining, Is.EqualTo(2)); Assert.That(tile.IceTurnsRemaining, Is.EqualTo(2));
            Assert.That(tile.HasBossFeather && tile.IsBonusTile, Is.True);
        }
        [Test] public void NewTurnRerollsExactlyConfiguredCount()
        {
            gimmick.BeginTurn(state, region); var previous = Patches(); gimmick.EndTurn(state);
            state.Round++; gimmick.BeginTurn(state, region);
            Assert.That(Patches().Length, Is.EqualTo(8)); Assert.That(Patches(), Is.Not.EquivalentTo(previous));
        }
        [Test] public void DurationAndEndIdempotenceUseTurnClockNotActionCount()
        {
            region.DurationTurns = 2; gimmick.BeginTurn(state, region); var tile = state.Board.Tiles[Patches()[0]];
            gimmick.EndTurn(state); gimmick.EndTurn(state);
            Assert.That(tile.OvergrowthTurnsRemaining, Is.EqualTo(1));
            state.Round++; region.TilesPerTurn = 0; gimmick.BeginTurn(state, region); gimmick.EndTurn(state);
            Assert.That(tile.HasOvergrowth, Is.False);
        }
        [Test] public void ActualArrivalQueuesRewardOnlyForNextTurn()
        {
            gimmick.BeginTurn(state, region); var tile = Patches()[0];
            Assert.That(gimmick.ResolveArrival(state, region, tile), Is.EqualTo(1));
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(1));
            Assert.That(gimmick.TryConsumeExtraRoll(state), Is.False);
            gimmick.EndTurn(state); state.Round++; gimmick.BeginTurn(state, region);
            Assert.That(state.Player.PendingExtraRolls, Is.Zero); Assert.That(state.Player.AvailableExtraRolls, Is.EqualTo(1));
            Assert.That(gimmick.TryConsumeExtraRoll(state), Is.True); Assert.That(gimmick.TryConsumeExtraRoll(state), Is.False);
        }
        [Test] public void SameArrivalCallbackCannotAwardTwiceButLaterArrivalsStack()
        {
            region.RewardEffectValue = 2; gimmick.BeginTurn(state, region); var tile = Patches()[0];
            gimmick.ResolveArrival(state, region, tile); gimmick.ResolveArrival(state, region, tile);
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(2));
            state.Player.ArrivalSerial++; gimmick.ResolveArrival(state, region, tile);
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(4));
        }
        [Test] public void NonPatchAndExpiredPatchCannotReward()
        {
            gimmick.BeginTurn(state, region);
            Assert.That(gimmick.ResolveArrival(state, region, state.Board.Tiles.First(t => !t.HasOvergrowth).Index), Is.Zero);
            var patch = Patches()[0]; gimmick.EndTurn(state);
            Assert.That(gimmick.ResolveArrival(state, region, patch), Is.Zero);
        }
        [Test] public void ConsumingAvailableBonusNeverConsumesNewNextTurnRewards()
        {
            state.Player.AvailableExtraRolls = 1; state.Player.PendingExtraRolls = 3;
            Assert.That(gimmick.TryConsumeExtraRoll(state), Is.True);
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(3)); Assert.That(gimmick.TryConsumeExtraRoll(state), Is.False);
        }
        [Test] public void RegionSwitchClearsPatchAndRewardState()
        {
            session.SetRegion(region); session.ResolveTurnStartEffects();
            state.Player.AvailableExtraRolls = 2; state.Player.PendingExtraRolls = 3;
            session.SetRegion(new RegionDefinition { Id = "REGION_01" });
            Assert.That(Patches(), Is.Empty); Assert.That(state.Player.AvailableExtraRolls + state.Player.PendingExtraRolls, Is.Zero);
        }
        [Test] public void BuildRestrictionIsEnforcedInCoreWithoutChangingPhaseOrTower()
        {
            region.TilesPerTurn = 36; session.SetRegion(region); session.ResolveTurnStartEffects();
            state.CurrentPhase = TurnPhase.TileAction;
            Assert.Throws<InvalidOperationException>(() => session.BuildTower(5, "TOW_01"));
            Assert.That(state.Board.Tiles[5].Tower, Is.Null); Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.TileAction));
        }
        [Test] public void UpgradeRestrictionIsEnforcedBeforeApplyingStatsOrPhase()
        {
            var tower = session.BuildTower(5, "TOW_01");
            region.TilesPerTurn = 36; session.SetRegion(region); session.ResolveTurnStartEffects(); state.CurrentPhase = TurnPhase.TileAction;
            var upgrade = ScriptableObject.CreateInstance<TowerUpgradeDefinition>();
            try
            {
                Assert.Throws<InvalidOperationException>(() => session.UpgradeTower(5, upgrade));
                Assert.That(tower.AppliedUpgradeIds, Is.Empty); Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.TileAction));
            }
            finally { UnityEngine.Object.DestroyImmediate(upgrade); }
        }
        [Test] public void RestrictionFlagsCanIndependentlyBeDisabledByJson()
        {
            region.TilesPerTurn = 36; region.DisallowBuild = false; region.DisallowUpgrade = false;
            session.SetRegion(region); session.ResolveTurnStartEffects();
            Assert.That(state.Board.Tiles[5].HasOvergrowth, Is.True);
            Assert.That(state.Board.Tiles[5].IsUpgradeRestricted, Is.False);
            Assert.DoesNotThrow(() => session.BuildTower(5, "TOW_01"));
        }
        [Test] public void SessionArrivalAndTurnEventsShareBonusClock()
        {
            region.TilesPerTurn = 36; session.SetRegion(region); session.ResolveTurnStartEffects();
            session.TeleportPlayer(5); session.ResolvePlayerArrivalEffects(5); session.ResolvePlayerArrivalEffects(5);
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(1));
            session.ResolveMonsterTurnEndEffects(); session.CompleteRound(); session.ResolveTurnStartEffects();
            Assert.That(session.TryConsumeRegionExtraRoll(), Is.True);
            Assert.That(session.TryConsumeRegionExtraRoll(), Is.False);
        }
        [Test] public void PassingPatchViaPathEffectsDoesNotAwardBonus()
        {
            region.TilesPerTurn = 36; session.SetRegion(region); session.ResolveTurnStartEffects();
            session.ResolvePlayerPathEffects(3, 5);
            Assert.That(state.Player.PendingExtraRolls, Is.Zero);
        }
        [Test] public void NewGameClearsAllRegionRewardsAndPatches()
        {
            session.SetRegion(region); session.ResolveTurnStartEffects(); state.Player.PendingExtraRolls = 3;
            session.StartNewGame();
            Assert.That(Patches(), Is.Empty); Assert.That(state.Player.AvailableExtraRolls + state.Player.PendingExtraRolls, Is.Zero);
        }
    }
}
