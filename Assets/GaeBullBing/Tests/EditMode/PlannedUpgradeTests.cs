using System;
using System.Collections.Generic;
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
    public sealed class PlannedUpgradeTests
    {
        private GameState state;
        private TowerEffectService effects;
        private TowerState tower;

        [SetUp]
        public void Setup()
        {
            state = new GameState { Round = 1 };
            new BoardService().Initialize(state.Board);
            tower = new TowerState { InstanceId = 1, DefinitionId = "TOW_01", Element = TowerElement.Fire,
                LastResolvedDamage = 100, LastResolvedRange = 10 };
            state.Board.Tiles[5].Tower = tower;
            effects = new TowerEffectService();
        }

        private static void Give(TowerState target, string id, float value = 1f)
        {
            if (!target.AppliedEffectIds.Contains(id)) target.AppliedEffectIds.Add(id);
            target.EffectValues[id] = value;
        }

        private MonsterState Monster(int id = 1, int tile = 5, float hp = 10000f)
        {
            var monster = new MonsterState { InstanceId = id, CurrentTileIndex = tile,
                DistanceTravelled = tile, MaxHealth = hp, CurrentHealth = hp, MoveDistance = 3 };
            state.Monsters.Add(monster);
            return monster;
        }

        private List<TowerAttackResult> Attack(int count = 1, int targets = 1, int damage = 100)
        {
            return new List<TowerAttackResult>(new TowerCombatService().ResolveByTower(state,
                new Dictionary<int, TowerCombatStats> { [1] = new TowerCombatStats(damage, 10, targets, count) },
                batch => {
                    var all = new List<TowerAttackResult>(batch);
                    all.AddRange(effects.ResolveAfterAttacks(state, batch));
                    return all;
                }, preferredTargetInstanceId: 1));
        }


        private static IEnumerable<TestCaseData> EveryNewUpgrade()
        {
            foreach (var id in new[] {
                "UPG_FIRE_T3_05",
                "UPG_FIRE_T3_06",
                "UPG_ICE_T3_04",
                "UPG_PHYSICS_T3_03",
                "UPG_ELECTRIC_T3_03",
                "UPG_PHYSICS_T3_04",
                "UPG_FIRE_T3_07",
                "UPG_ELECTRIC_T3_04",
                "UPG_FIRE_T3_08",
                "UPG_ICE_T3_05",
                "UPG_PHYSICS_T3_05",
                "UPG_ELECTRIC_T3_05",
                "UPG_FIRE_T3_09",
                "UPG_FIRE_T3_10",
                "UPG_FIRE_T3_11",
                "UPG_FIRE_T3_12",
                "UPG_FIRE_T3_13",
                "UPG_FIRE_T3_14",
                "UPG_FIRE_T3_15",
                "UPG_FIRE_T3_16",
                "UPG_FIRE_T2_05",
                "UPG_FIRE_T2_06",
                "UPG_ICE_T3_06",
                "UPG_ICE_T3_07",
                "UPG_ICE_T3_08",
                "UPG_ICE_T3_09",
                "UPG_ICE_T3_10",
                "UPG_ICE_T3_11",
                "UPG_ICE_T3_12",
                "UPG_ICE_T3_13",
                "UPG_ICE_T2_05",
                "UPG_ICE_T2_06",
                "UPG_PHYSICS_T3_06",
                "UPG_PHYSICS_T3_07",
                "UPG_PHYSICS_T3_08",
                "UPG_PHYSICS_T3_09",
                "UPG_PHYSICS_T3_11",
                "UPG_PHYSICS_T3_13",
                "UPG_PHYSICS_T2_05",
                "UPG_PHYSICS_T2_06",
                "UPG_ELECTRIC_T3_06",
                "UPG_ELECTRIC_T3_07",
                "UPG_ELECTRIC_T3_08",
                "UPG_ELECTRIC_T3_09",
                "UPG_ELECTRIC_T3_10",
                "UPG_ELECTRIC_T3_11",
                "UPG_ELECTRIC_T3_12",
                "UPG_ELECTRIC_T3_13",
                "UPG_ELECTRIC_T2_05",
                "UPG_ELECTRIC_T2_06",
            }) yield return new TestCaseData(id).SetName("NewUpgrade_Runtime_" + id);
        }

        [TestCaseSource(nameof(EveryNewUpgrade))]
        public void EachNewUpgradeRunsThroughRealSessionAndTurnEvents(string id)
        {
            var upgradeDb = Resources.Load<TowerUpgradeDatabaseDefinition>("GaeBullBing/TowerUpgradeDatabase");
            var towerDb = Resources.Load<TowerDatabaseDefinition>("GaeBullBing/TowerDatabase");
            var upgrade = upgradeDb.Upgrades.First(u => u.Id == id);
            var definition = towerDb.Towers.First(t => t.Element == upgrade.Element);
            var session = new GameSession(state, new BoardService(), new PlayerMovementService(),
                new WeightedDiceRoller(new SystemDiceRandom(new System.Random(1))), new MonsterService(),
                new TowerService(), new TowerCombatService());
            state.Board.Tiles[5].Tower = null;
            state.Board.Tiles[5].BuildTowerDefinitionId = definition.Id;
            session.BuildTower(5, definition);
            if (upgrade.Tier == 3)
                session.UpgradeTower(5, upgradeDb.Upgrades.First(u => u.Element == upgrade.Element && u.Tier == 2 && u.Weight > 0));
            session.UpgradeTower(5, upgrade);
            tower = state.Board.Tiles[5].Tower;
            session.CalculateTowerStats(definition, state.Board.Tiles[5], upgradeDb.Upgrades);
            var target = Monster(); target.BurnStacks = 10; target.FrostbiteStacks = 1;
            target.Shocked = true;
            Monster(2, 4); Monster(3, 6);
            state.Board.Tiles[5].FireTurnsRemaining = 2;
            session.ResolvePlayerArrivalEffects(5);
            session.ResolvePlayerPathEffects(4, 2);
            session.CompletePlayerLap();
            for (var round = 0; round < 4; round++)
            {
                session.ResolveTurnStartEffects();
                session.ResolveTestTowerAttack(5, 2, 1, true, towerDb.Towers, upgradeDb.Upgrades);
                session.ResolveMonsterTurnEndEffects();
                session.CompleteRound();
            }
            Assert.That(tower.AppliedUpgradeIds, Contains.Item(id));
            Assert.That(state.Monsters.All(m => !float.IsNaN(m.CurrentHealth)), Is.True);
        }

        [Test]
        public void UpgradeDefinitionsHaveUniqueIdsAndEveryEffectHasAHandler()
        {
            var db = Resources.Load<TowerUpgradeDatabaseDefinition>("GaeBullBing/TowerUpgradeDatabase");
            Assert.That(db, Is.Not.Null);
            Assert.That(db.Upgrades, Is.Not.Empty);
            Assert.That(db.Upgrades.Select(x => x.Id).Distinct().Count(), Is.EqualTo(db.Upgrades.Length));
            foreach (var upgrade in db.Upgrades)
                foreach (var effect in upgrade.Effects)
                    Assert.That(TowerEffectCatalog.IsImplemented(effect.Id), Is.True, upgrade.Id + ":" + effect.Id);
        }

        [TestCase("burn_inherit_on_kill")]
        [TestCase("burn_kill_fire_tile")]
        [TestCase("shatter_on_kill")]
        public void KillTraitsRunBeforeDeadMonstersAreRemoved(string id)
        {
            Give(tower, id, id == "shatter_on_kill" ? 10 : 1);
            var dead = Monster(1, 5, 10);
            dead.BurnStacks = 7;
            dead.FrozenMovesRemaining = 1;
            var other = Monster(2, 6);
            Attack();
            if (id == "burn_inherit_on_kill") Assert.That(other.BurnStacks, Is.EqualTo(7));
            if (id == "burn_kill_fire_tile") Assert.That(state.Board.Tiles[4].FireTurnsRemaining, Is.GreaterThan(0));
            if (id == "shatter_on_kill") Assert.That(other.CurrentHealth, Is.LessThan(other.MaxHealth));
            Assert.That(state.Monsters, Has.No.Member(dead));
        }

        [Test]
        public void DamageStoreIncludesFirstHitAndAllLaterDamageAndReleasesOnce()
        {
            Give(tower, "damage_store", 150);
            var target = Monster();
            Attack();
            target.ReceiveDamage(40, state.Difficulty);
            Assert.That(target.StoredDamage, Is.EqualTo(140));
            effects.ResolveMonsterTurnEnd(state);
            Assert.That(target.CurrentHealth, Is.EqualTo(9650));
            Assert.That(target.DamageStoreMarked, Is.False);
            effects.ResolveMonsterTurnEnd(state);
            Assert.That(target.CurrentHealth, Is.EqualTo(9650));
        }

        [Test]
        public void LineWindRepeatsPerAttackAndNeverCrossesLineStart()
        {
            Give(tower, "knockback_line"); Give(tower, "knockback_line_burn");
            var a = Monster(1, 5); var b = Monster(2, 2);
            Attack(2);
            Assert.That(a.CurrentTileIndex, Is.EqualTo(3));
            Assert.That(b.CurrentTileIndex, Is.EqualTo(0));
            Assert.That(a.BurnStacks, Is.EqualTo(2));
            Assert.That(a.KnockbackImmunityPending, Is.True);
        }

        [Test]
        public void TeleportIgnoresKnockbackImmunityAndIsConsumedOnlyOnActualMovement()
        {
            Give(tower, "teleport_line_start");
            var target = Monster(1, 9); target.StatusImmunities.Add("knockback");
            state.Board.Tiles[0].FireTurnsRemaining = 2;
            Attack();
            Assert.That(target.CurrentTileIndex, Is.Zero);
            Assert.That(target.BurnStacks, Is.EqualTo(1));
            Assert.That(target.LineTeleportConsumed, Is.True);
            target.CurrentTileIndex = 5; Attack();
            Assert.That(target.CurrentTileIndex, Is.EqualTo(5));
            target.LineTeleportConsumed = false; target.CurrentTileIndex = 0; Attack();
            Assert.That(target.LineTeleportConsumed, Is.False);
        }

        [Test]
        public void DisableBurnBlocksBaseDoubleAndSpreadButDoesNotEraseExistingStacks()
        {
            Give(tower, "disable_burn"); Give(tower, "burn"); Give(tower, "double_burn");
            Give(tower, "tile_burn_spread");
            var target = Monster(); target.BurnStacks = 4;
            var other = Monster(2);
            Attack();
            Assert.That(target.BurnStacks, Is.EqualTo(4)); Assert.That(other.BurnStacks, Is.Zero);
        }

        [TestCase("fire_tile_damage_bonus", 50, 150)]
        [TestCase("frostbite_damage_bonus", 50, 150)]
        [TestCase("low_hp_damage_bonus", 50, 150)]
        [TestCase("burn_consume_burst", 30, 400)]
        public void ConditionsUsePreAttackSnapshot(string id, float value, float expected)
        {
            Give(tower, id, value); var target = Monster();
            if (id == "fire_tile_damage_bonus") state.Board.Tiles[5].FireTurnsRemaining = 2;
            if (id == "frostbite_damage_bonus") target.FrostbiteStacks = 1;
            if (id == "low_hp_damage_bonus") target.CurrentHealth = 5000;
            if (id == "burn_consume_burst") target.BurnStacks = 10;
            var before = target.CurrentHealth;
            Attack(); Assert.That(before - target.CurrentHealth, Is.EqualTo(expected).Within(.01f));
        }

        [Test]
        public void ThisHitNewBurnAndShockCannotEnableItsOwnConditional()
        {
            Give(tower, "burn"); Give(tower, "burn_consume_burst", 30);
            Give(tower, "shock"); Give(tower, "status_count_damage", 10);
            var target = Monster(); target.BurnStacks = 9;
            Attack();
            // One pre-existing status, with shock applied before conditional damage.
            Assert.That(target.CurrentHealth, Is.EqualTo(9887).Within(.01));
            Assert.That(target.BurnStacks, Is.EqualTo(10));
        }

        [Test]
        public void GroupCountTracksAllElementsAndDecreasesWhenTowerIsRemoved()
        {
            Give(tower, "upgrade_count_damage", 30);
            state.Board.Tiles[6].Tower = new TowerState { InstanceId = 2, Element = TowerElement.Ice };
            Give(state.Board.Tiles[6].Tower, "upgrade_count_damage", 30);
            Assert.That(TowerUpgradeRules.ExtraBaseDamage(state, tower), Is.EqualTo(60));
            state.Board.Tiles[6].Tower = null;
            Assert.That(TowerUpgradeRules.ExtraBaseDamage(state, tower), Is.EqualTo(30));
        }

        [Test]
        public void ConsecutiveAndTurnRampIncreaseHitsAndResetOnNewTargetOrTurn()
        {
            Give(tower, "consecutive_target_damage", 20); Give(tower, "consecutive_target_damage_cap", 100);
            var target = Monster(); Attack(3);
            Assert.That(target.CurrentHealth, Is.EqualTo(9640).Within(.01));
            tower.AppliedEffectIds.Clear(); tower.EffectValues.Clear();
            Give(tower, "attack_ramp_turn", 15); target.CurrentHealth = 10000; Attack(3);
            Assert.That(target.CurrentHealth, Is.EqualTo(9655).Within(.01));
            Attack(); Assert.That(target.CurrentHealth, Is.EqualTo(9555).Within(.01));
        }

        [Test]
        public void BurnTickSplashDoesNotAddStacksOrRecursivelySplash()
        {
            Give(tower, "burn_tick_splash");
            var target = Monster(); target.BurnStacks = 2; target.BurnSourceTowerId = 1;
            var other = Monster(2);
            effects.ResolveMonsterStandby(state);
            Assert.That(target.CurrentHealth, Is.EqualTo(9950));
            Assert.That(other.CurrentHealth, Is.EqualTo(9950));
            Assert.That(other.BurnStacks, Is.Zero);
        }

        [Test]
        public void BurnCapCanIncreaseAndLineDurationBonusesStack()
        {
            Give(tower, "burn_max_stack_add", 20); Give(tower, "double_burn");
            Give(tower, "line_fire_tile_duration_add");
            var target = Monster(); target.BurnStacks = 20; Attack();
            Assert.That(target.BurnStacks, Is.EqualTo(40));
            effects.PlaceFireField(state, 4, 1);
            Assert.That(state.Board.Tiles[4].FireTurnsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void BurnSplashCopiesResolvedDamageWithoutSecondDefenseOrIncomingMultiplier()
        {
            Give(tower, "burn_tick_splash");
            var source = Monster(); source.BurnStacks = 2; source.BurnSourceTowerId = 1;
            source.BaseDefense = 100;
            var other = Monster(2); other.BaseDefense = 900; other.Shocked = true;
            other.DamageStoreMarked = true;
            var hits = new List<TowerAttackResult>();
            TowerUpgradeRules.BurnTick(state, source, 5, hits);
            Assert.That(source.CurrentHealth, Is.EqualTo(9975));
            Assert.That(other.CurrentHealth, Is.EqualTo(9975));
            Assert.That(other.StoredDamage, Is.EqualTo(25));
            Assert.That(other.LastDamageTowerId, Is.EqualTo(1));
        }

        [Test]
        public void OverheatAttacksTwiceThenRestsAndFeatherDoesNotConsumeAttackCycle()
        {
            Give(tower, "overheat_cycle", 2); var target = Monster();
            Attack();
            Assert.That(target.CurrentHealth, Is.EqualTo(9900), "The first phase must attack, not rest.");
            Attack(); Attack();
            Assert.That(target.CurrentHealth, Is.EqualTo(9800));
            tower.IsFeatherSealed = true; Attack();
            tower.IsFeatherSealed = false; Attack();
            Assert.That(target.CurrentHealth, Is.EqualTo(9700));
        }

        [Test]
        public void FrostbiteMultiplierResidualAndSpreadBurstAtMostOncePerResolution()
        {
            Give(tower, "frostbite"); Give(tower, "frostbite_burst_multiplier", 500);
            Give(tower, "frostbite_residual"); Give(tower, "frostbite_burst_spread");
            var a = Monster(); a.FrostbiteStacks = 2;
            var b = Monster(2); b.FrostbiteStacks = 2;
            Attack();
            Assert.That(a.CurrentHealth, Is.EqualTo(9400));
            Assert.That(b.CurrentHealth, Is.EqualTo(9500));
            Assert.That(a.FrostbiteStacks, Is.EqualTo(2));
            Assert.That(b.FrostbiteStacks, Is.EqualTo(1));
        }

        [Test]
        public void FreezeTwoBlocksTwoMovesAndSlowIsConsumedOnNextActualMove()
        {
            Give(tower, "freeze", 2); Give(tower, "move_reduce");
            var target = Monster(); Attack();
            var movement = new MonsterService();
            Assert.That(movement.MoveAll(state)[0].Distance, Is.Zero);
            Assert.That(movement.MoveAll(state)[0].Distance, Is.Zero);
            Assert.That(movement.MoveAll(state)[0].Distance, Is.EqualTo(2));
            Assert.That(target.FreezeImmuneThisTurn, Is.True);
            Assert.That(target.NextMoveReduction, Is.Zero);
        }

        [Test]
        public void IceVulnerabilityAndShockUseCombinedIncomingDamageRate()
        {
            Give(tower, "ice_tile_vulnerability", 30);
            var target = Monster(); target.Shocked = true;
            state.Board.Tiles[5].IceTurnsRemaining = 2;
            Assert.That(TowerUpgradeRules.Receive(state, target, 100, 1), Is.EqualTo(160).Within(.01));
        }

        [Test]
        public void PeriodicAttacksFireImmediatelyThenEveryThirdPhase()
        {
            Give(tower, "blizzard_line", 3); Give(tower, "lightning_strike", 300);
            var target = Monster();
            var hits = effects.ResolvePeriodicAttack(state, tower);
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(target.CurrentHealth, Is.EqualTo(9600));
            Assert.That(effects.ResolvePeriodicAttack(state, tower), Is.Empty);
            Assert.That(effects.ResolvePeriodicAttack(state, tower), Is.Empty);
            Assert.That(effects.ResolvePeriodicAttack(state, tower).Count, Is.EqualTo(2));
        }

        [Test]
        public void NewPeriodicEffectIsReadyOnOldTowerIndependentlyOfExistingCooldown()
        {
            tower.PeriodicTurnCount = 41;
            Give(tower, "blizzard_line", 3); Monster();
            Assert.That(effects.ResolvePeriodicAttack(state, tower).Count, Is.EqualTo(1));
            Give(tower, "lightning_strike", 300);
            Assert.That(effects.ResolvePeriodicAttack(state, tower).Count, Is.EqualTo(1));
            Assert.That(effects.ResolvePeriodicAttack(state, tower), Is.Empty);
            Assert.That(effects.ResolvePeriodicAttack(state, tower).Count, Is.EqualTo(1));
        }

        [Test]
        public void PeriodicReadyStateWaitsForTargetAndSurvivesFeatherSeal()
        {
            Give(tower, "lightning_strike", 300);
            Assert.That(effects.ResolvePeriodicAttack(state, tower), Is.Empty);
            Monster();
            Assert.That(effects.ResolvePeriodicAttack(state, tower).Count, Is.EqualTo(1));
            tower.IsFeatherSealed = true;
            for (var i = 0; i < 4; i++) Assert.That(effects.ResolvePeriodicAttack(state, tower), Is.Empty);
            tower.IsFeatherSealed = false;
            Assert.That(effects.ResolvePeriodicAttack(state, tower).Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecuteUsesPreHitThresholdAndBossGetsDamageInstead(bool boss)
        {
            Give(tower, "execute_threshold", 20);
            var target = Monster(); target.CurrentHealth = 2000;
            if (boss) target.Tier = MonsterTier.Boss;
            Attack();
            Assert.That(target.CurrentHealth, Is.EqualTo(boss ? 1800 : 0));
        }

        [Test]
        public void PierceHitsPreviousTileWithBaseEffects()
        {
            Give(tower, "pierce"); Give(tower, "burn");
            var first = Monster(1, 5); var second = Monster(2, 4);
            Attack(); Assert.That(first.BurnStacks, Is.EqualTo(1));
            Assert.That(second.CurrentHealth, Is.EqualTo(9900)); Assert.That(second.BurnStacks, Is.EqualTo(1));
        }

        [Test]
        public void TwoStepKnockbackProcessesEveryTileAndImpactDoesNotLoop()
        {
            Give(tower, "knockback", 2); Give(tower, "knockback_impact", 50);
            var target = Monster(); var other = Monster(2, 4);
            state.Board.Tiles[4].FireTurnsRemaining = 2;
            Attack();
            Assert.That(target.CurrentTileIndex, Is.EqualTo(3));
            Assert.That(target.BurnStacks, Is.EqualTo(1));
            Assert.That(other.CurrentHealth, Is.EqualTo(9950));
        }

        [Test]
        public void PlayerArrivalPathAndPassTraitsRespectDifferentPathEndpoints()
        {
            Give(tower, "path_damage"); Give(tower, "step_damage_double", 2);
            Give(tower, "quake_line_stun"); Give(tower, "pass_attack_count");
            var a = Monster(1, 5); var b = Monster(2, 6); var c = Monster(3, 7);
            effects.ResolvePlayerArrival(state, 5);
            Assert.That(a.StunnedMovesRemaining, Is.EqualTo(1));
            Assert.That(TowerUpgradeRules.ExtraDamageRate(state, state.Board.Tiles[5]), Is.EqualTo(1));
            effects.ResolvePlayerPath(state, 5, 2, false);
            Assert.That(a.CurrentHealth, Is.EqualTo(9900)); Assert.That(b.CurrentHealth, Is.EqualTo(9900));
            Assert.That(c.CurrentHealth, Is.EqualTo(10000));
            effects.ResolvePlayerPath(state, 4, 1, false);
            Assert.That(TowerUpgradeRules.ExtraAttackCount(state, tower), Is.EqualTo(1));
            tower.PassAttackRound = -1; effects.ResolvePlayerPath(state, 4, 1, true);
            Assert.That(tower.PassAttackRound, Is.EqualTo(-1));
        }

        [Test]
        public void StrongShockAuraAndLinkApplyWithoutRecursiveLinking()
        {
            Give(tower, "shock_aura"); Give(tower, "shock_amplify", 60); Give(tower, "shock_link", 30);
            var a = Monster(); var b = Monster(2, 4);
            effects.ResolveTurnStart(state); Assert.That(b.Shocked, Is.True);
            Attack();
            Assert.That(a.ShockDamageBonus, Is.EqualTo(.6f));
            Assert.That(a.CurrentHealth, Is.EqualTo(9870).Within(.01));
            Assert.That(b.CurrentHealth, Is.EqualTo(9949.3f).Within(.01));
        }

        [Test]
        public void TemporaryTowerDamageBuffStacksAndSurvivesUntilNextTurnEnd()
        {
            Give(tower, "tile_tower_damage_buff", 30); Monster();
            Attack(2);
            Assert.That(TowerUpgradeRules.ExtraDamageRate(state, state.Board.Tiles[5]), Is.EqualTo(.6f).Within(.001));
            effects.ResolveMonsterTurnEnd(state); state.Round++;
            Assert.That(TowerUpgradeRules.ExtraDamageRate(state, state.Board.Tiles[5]), Is.EqualTo(.6f).Within(.001));
            effects.ResolveMonsterTurnEnd(state);
            Assert.That(tower.TemporaryDamageBuffs, Is.Empty);
        }

        [Test]
        public void TransferAddsStacksDamageAndCopiesOnlyEmptyDamageStoreMarker()
        {
            Give(tower, "status_transfer"); Give(tower, "transfer_stack_add");
            Give(tower, "transfer_damage", 50); Give(tower, "damage_store", 150);
            var a = Monster(); a.BurnStacks = 2; var b = Monster(2, 4);
            Attack();
            Assert.That(b.BurnStacks, Is.EqualTo(3)); Assert.That(b.CurrentHealth, Is.EqualTo(9950));
            Assert.That(b.DamageStoreMarked, Is.True); Assert.That(b.StoredDamage, Is.EqualTo(50));
        }

        [Test]
        public void BurnSplashUsesOnlyStacksOwnedByTheSplashTowerDuringMovement()
        {
            Give(tower, "burn_tick_splash");
            var otherTower = new TowerState { InstanceId = 2 };
            state.Board.Tiles[6].Tower = otherTower;
            var target = Monster(1, 4); target.MoveDistance = 1;
            var other = Monster(2, 5); other.MoveDistance = 0;
            TowerUpgradeRules.AddBurn(state, target, 2, 1);
            TowerUpgradeRules.AddBurn(state, target, 2, 2);
            state.Board.Tiles[5].FireTurnsRemaining = 2;
            state.Board.Tiles[5].FireSourceTowerId = 2;
            new MonsterService().MoveAll(state);
            Assert.That(other.CurrentHealth, Is.EqualTo(9950).Within(.01));
        }

        [Test]
        public void KnockbackImmunityEndsOnlyAfterEnteringNextLine()
        {
            Give(tower, "knockback"); var target = Monster(1, 8);
            Attack(); effects.ResolveMonsterStandby(state);
            Assert.That(target.KnockbackConsumed, Is.True);
            target.MoveDistance = 1;
            var movement = new MonsterService();
            movement.MoveAll(state); Assert.That(target.KnockbackConsumed, Is.True);
            movement.MoveAll(state); Assert.That(target.KnockbackConsumed, Is.True);
            movement.MoveAll(state); Assert.That(target.CurrentTileIndex, Is.EqualTo(10));
            Assert.That(target.KnockbackConsumed, Is.False);
        }

        [Test]
        public void LockedAttackCountCannotBeRaisedByTemporaryAttackBonuses()
        {
            var db = Resources.Load<TowerUpgradeDatabaseDefinition>("GaeBullBing/TowerUpgradeDatabase");
            var definitions = Resources.Load<TowerDatabaseDefinition>("GaeBullBing/TowerDatabase");
            var definition = definitions.Towers.First(d => d.Element == TowerElement.Ice);
            var upgrade = db.Upgrades.First(d => d.Id == "UPG_ICE_T3_09");
            tower.AppliedUpgradeIds.Add(upgrade.Id);
            var stats = TowerStatCalculator.Calculate(definition, tower, db.Upgrades, 0f, 0, 5).CombatStats;
            Assert.That(stats.AttackCount, Is.EqualTo(1));
            tower.BonusAttackCount = 4;
            var target = Monster();
            var hits = new TowerCombatService().ResolveByTower(state,
                new Dictionary<int, TowerCombatStats> { [1] = stats });
            Assert.That(hits.Count, Is.EqualTo(1));
        }

        [Test]
        public void NewBurnUpgradeAddsToBaseBurnInsteadOfReplacingIt()
        {
            var db = Resources.Load<TowerUpgradeDatabaseDefinition>("GaeBullBing/TowerUpgradeDatabase");
            var definitions = Resources.Load<TowerDatabaseDefinition>("GaeBullBing/TowerDatabase");
            var definition = definitions.Towers.First(d => d.Element == TowerElement.Fire);
            state.Board.Tiles[5].Tower = null; state.Board.Tiles[5].BuildTowerDefinitionId = definition.Id;
            var session = new GameSession(state, new BoardService(), new PlayerMovementService(),
                new WeightedDiceRoller(new SystemDiceRandom()), new MonsterService(), new TowerService(), new TowerCombatService());
            session.BuildTower(5, definition);
            session.UpgradeTower(5, db.Upgrades.First(d => d.Id == "UPG_FIRE_T2_05"));
            var target = Monster();
            session.ResolveTestTowerAttack(5, 1, 1, true, definitions.Towers, db.Upgrades);
            Assert.That(target.BurnStacks, Is.EqualTo(2));
        }

        [Test]
        public void ChargeWaitsForActualAttackCapsAtThreeAndOnlyBoostsFirstVolley()
        {
            Give(tower, "charge_damage", 100); Give(tower, "charge_cap", 3);
            Attack(); Attack(); Attack(); Attack();
            Assert.That(tower.ChargeCount, Is.EqualTo(3));
            var target = Monster(); Attack(2);
            Assert.That(target.CurrentHealth, Is.EqualTo(9500)); Assert.That(tower.ChargeCount, Is.Zero);
        }
    }
}
