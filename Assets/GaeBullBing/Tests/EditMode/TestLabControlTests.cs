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
    public sealed class TestLabControlTests
    {
        private GameState state;
        private GameSession session;
        private TowerDefinition definition;
        private TowerState tower;
        private MonsterState monster;
        private readonly List<UnityEngine.Object> assets = new();

        [SetUp]
        public void Setup()
        {
            state = new GameState { Round = 1 };
            new BoardService().Initialize(state.Board);
            definition = ScriptableObject.CreateInstance<TowerDefinition>(); assets.Add(definition);
            Set(definition, "id", "TEST_LAB_TOWER"); Set(definition, "damage", 10);
            Set(definition, "range", 2); Set(definition, "targetCount", 1); Set(definition, "attackCount", 3);
            Set(definition, "element", TowerElement.Fire);
            session = new GameSession(state, new BoardService(), new PlayerMovementService(),
                new WeightedDiceRoller(new SystemDiceRandom(new System.Random(1))), new MonsterService(), new TowerService(), new TowerCombatService());
            state.Board.Tiles[5].BuildTowerDefinitionId = definition.Id;
            tower = session.BuildTower(5, definition);
            monster = new MonsterState { InstanceId = 1, CurrentTileIndex = 5, DistanceTravelled = 5,
                CurrentHealth = 10000, MaxHealth = 10000, MoveDistance = 3 };
            state.Monsters.Add(monster);
        }
        [TearDown] public void Cleanup() { foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset); assets.Clear(); }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private void Effect(string id, float value = 1) { tower.AppliedEffectIds.Add(id); tower.EffectValues[id] = value; }
        private IReadOnlyList<TowerAttackResult> Attack(bool force = false, int count = 1, bool ignoreRange = false, int preferred = 0) =>
            session.ResolveLabTowerPhase(5, force, count, preferred, ignoreRange, new[] { definition }, Array.Empty<TowerUpgradeDefinition>());

        [Test] public void ActualAttackPreservesCalculatedAttackCount()
        { Assert.That(Attack().Count(r => r.Damage > 0), Is.EqualTo(3)); }
        [Test] public void ForcedAttackOverridesCountWithoutAddingTemporaryBonusAgain()
        { tower.BonusAttackCount = 2; Assert.That(Attack(true, 2).Count(r => r.Damage > 0), Is.EqualTo(2)); }
        [Test] public void ActualAttackIncludesTemporaryBonus()
        { tower.BonusAttackCount = 2; Assert.That(Attack().Count(r => r.Damage > 0), Is.EqualTo(5)); }
        [Test] public void ActualAttackCannotIgnoreRange()
        { monster.CurrentTileIndex = 15; Assert.That(Attack(false, 99, true), Is.Empty); }
        [Test] public void ForcedAttackCanIgnoreRange()
        { monster.CurrentTileIndex = 15; Assert.That(Attack(true, 2, true).Count(r => r.Damage > 0), Is.EqualTo(2)); }
        [Test] public void ActualCooldownTicksAndSkips()
        { tower.AttackCooldownRounds = 2; Assert.That(Attack(), Is.Empty); Assert.That(tower.AttackCooldownRounds, Is.EqualTo(1)); }
        [Test] public void ForcedAttackBypassesCooldownWithoutTickingIt()
        { tower.AttackCooldownRounds = 2; Assert.That(Attack(true).Any(r => r.Damage > 0), Is.True); Assert.That(tower.AttackCooldownRounds, Is.EqualTo(2)); }
        [Test] public void FeatherBlocksActualButNotForcedOrdinaryAttacks()
        { tower.IsFeatherSealed = true; Assert.That(Attack(), Is.Empty); Assert.That(Attack(true).Any(r => r.Damage > 0), Is.True); }
        [Test] public void OverheatBlocksActualButNotForced()
        { Effect("overheat_cycle", 2); tower.OverheatAttackTurns = 2; Assert.That(Attack(), Is.Empty); tower.OverheatAttackTurns = 2; Assert.That(Attack(true).Any(r => r.Damage > 0), Is.True); }
        [Test] public void AutomaticTargetingDoesNotImplicitlyPreferSelectedMonster()
        {
            var boss = new MonsterState { InstanceId = 2, Tier = MonsterTier.Boss, CurrentTileIndex = 5,
                MaxHealth = 10000, CurrentHealth = 10000 };
            state.Monsters.Add(boss);
            Assert.That(Attack(true).First(r => r.Damage > 0).TargetInstanceId, Is.EqualTo(2));
            Assert.That(Attack(true, preferred: 1).First(r => r.Damage > 0).TargetInstanceId, Is.EqualTo(1));
        }
        [Test] public void ManualRepetitionDoesNotAdvanceRound()
        { Attack(); Attack(); Assert.That(state.Round, Is.EqualTo(1)); Assert.That(tower.PeriodicTurnCount, Is.EqualTo(2)); }
        [Test] public void SingleAndFullCombatSharePeriodicBeforeOrdinaryOrder()
        {
            Effect("lightning_strike", 300); tower.PeriodicTurnCount = 2;
            var single = Attack().Where(r => r.Damage > 0).Select(r => r.Damage).ToArray();
            monster.CurrentHealth = monster.MaxHealth; monster.Shocked = false; tower.PeriodicTurnCount = 2;
            tower.EffectReadyPhases.Clear();
            var all = session.ResolveTowerCombat(new[] { definition }, Array.Empty<TowerUpgradeDefinition>())
                .Where(r => r.Damage > 0).Select(r => r.Damage).ToArray();
            Assert.That(all, Is.EqualTo(single)); Assert.That(single.First(), Is.EqualTo(30));
        }
        [Test] public void ForcedCountAlsoOverridesSetOneAttackUpgrade()
        {
            var upgrade = ScriptableObject.CreateInstance<TowerUpgradeDefinition>(); assets.Add(upgrade);
            Set(upgrade, "id", "TEST_LOCK"); Set(upgrade, "statModifiers", new[] { new TowerStatModifier { Stat = "attack_count", Operation = "set", Value = 1 } });
            tower.AppliedUpgradeIds.Add(upgrade.Id); tower.BonusAttackCount = 4;
            Assert.That(session.ResolveLabTowerPhase(5, false, 9, 0, false, new[] { definition }, new[] { upgrade }).Count(r => r.Damage > 0), Is.EqualTo(1));
            Assert.That(session.ResolveLabTowerPhase(5, true, 2, 0, false, new[] { definition }, new[] { upgrade }).Count(r => r.Damage > 0), Is.EqualTo(2));
        }
        [Test] public void FullCombatRespectsDescendingTowerOrder()
        {
            state.Board.Tiles[7].BuildTowerDefinitionId = definition.Id;
            var other = session.BuildTower(7, definition);
            Assert.That(session.ResolveTowerCombat(new[] { definition }, Array.Empty<TowerUpgradeDefinition>()).First(r => r.Damage > 0).TowerInstanceId, Is.EqualTo(other.InstanceId));
        }
        [Test] public void TraceCapturesOldConditionAndNewStacksSeparately()
        {
            Effect("burn"); Effect("burn_explode", 10); monster.BurnStacks = 9;
            state.CombatTrace = new CombatTrace(); Attack(true);
            Assert.That(monster.BurnStacks, Is.EqualTo(10));
            Assert.That(state.CombatTrace.Entries.Any(e => e.Phase == 1 && e.Message.Contains("burn_explode: NOT MET")), Is.True);
            Assert.That(state.CombatTrace.Entries.Any(e => e.Phase == 3 && e.Message.Contains("burn 9 -> 10")), Is.True);
            Assert.That(state.CombatTrace.Entries.Any(e => e.Phase == 2 && e.Message.Contains("damage raw")), Is.True);
        }
        [Test] public void TraceCapturesDefenseFormulaAndSkipReason()
        {
            state.CombatTrace = new CombatTrace(); monster.BaseDefense = 100; Attack(true);
            Assert.That(state.CombatTrace.Entries.Any(e => e.Message.Contains("defense 100") && e.Message.Contains("= 5")), Is.True);
            tower.IsFeatherSealed = true; Attack();
            Assert.That(state.CombatTrace.Entries.Any(e => e.Message.Contains("feather sealed")), Is.True);
        }
        [Test] public void FrostbiteBurstIsLoggedInsideBasePhase()
        {
            state.CombatTrace = new CombatTrace(); Effect("frostbite"); monster.FrostbiteStacks = 2; Attack(true);
            Assert.That(state.CombatTrace.Entries.Any(e => e.Phase == 3 && e.Source == "frostbite immediate burst" && e.Message.Contains("damage raw")), Is.True);
        }
        [Test] public void NormalGameplayDoesNotCreateTraceRecorder()
        { Attack(); Assert.That(state.CombatTrace, Is.Null); }
        [Test] public void TraceIsBoundedAndNestedPhaseRestoresEvenOnFailure()
        {
            var trace = new CombatTrace();
            Assert.Throws<InvalidOperationException>(() => { using (trace.Enter(3, "outer")) using (trace.Enter(5, "inner")) throw new InvalidOperationException(); });
            Assert.That(trace.Phase, Is.Zero);
            var step = trace.Enter(4, "explicit close", target: monster);
            step.Dispose(); var closedCount = trace.Entries.Count; step.Dispose();
            Assert.That(trace.Entries.Count, Is.EqualTo(closedCount));
            for (var i = 0; i < 4000; i++) trace.Add("test");
            Assert.That(trace.Entries.Count, Is.EqualTo(CombatTrace.EntryLimit)); Assert.That(trace.DroppedEntries, Is.GreaterThan(0));
        }
        [Test] public void OnHitTraceClosesBeforeConditionalDamage()
        {
            state.CombatTrace = new CombatTrace(); Effect("burn"); Effect("burn_consume_burst", 30);
            monster.BurnStacks = 10; Attack(true);
            var conditionalIndex = state.CombatTrace.Entries.FindIndex(e => e.Phase == 5);
            Assert.That(conditionalIndex, Is.GreaterThan(0));
            Assert.That(state.CombatTrace.Entries.Skip(conditionalIndex).Any(e => e.Phase == 4), Is.False);
        }
        [Test] public void NonConditionalShockAmplifyPrecedesConditionalFreezeDamage()
        {
            Effect("shock_amplify", 60); Effect(TowerEffectCatalog.FreezeDamageMultiply, 5);
            monster.FrozenMovesRemaining = 1;
            Assert.That(Attack(true).Sum(r => r.Damage), Is.EqualTo(74).Within(.001));
        }
        [Test] public void MonsterEditorChangesMaximumHealthAndPreservesExactRatio()
        {
            var edit = TestMonsterSettings.Read(monster); edit.MaxHealth = 50000; edit.Health = 10000;
            Assert.That(edit.Apply(monster, 36, out _), Is.True); Assert.That(monster.CurrentHealth / monster.MaxHealth, Is.EqualTo(.2f));
        }
        [TestCase(-1)] [TestCase(36)] public void InvalidPositionDoesNotPartiallyApplyHealth(int tile)
        {
            var edit = TestMonsterSettings.Read(monster); edit.Health = 123; edit.Tile = tile;
            Assert.That(edit.Apply(monster, 36, out _), Is.False); Assert.That(monster.CurrentHealth, Is.EqualTo(10000));
        }
        [Test] public void NaNAndOverMaxHealthAreRejectedAtomically()
        {
            var edit = TestMonsterSettings.Read(monster); edit.Health = float.NaN;
            Assert.That(edit.Apply(monster, 36, out _), Is.False); edit.Health = monster.MaxHealth + 1;
            Assert.That(edit.Apply(monster, 36, out _), Is.False); Assert.That(monster.CurrentHealth, Is.EqualTo(10000));
        }
        [Test] public void MonsterEditorIncludesTemporaryAndPermanentImmunityAndBurnOwner()
        {
            var edit = TestMonsterSettings.Read(monster); edit.FreezeImmune = true; edit.KnockbackConsumed = true;
            edit.FreezeImmunityPending = true; edit.Burn = 4; edit.BurnSourceTowerId = tower.InstanceId;
            Assert.That(edit.Apply(monster, 36, out _), Is.True);
            Assert.That(monster.CanReceiveFreeze, Is.False); Assert.That(monster.KnockbackConsumed, Is.True);
            Assert.That(monster.BurnSourceTowerIds.All(id => id == tower.InstanceId), Is.True);
        }
        [Test] public void ClearingStatusesPreservesInnateImmunityHealthAndPosition()
        {
            monster.StatusImmunities.Add("knockback"); monster.BurnStacks = 5; monster.FreezeImmunityPending = true;
            monster.DamageStoreMarked = true; monster.StoredDamage = 50; monster.NextMoveReduction = 2;
            TestMonsterSettings.ClearStatuses(monster);
            Assert.That(monster.IsImmuneTo("knockback"), Is.True); Assert.That(monster.CurrentHealth, Is.EqualTo(10000));
            Assert.That(monster.CurrentTileIndex, Is.EqualTo(5)); Assert.That(monster.BurnStacks, Is.Zero);
            Assert.That(monster.DamageStoreMarked || monster.FreezeImmunityPending, Is.False);
        }
        [Test] public void EditingHealthDoesNotReplaceMixedBurnOwnership()
        {
            monster.BurnStacks = 2; monster.BurnSourceTowerId = 9;
            monster.BurnSourceTowerIds[0] = 7; monster.BurnSourceTowerIds[1] = 9;
            var edit = TestMonsterSettings.Read(monster); edit.Health = 1234;
            Assert.That(edit.Apply(monster, 36, out _), Is.True);
            Assert.That(monster.BurnSourceTowerIds, Is.EqualTo(new[] {7, 9}));
        }
        [Test] public void InvalidTowerCounterDoesNotPartiallyApplyFeather()
        {
            var edit = TestTowerSettings.Read(tower); edit.FeatherSealed = true; edit.Charge = 1;
            Assert.That(edit.Apply(tower, out _), Is.False); Assert.That(tower.IsFeatherSealed, Is.False);
        }
        [Test] public void TowerEditorCanSetCooldownChargeHeatAndResetCounters()
        {
            Effect("charge_damage", 100); Effect("charge_cap", 3); Effect("overheat_cycle", 2);
            var edit = TestTowerSettings.Read(tower); edit.Charge = 3; edit.Overheat = 2; edit.Cooldown = 4; edit.FeatherSealed = true;
            Assert.That(edit.Apply(tower, out _), Is.True); Assert.That(tower.ChargeCount, Is.EqualTo(3));
            TestTowerSettings.ClearCounters(tower);
            Assert.That(tower.ChargeCount + tower.OverheatAttackTurns + tower.AttackCooldownRounds, Is.Zero);
            Assert.That(tower.IsFeatherSealed, Is.False); Assert.That(tower.HasEffect("charge_damage"), Is.True);
        }
    }
}
