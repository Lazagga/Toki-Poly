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
    public sealed class RegionMonsterPassiveTests
    {
        private GameState state;
        private MonsterService movement;
        private TowerEffectService effects;
        private readonly List<ScriptableObject> assets = new();
        [SetUp] public void Setup()
        {
            state = new GameState { Round = 1 };
            new BoardService().Initialize(state.Board);
            movement = new MonsterService(); effects = new TowerEffectService();
        }
        [TearDown] public void Cleanup() { foreach (var a in assets) UnityEngine.Object.DestroyImmediate(a); assets.Clear(); }
        private MonsterState Monster(string id = "MON_TEST", int tile = 1, int speed = 1)
        {
            var m = new MonsterState { InstanceId = state.Monsters.Count + 1, DefinitionId = id,
                MaxHealth = 1000, CurrentHealth = 1000, CurrentTileIndex = tile, DistanceTravelled = tile,
                MoveDistance = speed, PassiveRules = state.MonsterPassiveRules };
            m.BeginPassiveTurn(state.Round); state.Monsters.Add(m); return m;
        }
        private void NextTurn() { state.Round++; foreach (var m in state.Monsters) m.BeginPassiveTurn(state.Round); }
        private GameSession Session() => new(state, new BoardService(), new PlayerMovementService(),
            new WeightedDiceRoller(new SystemDiceRandom(new System.Random(1))), movement, new TowerService(), new TowerCombatService());
        private MonsterDefinition Definition(params TowerUpgradeEffect[] passive)
        {
            var d = ScriptableObject.CreateInstance<MonsterDefinition>(); assets.Add(d);
            Set(d, "id", "TEST_GROUP"); Set(d, "maxHp", 1000); Set(d, "passiveEffects", passive); return d;
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Passive(MonsterState m, string id, float value = 1) => m.PassiveEffects[id] = value;
        private TowerState Tower(int tile, int id = 10)
        {
            var t = new TowerState { InstanceId = id, DefinitionId = state.Board.Tiles[tile].BuildTowerDefinitionId,
                UpgradeTier = 3, LastResolvedDamage = 100 };
            t.AppliedUpgradeIds.Add("retained-upgrade"); state.Board.Tiles[tile].Tower = t; return t;
        }
        [Test] public void SnowLeopardRequiresOverlayNotConstructionElement()
        {
            var m = Monster(); Passive(m, "ice_overlay_damage_reduction_percent", 20);
            state.Board.Tiles[1].DefinitionId = "TILE_ICE";
            Assert.That(TowerUpgradeRules.Receive(state, m, 100, 0), Is.EqualTo(100));
            state.Board.Tiles[1].IceTurnsRemaining = 1;
            Assert.That(TowerUpgradeRules.Receive(state, m, 100, 0), Is.EqualTo(80));
            state.Board.Tiles[1].IceTurnsRemaining = 0;
            Assert.That(TowerUpgradeRules.Receive(state, m, 100, 0), Is.EqualTo(100));
        }
        [TestCase(false, 2)] [TestCase(true, 1)]
        public void WolfGroupProgressIsConfigurableAndIdempotent(bool groupCount, int expected)
        {
            state.MonsterPassiveRules.CountSpawnGroupAsOneKill = groupCount;
            var session = Session(); var d = Definition(new TowerUpgradeEffect { Id = "spawn_group_size", Value = 2 });
            var group = session.SpawnMonsterGroup(d);
            Assert.That(group.Count, Is.EqualTo(2));
            Assert.That(group[0].InstanceId, Is.Not.EqualTo(group[1].InstanceId));
            var first = new[] { new TowerAttackResult(0, group[0].InstanceId, 1000, true) };
            session.CollectKillRewards(first); session.CollectKillRewards(first);
            Assert.That(state.PendingProgressKills, Is.EqualTo(groupCount ? 0 : 1));
            session.CollectKillRewards(new[] { new TowerAttackResult(0, group[1].InstanceId, 1000, true) });
            Assert.That(state.PendingProgressKills, Is.EqualTo(expected));
            Assert.That(state.CapturedMonsterCount, Is.EqualTo(2));
        }
        private MonsterState Seal()
        {
            var m = Monster("BOSS_002"); m.Tier = MonsterTier.Boss;
            Passive(m, "submerge_cycle_length_turns", 3); Passive(m, "submerge_duration_turns", 2);
            Passive(m, "submerged_invincible"); Passive(m, "submerged_tile_effect_immune"); return m;
        }
        [Test] public void SealCycleStartsAtSpawnAndAdvancesOnlyOncePerRound()
        {
            var m = Seal();
            for (var i = 1; i <= 7; i++)
            {
                Assert.That(m.IsSubmerged, Is.EqualTo(i % 3 != 0));
                m.BeginPassiveTurn(state.Round); Assert.That(m.PassiveTurn, Is.EqualTo(i)); NextTurn();
            }
        }
        [Test] public void SealInvincibilityAndTargetExclusionAreIndependent()
        {
            var m = Seal(); Assert.That(m.IsTargetable, Is.False);
            Assert.That(TowerUpgradeRules.Receive(state, m, 100, 0), Is.Zero);
            state.MonsterPassiveRules.ExcludeSubmergedFromTargeting = false;
            Assert.That(m.IsTargetable, Is.True);
            Assert.That(m.ReceiveResolvedDamage(100), Is.Zero);
            Passive(m, "submerged_invincible", 0); Assert.That(m.ReceiveResolvedDamage(100), Is.EqualTo(100));
        }
        [Test] public void SubmergedSealSkipsFieldsThenSurfacedSealTouchesIntermediateFields()
        {
            var m = Seal(); m.MoveDistance = 2;
            state.Board.Tiles[2].FireTurnsRemaining = 5; state.Board.Tiles[1].IceTurnsRemaining = 5;
            Assert.That(movement.MoveAll(state)[0].Distance, Is.EqualTo(2)); Assert.That(m.BurnStacks, Is.Zero);
            NextTurn(); NextTurn(); m.CurrentTileIndex = m.DistanceTravelled = 1;
            state.Board.Tiles[1].IceTurnsRemaining = 0;
            movement.MoveAll(state); Assert.That(m.BurnStacks, Is.EqualTo(1));
        }
        [Test] public void NewBossesNeverGenerateCrowFeathers()
        {
            var m = Seal(); Tower(2); var session = Session(); session.MoveMonsters();
            Assert.That(m.BossInitialFeatherPlaced, Is.False);
            Assert.That(state.Board.Tiles.Any(t => t.HasBossFeather), Is.False);
        }
        [Test] public void MoltBlocksOneApplicationNotEveryStackAndResetsPerTurn()
        {
            var m = Monster(); Passive(m, "molt_status_block_per_turn");
            TowerUpgradeRules.AddBurn(state, m, 5, 0); Assert.That(m.BurnStacks, Is.Zero);
            TowerUpgradeRules.AddBurn(state, m, 5, 0); Assert.That(m.BurnStacks, Is.EqualTo(5));
            m.BeginPassiveTurn(state.Round); Assert.That(m.TryReceiveStatus("shock"), Is.True);
            NextTurn(); Assert.That(m.TryReceiveStatus("shock"), Is.False);
        }
        [Test] public void MoltTransferAndBurnPoliciesCanBeChanged()
        {
            var m = Monster(); Passive(m, "molt_status_block_per_turn");
            state.MonsterPassiveRules.MoltBlocksTransferredStatuses = false;
            Assert.That(m.TryReceiveStatus("freeze", true), Is.True);
            Assert.That(m.TryReceiveStatus("freeze"), Is.False);
            NextTurn(); state.MonsterPassiveRules.MoltBlocksBurnStacks = false;
            TowerUpgradeRules.AddBurn(state, m, 2, 0); Assert.That(m.BurnStacks, Is.EqualTo(2));
            Assert.That(m.TryReceiveStatus("shock"), Is.False);
        }
        [Test] public void MovementImmunityDoesNotPreventStandbyFire()
        {
            var m = Monster(speed: 3); Passive(m, "move_tile_effect_immune");
            for (var i = 1; i <= 4; i++) { state.Board.Tiles[i].FireTurnsRemaining = 2; state.Board.Tiles[i].IceTurnsRemaining = 2; }
            Assert.That(movement.MoveAll(state)[0].Distance, Is.EqualTo(3)); Assert.That(m.BurnStacks, Is.Zero);
            effects.ResolveMonsterStandby(state); Assert.That(m.BurnStacks, Is.EqualTo(1));
        }
        [Test] public void WaterTrailIncludesDepartureAndIntermediateNotDestinationAndExpires()
        {
            var m = Monster(speed: 3); Passive(m, "water_trail_duration_turns"); Passive(m, "water_trail_move_speed_bonus");
            movement.MoveAll(state);
            for (var i = 1; i <= 3; i++) Assert.That(state.Board.Tiles[i].WaterTrailTurnsRemaining, Is.EqualTo(2));
            Assert.That(state.Board.Tiles[4].WaterTrailTurnsRemaining, Is.Zero);
            effects.ResolveMonsterTurnEnd(state); Assert.That(state.Board.Tiles[1].WaterTrailTurnsRemaining, Is.EqualTo(1));
            effects.ResolveMonsterTurnEnd(state); Assert.That(state.Board.Tiles[1].WaterTrailTurnsRemaining, Is.Zero);
        }
        [TestCase(false, 1)] [TestCase(true, 2)]
        public void WaterEntryPolicyControlsSpeed(bool onEntry, int expected)
        {
            state.MonsterPassiveRules.WaterTrailBonusOnEntry = onEntry; Monster();
            state.Board.Tiles[2].WaterTrailTurnsRemaining = 1; state.Board.Tiles[2].WaterTrailMoveSpeedBonus = 1;
            Assert.That(movement.MoveAll(state)[0].Distance, Is.EqualTo(expected));
        }
        [TestCase(false, 1)] [TestCase(true, 2)]
        public void WaterCanIncludeOrExcludeItsCreator(bool include, int expected)
        {
            state.MonsterPassiveRules.WaterTrailAffectsCreators = include;
            var m = Monster(); Passive(m, "water_trail_duration_turns");
            state.Board.Tiles[1].WaterTrailTurnsRemaining = 1; state.Board.Tiles[1].WaterTrailMoveSpeedBonus = 1;
            Assert.That(movement.MoveAll(state)[0].Distance, Is.EqualTo(expected));
        }
        [Test] public void TurtleStopsOnThirdTurnAndReductionFlagCanBeChanged()
        {
            var m = Monster(); Passive(m, "periodic_stop_interval_turns", 3); Passive(m, "periodic_stop_duration_turns");
            Passive(m, "stop_damage_reduction_percent", 50); Passive(m, "stop_damage_reduction_only_while_stopped");
            Assert.That(m.ReceiveDamage(100, null), Is.EqualTo(100)); movement.MoveAll(state); NextTurn(); movement.MoveAll(state); NextTurn();
            Assert.That(movement.MoveAll(state)[0].Distance, Is.Zero); Assert.That(m.ReceiveDamage(100, null), Is.EqualTo(50));
            NextTurn(); Assert.That(movement.MoveAll(state)[0].Distance, Is.EqualTo(1));
            Passive(m, "stop_damage_reduction_only_while_stopped", 0); Assert.That(m.ReceiveDamage(100, null), Is.EqualTo(50));
        }
        [TestCase(false, true, 900)] [TestCase(true, true, 700)] [TestCase(false, false, 720)]
        public void CrocodileBushHealBasisAndFrequencyAreConfigurable(bool once, bool maxHp, int health)
        {
            state.MonsterPassiveRules.BushHealOncePerTurn = once;
            var m = Monster("BOSS_004", speed: 2); m.Tier = MonsterTier.Boss; m.CurrentHealth = 500;
            Passive(m, "bush_pass_heal_percent", 20); Passive(m, "bush_pass_heal_use_max_hp", maxHp ? 1 : 0);
            state.Board.Tiles[2].OvergrowthTurnsRemaining = state.Board.Tiles[3].OvergrowthTurnsRemaining = 2;
            movement.MoveAll(state); Assert.That(m.CurrentHealth, Is.EqualTo(health).Within(.001));
        }
        private MonsterState Eagle()
        {
            var m = Monster("BOSS_003"); m.Tier = MonsterTier.Boss;
            Passive(m, "move_tile_effect_immune"); Passive(m, "tower_snatch_count");
            Passive(m, "tower_snatch_swap_release"); Passive(m, "tower_snatch_disable_attack"); return m;
        }
        [Test] public void EagleExchangesInstancesAndPreservesUpgradesAndReturnsOnDeath()
        {
            var m = Eagle(); var first = Tower(2); var second = Tower(3, 11);
            movement.MoveAll(state); Assert.That(state.Board.Tiles[2].HasTower, Is.False);
            Assert.That(m.CarriedTowers[0].Tower, Is.SameAs(first));
            NextTurn(); movement.MoveAll(state);
            Assert.That(state.Board.Tiles[3].Tower, Is.SameAs(first)); Assert.That(first.AppliedUpgradeIds.Count, Is.EqualTo(1));
            Assert.That(m.CarriedTowers[0].Tower, Is.SameAs(second));
            m.CurrentHealth = 0; effects.ResolveDeathEffects(state, new List<TowerAttackResult>());
            Assert.That(state.Board.Tiles.Count(t => t.Tower == second), Is.EqualTo(1));
            Assert.That(state.Board.Tiles[0].HasTower, Is.False); Assert.That(m.CarriedTowers, Is.Empty);
        }
        [Test] public void StationaryLabMonsterDoesNotMoveOnWaterOrIce()
        {
            Monster(speed: 0);
            state.Board.Tiles[1].WaterTrailTurnsRemaining = 2; state.Board.Tiles[1].WaterTrailMoveSpeedBonus = 5;
            state.Board.Tiles[1].IceTurnsRemaining = 2;
            Assert.That(movement.MoveAll(state)[0].Distance, Is.Zero);
        }
        [Test] public void EagleCanReturnToCurrentTileInsteadOfOrigin()
        {
            var m = Eagle(); var t = Tower(2); movement.MoveAll(state);
            m.CurrentTileIndex = 5; state.MonsterPassiveRules.ReturnCarriedTowersToOrigin = false;
            MonsterService.ReleaseCarriedTowers(state, m);
            Assert.That(state.Board.Tiles[5].Tower, Is.SameAs(t));
            Assert.That(state.Board.Tiles[2].HasTower, Is.False);
        }
        [Test] public void FullBoardDefersReturnInsteadOfDeletingCarriedTower()
        {
            var m = Eagle(); var t = Tower(2); movement.MoveAll(state);
            foreach (var tile in state.Board.Tiles) if (tile.CanBuildTower && !tile.HasTower) Tower(tile.Index, 100 + tile.Index);
            MonsterService.ReleaseCarriedTowers(state, m);
            Assert.That(state.PendingTowerReturns, Contains.Item(m));
            Assert.That(m.CarriedTowers[0].Tower, Is.SameAs(t));
            state.Board.Tiles[2].Tower = null; MonsterService.ReleaseCarriedTowers(state, m);
            Assert.That(state.Board.Tiles[2].Tower, Is.SameAs(t)); Assert.That(state.PendingTowerReturns, Is.Empty);
        }
        [Test] public void EagleDoesNotSnatchIntermediateTowerUnlessRuleEnabled()
        {
            var m = Eagle(); m.MoveDistance = 2; var t = Tower(2);
            movement.MoveAll(state); Assert.That(state.Board.Tiles[2].Tower, Is.SameAs(t));
            m.CurrentTileIndex = m.DistanceTravelled = 1;
            state.MonsterPassiveRules.SnatchOnEachEnteredTile = true;
            movement.MoveAll(state); Assert.That(state.Board.Tiles[2].HasTower, Is.False);
            Assert.That(state.Board.Tiles[3].Tower, Is.SameAs(t));
        }
        [Test] public void EagleUpgradeResetRestoresBaseEffectsWhenConfigured()
        {
            var definition = ScriptableObject.CreateInstance<TowerDefinition>(); assets.Add(definition);
            Set(definition, "id", state.Board.Tiles[2].BuildTowerDefinitionId);
            Set(definition, "baseEffects", new[] { new TowerUpgradeEffect { Id = "burn", Value = 1 } });
            var m = Eagle(); var t = Tower(2); t.AppliedEffectIds.Add("explode");
            state.MonsterPassiveRules.PreserveSnatchedTowerUpgrades = false;
            movement.MoveAll(state, definitions: new[] { definition });
            Assert.That(t.UpgradeTier, Is.EqualTo(1)); Assert.That(t.AppliedUpgradeIds, Is.Empty);
            Assert.That(t.AppliedEffectIds, Is.EqualTo(new[] { "burn" }));
        }
        [TestCase(1, false)] [TestCase(0, true)]
        public void CarriedTowerAttackFlagIsRespected(int disabled, bool attacks)
        {
            var definition = ScriptableObject.CreateInstance<TowerDefinition>(); assets.Add(definition);
            Set(definition, "id", state.Board.Tiles[2].BuildTowerDefinitionId);
            Set(definition, "damage", 10); Set(definition, "range", 3);
            var m = Eagle(); Tower(2); movement.MoveAll(state); Passive(m, "tower_snatch_disable_attack", disabled);
            var results = Session().ResolveTowerCombat(new[] { definition }, Array.Empty<TowerUpgradeDefinition>());
            Assert.That(results.Any(r => r.Damage > 0), Is.EqualTo(attacks));
            Assert.That(m.CarriedTowers.Count, Is.EqualTo(1)); Assert.That(state.Board.Tiles[2].HasTower, Is.False);
        }
        [Test] public void ImportedJsonIncludesNewValuesAndKeepsExistingImmunities()
        {
            var db = Resources.Load<MonsterDatabaseDefinition>("GaeBullBing/MonsterDatabase");
            Assert.That(db.Monsters.First(m => m.Id == "MON_007").MoveDistance, Is.EqualTo(4));
            Assert.That(db.Monsters.First(m => m.Id == "MON_008").BaseDefense, Is.EqualTo(60));
            Assert.That(db.Monsters.First(m => m.Id == "BOSS_004").StatusImmunities, Is.Empty);
            Assert.That(db.Monsters.First(m => m.Id == "BOSS_002").StatusImmunities, Contains.Item("freeze"));
            var leopard = movement.Spawn(state, db.Monsters.First(m => m.Id == "MON_005"));
            Assert.That(leopard.GetPassiveValue("ice_overlay_damage_reduction_percent"), Is.EqualTo(20));
        }
        [Test] public void EagleCanKeepTowerAndCanLimitCrossElementDrops()
        {
            var m = Eagle(); var first = Tower(2); var second = Tower(3, 11);
            movement.MoveAll(state); Passive(m, "tower_snatch_swap_release", 0);
            NextTurn(); movement.MoveAll(state); Assert.That(state.Board.Tiles[3].Tower, Is.SameAs(second));
            Assert.That(m.CarriedTowers[0].Tower, Is.SameAs(first));
            Passive(m, "tower_snatch_swap_release"); state.MonsterPassiveRules.AllowCrossElementTowerDrop = false;
            state.Board.Tiles[4].BuildTowerDefinitionId = "OTHER";
            NextTurn(); movement.MoveAll(state); Assert.That(m.CarriedTowers[0].Tower, Is.SameAs(first));
        }
    }
}
