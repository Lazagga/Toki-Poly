using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public sealed class RegionGameplayTests
    {
        private GameState state;
        private GameSession session;
        private RegionGimmickService gimmick;
        private RegionDefinition snow, oasis;
        private TowerDefinition[] towers;
        private TowerUpgradeDefinition[] upgrades;
        private readonly List<UnityEngine.Object> owned = new();

        [SetUp] public void Setup()
        {
            state = new GameState { Round = 1 };
            new BoardService(new System.Random(1)).Initialize(state.Board);
            session = new GameSession(state, new BoardService(), new PlayerMovementService(),
                new WeightedDiceRoller(new SystemDiceRandom(new System.Random(1))), new MonsterService(), new TowerService(), new TowerCombatService());
            gimmick = new RegionGimmickService(new System.Random(7));
            snow = new RegionDefinition { Id = "snow", GimmickId = RegionGimmickService.IceSheetId,
                Scope = "per_line", AppliedEffectId = TowerEffectCatalog.TileFreeze, TilesPerTurn = 1 };
            oasis = new RegionDefinition { Id = "desert", GimmickId = RegionGimmickService.OasisId,
                TargetTileIndices = new[] { 0, 18 }, OverrideEffectId = "oasis_extra_upgrade", OverrideEffectValue = 2 };
            towers = new[] { Asset<TowerDefinition>("TOW_01", 1, TowerElement.Fire) };
            upgrades = new[] { Upgrade("t2", 2), Upgrade("t3a", 3), Upgrade("t3b", 3), Upgrade("t3c", 3), Upgrade("t3d", 3) };
            session.SetRegion(oasis);
        }
        [TearDown] public void Cleanup() { foreach (var asset in owned) UnityEngine.Object.DestroyImmediate(asset); owned.Clear(); }
        private T Asset<T>(string id, int tier, TowerElement element) where T : ScriptableObject
        {
            var value = ScriptableObject.CreateInstance<T>(); owned.Add(value);
            Set(value, "id", id); Set(value, "tier", tier); Set(value, "element", element); return value;
        }
        private static void Set(object value, string field, object content) =>
            value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, content);
        private TowerUpgradeDefinition Upgrade(string id, int tier) => Asset<TowerUpgradeDefinition>(id, tier, TowerElement.Fire);
        private int[] Ice() => state.Board.Tiles.Where(t => t.IceTurnsRemaining > 0).Select(t => t.Index).ToArray();
        private void Begin(int index = 5) => session.BeginOasisUpgrade(index, towers, upgrades);
        private void Apply(int upgrade) => session.ApplyOasisUpgrade(upgrades[upgrade], towers, upgrades);

        [TestCase(1, 4)] [TestCase(3, 12)] [TestCase(100, 32)] [TestCase(0, 0)]
        public void SnowGeneratesConfiguredCountForEveryLine(int count, int expected)
        {
            snow.TilesPerTurn = count; gimmick.BeginTurn(state, snow);
            Assert.That(Ice().Length, Is.EqualTo(expected));
            foreach (var line in Enumerable.Range(0, 4))
                Assert.That(Ice().Count(i => MonsterService.GetLine(i) == line), Is.EqualTo(expected / 4));
            Assert.That(Ice().Any(i => i % 9 == 0), Is.False);
            Assert.That(Ice().Distinct().Count(), Is.EqualTo(expected));
        }
        [Test] public void SnowRepeatedStartInSameTurnDoesNotGenerateMoreFields()
        { gimmick.BeginTurn(state, snow); var before = Ice(); gimmick.BeginTurn(state, snow); Assert.That(Ice(), Is.EqualTo(before)); }
        [Test] public void SnowIsAvailableThroughSessionWithoutPresentation()
        { session.SetRegion(snow); session.ResolveTurnStartEffects(); Assert.That(Ice().Length, Is.EqualTo(4)); }
        [Test] public void SnowExpiresAtEndOfCreationTurnAndIsRecreatedNextTurn()
        {
            snow.TilesPerTurn = 100; session.SetRegion(snow); session.ResolveTurnStartEffects();
            Assert.That(state.Board.Tiles[5].IceTurnsRemaining, Is.EqualTo(1));
            session.ResolveMonsterTurnEndEffects(); Assert.That(Ice(), Is.Empty);
            session.CompleteRound(); session.ResolveTurnStartEffects();
            Assert.That(state.Board.Tiles[5].IceTurnsRemaining, Is.EqualTo(1));
        }
        [Test] public void SnowDoesNotShortenOrExtendExistingTowerIce()
        {
            state.Board.Tiles[5].IceTurnsRemaining = TileState.OneTurnEffectDuration;
            snow.TilesPerTurn = 100; session.SetRegion(snow); session.ResolveTurnStartEffects();
            Assert.That(state.Board.Tiles[5].IceTurnsRemaining, Is.EqualTo(TileState.OneTurnEffectDuration));
            session.ResolveMonsterTurnEndEffects(); Assert.That(state.Board.Tiles[5].IceTurnsRemaining, Is.EqualTo(1));
            session.CompleteRound(); snow.TilesPerTurn = 0; session.ResolveTurnStartEffects();
            Assert.That(Ice(), Is.EqualTo(new[] { 5 }));
        }
        [Test] public void TowerCanRefreshSnowUsingNormalTowerDuration()
        {
            snow.TilesPerTurn = 100; session.SetRegion(snow); session.ResolveTurnStartEffects();
            session.PlaceIceField(5);
            Assert.That(state.Board.Tiles[5].IceTurnsRemaining, Is.EqualTo(TileState.OneTurnEffectDuration));
            session.ResolveMonsterTurnEndEffects();
            Assert.That(Ice(), Is.EqualTo(new[] { 5 }));
        }
        [Test] public void ManualTurnClockEndsBeforeAdvancingAndRunsMapGimmickOnNextStart()
        {
            session.SetRegion(snow);
            Assert.That(session.CanBeginManualTurn, Is.True); Assert.That(session.CanEndManualTurn, Is.False);
            session.BeginManualTurn(); Assert.That(Ice().Length, Is.EqualTo(4));
            Assert.That(session.CanBeginManualTurn, Is.False); Assert.That(session.CanEndManualTurn, Is.True);
            session.EndManualTurn(); Assert.That(Ice(), Is.Empty); Assert.That(state.Round, Is.EqualTo(1));
            Assert.That(session.CanBeginManualTurn, Is.True); Assert.That(session.CanEndManualTurn, Is.False);
            session.BeginManualTurn(); Assert.That(state.Round, Is.EqualTo(2)); Assert.That(Ice().Length, Is.EqualTo(4));
            session.EndManualTurn(); session.BeginManualTurn();
            Assert.That(state.Round, Is.EqualTo(3)); Assert.That(Ice().Length, Is.EqualTo(4));
        }
        [Test] public void RepeatedManualStartCannotRerollOrActivatePendingRewardsEarly()
        {
            session.SetRegion(snow); session.BeginManualTurn(); var before = Ice(); state.Player.PendingExtraRolls = 2;
            Assert.Throws<InvalidOperationException>(() => session.BeginManualTurn());
            session.ResolveTurnStartEffects();
            Assert.That(Ice(), Is.EqualTo(before)); Assert.That(state.Round, Is.EqualTo(1));
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(2)); Assert.That(state.Player.AvailableExtraRolls, Is.Zero);
        }
        [Test] public void RepeatedManualOrCoreEndCannotReduceFieldsTwice()
        {
            session.BeginManualTurn(); state.Board.Tiles[5].FireTurnsRemaining = 2;
            session.EndManualTurn();
            Assert.Throws<InvalidOperationException>(() => session.EndManualTurn());
            session.ResolveMonsterTurnEndEffects();
            Assert.That(state.Board.Tiles[5].FireTurnsRemaining, Is.EqualTo(1)); Assert.That(state.Round, Is.EqualTo(1));
        }
        [Test] public void EndingBeforeManualStartIsRejectedWithoutMutation()
        {
            state.Board.Tiles[5].FireTurnsRemaining = 2;
            Assert.Throws<InvalidOperationException>(() => session.EndManualTurn());
            Assert.That(state.Board.Tiles[5].FireTurnsRemaining, Is.EqualTo(2)); Assert.That(state.Round, Is.EqualTo(1));
        }
        [Test] public void ManualSwampCycleExpiresRestrictionsThenUnlocksNextTurnReward()
        {
            var swamp = new RegionDefinition { Id = "swamp", GimmickId = RegionGimmickService.OvergrowthId,
                Scope = "board", TilesPerTurn = 36, DurationTurns = 1, DisallowBuild = true, DisallowUpgrade = true,
                TriggerCondition = "stop_exactly_on_tile", RewardEffectId = "extra_turn", RewardEffectValue = 1 };
            session.SetRegion(swamp); session.BeginManualTurn();
            session.TeleportPlayer(5); session.ResolveRegionArrivalEffects(5);
            session.EndManualTurn();
            Assert.That(state.Board.Tiles.Any(t => t.HasOvergrowth), Is.False);
            Assert.That(state.Player.PendingExtraRolls, Is.EqualTo(1)); Assert.That(state.Player.AvailableExtraRolls, Is.Zero);
            session.BeginManualTurn();
            Assert.That(state.Board.Tiles.Count(t => t.HasOvergrowth), Is.EqualTo(36));
            Assert.That(state.Player.PendingExtraRolls, Is.Zero); Assert.That(state.Player.AvailableExtraRolls, Is.EqualTo(1));
        }
        [TestCase(TurnPhase.Victory)] [TestCase(TurnPhase.Defeat)]
        public void ManualClockCannotRestartFinishedGame(TurnPhase phase)
        {
            state.CurrentPhase = phase;
            Assert.That(session.CanBeginManualTurn || session.CanEndManualTurn, Is.False);
            Assert.Throws<InvalidOperationException>(() => session.BeginManualTurn());
            Assert.Throws<InvalidOperationException>(() => session.EndManualTurn());
        }
        [Test] public void SnowReusesFireCancellationInsteadOfKeepingBothFields()
        {
            foreach (var tile in state.Board.Tiles) if (tile.Index % 9 != 0) tile.FireTurnsRemaining = 2;
            var monster = new MonsterState { InstanceId = 1, CurrentTileIndex = 5, CurrentHealth = 100, MaxHealth = 100 };
            state.Monsters.Add(monster); snow.TilesPerTurn = 100;
            var results = gimmick.BeginTurn(state, snow);
            Assert.That(state.Board.Tiles[5].FireTurnsRemaining + state.Board.Tiles[5].IceTurnsRemaining, Is.Zero);
            Assert.That(monster.CurrentHealth, Is.LessThan(100)); Assert.That(results.Any(r => r.Damage > 0), Is.True);
        }
        [Test] public void InvalidSnowConfigDoesNotAdvanceTurnClock()
        {
            snow.Scope = "board";
            Assert.Throws<InvalidOperationException>(() => gimmick.BeginTurn(state, snow));
            Assert.That(state.RegionGimmickStartedRound, Is.EqualTo(-1));
        }
        [Test] public void SnowCancellationCaptureRemovesDeadAndResolvesBossVictory()
        {
            state.Board.Tiles[5].FireTurnsRemaining = 2;
            state.Monsters.Add(new MonsterState { InstanceId = 7, CurrentTileIndex = 5, CurrentHealth = 1,
                MaxHealth = 100, Tier = MonsterTier.Boss });
            state.BossInstanceId = 7; state.BossSpawned = true;
            snow.TilesPerTurn = 8; session.SetRegion(snow);
            var results = session.ResolveTurnStartEffects();
            Assert.That(results.Any(r => r.TargetInstanceId == 7 && r.Killed), Is.True);
            Assert.That(state.Monsters, Is.Empty); Assert.That(state.IsVictory, Is.True);
        }
        [TestCase(0, true)] [TestCase(18, true)] [TestCase(9, false)] [TestCase(27, false)]
        public void OasisSourceTilesAreDataDriven(int index, bool expected)
        { Assert.That(session.IsOasisTile(index), Is.EqualTo(expected)); }
        [Test] public void OasisDoesNotActivateInOtherRegions()
        { session.SetRegion(snow); Assert.That(session.IsOasisTile(0), Is.False); Assert.Throws<InvalidOperationException>(() => Begin()); }
        [Test] public void OasisRequiresArrivalOnAnOasisSource()
        { session.TeleportPlayer(5); Assert.Throws<InvalidOperationException>(() => Begin()); }
        [Test] public void EmptyOasisTargetWaitsForChoiceThenBuildsExactlyTierTwo()
        {
            Begin(); Assert.That(state.Board.Tiles[5].Tower, Is.Null);
            Assert.That(session.PendingOasisUpgrade.RemainingChoices, Is.EqualTo(1)); Apply(0);
            Assert.That(state.Board.Tiles[5].Tower.UpgradeTier, Is.EqualTo(2));
            Assert.That(state.Board.Tiles[5].Tower.AppliedUpgradeIds, Is.EqualTo(new[] { "t2" }));
            Assert.That(session.PendingOasisUpgrade, Is.Null); Assert.That(state.Player.CurrentTileIndex, Is.Zero);
        }
        [Test] public void ExistingTierOneReceivesTwoSuccessiveChoices()
        {
            session.BuildTower(5, towers[0]); Begin();
            Assert.That(session.PendingOasisUpgrade.RemainingChoices, Is.EqualTo(2)); Apply(0);
            Assert.That(session.PendingOasisUpgrade.RemainingChoices, Is.EqualTo(1));
            Assert.That(session.GetOasisUpgradeOptions(5, towers, upgrades).All(u => u.Tier == 3), Is.True);
            Apply(1); Assert.That(state.Board.Tiles[5].Tower.UpgradeTier, Is.EqualTo(3));
            Assert.That(session.PendingOasisUpgrade, Is.Null);
        }
        [Test] public void ExistingTierTwoReceivesTierThreeAndAnotherDifferentTierThree()
        {
            session.BuildTower(5, towers[0]); session.UpgradeTower(5, upgrades[0]); Begin(); Apply(1);
            Assert.That(session.GetOasisUpgradeOptions(5, towers, upgrades).Any(u => u.Id == "t3a"), Is.False);
            Apply(2); Assert.That(state.Board.Tiles[5].Tower.AppliedUpgradeIds, Is.EqualTo(new[] { "t2", "t3a", "t3b" }));
        }
        [Test] public void TierThreeOasisRewardsDoNotConsumeBonusTileOrDiceVisitGrants()
        {
            session.BuildTower(5, towers[0]); session.UpgradeTower(5, upgrades[0]); session.UpgradeTower(5, upgrades[1]);
            state.Board.Tiles[5].IsBonusTile = true; Begin(); Apply(2); Apply(3);
            Assert.That(state.Board.Tiles[5].Tower.AdditionalTier3UpgradesClaimed, Is.Zero);
            session.UpgradeTower(5, upgrades[4]);
            Assert.That(state.Board.Tiles[5].Tower.AdditionalTier3UpgradesClaimed, Is.EqualTo(1));
        }
        [Test] public void RewardCountUsesJsonValueInsteadOfHardcodedTwo()
        {
            oasis.OverrideEffectValue = 3; session.BuildTower(5, towers[0]); Begin();
            Apply(0); Apply(1); Assert.That(session.PendingOasisUpgrade.RemainingChoices, Is.EqualTo(1));
            Apply(2); Assert.That(session.PendingOasisUpgrade, Is.Null);
        }
        [Test] public void WrongTierAndWrongElementCannotBuildOrConsumeChoice()
        {
            var wrongElement = Asset<TowerUpgradeDefinition>("wrong", 2, TowerElement.Ice);
            Begin(); Assert.Throws<InvalidOperationException>(() => Apply(1));
            Assert.Throws<InvalidOperationException>(() => session.ApplyOasisUpgrade(wrongElement, towers, upgrades));
            Assert.That(state.Board.Tiles[5].Tower, Is.Null); Assert.That(session.PendingOasisUpgrade.RemainingChoices, Is.EqualTo(1));
        }
        [Test] public void DuplicateSelectionCannotApplyTwice()
        {
            session.BuildTower(5, towers[0]); session.UpgradeTower(5, upgrades[0]); Begin(); Apply(1);
            Assert.Throws<InvalidOperationException>(() => Apply(1));
            Assert.That(session.PendingOasisUpgrade.RemainingChoices, Is.EqualTo(1));
        }
        [Test] public void NoFurtherPoolEndsActionWithoutBlocking()
        {
            upgrades = upgrades.Take(2).ToArray(); session.BuildTower(5, towers[0]);
            session.UpgradeTower(5, upgrades[0]); Begin(); Apply(1);
            Assert.That(session.PendingOasisUpgrade, Is.Null);
            Assert.That(session.GetOasisUpgradeOptions(5, towers, upgrades), Is.Empty);
        }
        [Test] public void NonBuildableAndRestrictedTilesCannotBeTargets()
        {
            Assert.Throws<InvalidOperationException>(() => Begin(9));
            state.Board.Tiles[5].OvergrowthTurnsRemaining = 1; state.Board.Tiles[5].OvergrowthDisallowsBuild = true;
            Assert.Throws<InvalidOperationException>(() => Begin()); Assert.That(state.Board.Tiles[5].Tower, Is.Null);
        }
        [Test] public void DisabledWeightsAreNotInOasisPool()
        {
            Set(upgrades[0], "weight", 0); Assert.That(session.GetOasisUpgradeOptions(5, towers, upgrades), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => Begin());
        }
        [Test] public void RegionChangeClearsPendingOasisAction()
        { Begin(); session.SetRegion(snow); Assert.That(session.PendingOasisUpgrade, Is.Null); Assert.That(state.Board.Tiles[5].Tower, Is.Null); }
        [Test] public void NewGameClearsPendingOasisAction()
        { Begin(); session.StartNewGame(); Assert.That(session.PendingOasisUpgrade, Is.Null); }
        [Test] public void AdvancingTurnClearsPendingOasisAction()
        { Begin(); session.CompleteRound(); Assert.That(session.PendingOasisUpgrade, Is.Null); }
    }
}
