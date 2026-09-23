using System;
using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using GaeBullBing.Core.Data;
using GaeBullBing.Presentation.Game;
using UnityEditor;
using UnityEngine;

namespace GaeBullBing.Editor
{
    internal static class TowerEffectRuleVerifier
    {
        [MenuItem("GaeBullBing/Tests/Verify Tower Effect Rules")]
        private static void Verify()
        {
            VerifyBurnConditionUsesPreAttackState();
            VerifyBurnExplosionAtConfiguredThreshold();
            VerifyFrostbiteBurst();
            VerifyStatusTransferAddsStacksAndTriggersFrostbite();
            VerifyEffectDamageDoesNotRepeatOnHit();
            VerifyMultiHitResolvesEffectsBetweenAttacks();
            Debug.Log("[TowerEffectRuleVerifier] All 6 tower effect rule checks passed.");
        }

        [MenuItem("GaeBullBing/Tests/Verify Test Lab Runtime")]
        private static void VerifyTestLabRuntime()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Open TestLab and enter Play Mode first.");
            var game = UnityEngine.Object.FindFirstObjectByType<GameController>();
            Require(game != null && game.State != null, "Running GameController was not found.");

            var upgradeIds = new[] { "UPG_FIRE_T2_00", "UPG_FIRE_T3_02" };
            Require(game.TestLabConfigureTower(4, "TOW_01", upgradeIds, false, out var towerMessage),
                towerMessage);
            Require(game.TestLabSpawnMonster("MON_001", 6, true,
                out var primaryId, out var primaryMessage), primaryMessage);
            Require(game.TestLabSpawnMonster("MON_001", 7, true,
                out var adjacentId, out var adjacentMessage), adjacentMessage);
            var primary = game.State.Monsters.Find(monster => monster.InstanceId == primaryId);
            var adjacent = game.State.Monsters.Find(monster => monster.InstanceId == adjacentId);
            Require(primary != null && adjacent != null, "Test monsters were not created.");
            primary.BurnStacks = 20;
            var adjacentHealth = adjacent.CurrentHealth;

            var attacks = game.Session.ResolveTestTowerAttack(
                4, 1, primaryId, true,
                game.TestTowerDefinitions, game.TestTowerUpgradeDefinitions);
            Require(attacks.Count > 0, "Immediate tower attack produced no results.");
            Require(adjacent.CurrentHealth < adjacentHealth,
                "Forced-target burn explosion did not damage the adjacent monster.");

            Require(game.SetTileEffectFromConsole(8, "ignite", out var tileMessage), tileMessage);
            Require(game.State.Board.Tiles[8].FireTurnsRemaining > 0,
                "Direct fire-field placement did not update the tile.");

            var diceDatabase = Resources.Load<DiceDatabaseDefinition>("GaeBullBing/DiceDatabase");
            Require(diceDatabase != null && diceDatabase.Dice.Length >= 2,
                "Dice database is unavailable.");
            var diceIds = new List<string>();
            for (var index = 0; index < Math.Min(4, diceDatabase.Dice.Length); index++)
                diceIds.Add(diceDatabase.Dice[index].Id);
            Require(game.TestLabSetDice(diceIds[0], diceIds[1], out var diceMessage), diceMessage);
            Require(game.State.Dice.Count == 2, "Two equipped dice were not applied.");

            Debug.Log("[TowerEffectRuleVerifier] Test Lab runtime smoke check passed.");
        }

        private static void VerifyBurnConditionUsesPreAttackState()
        {
            var setup = CreateSetup(TowerEffectCatalog.Burn, TowerEffectCatalog.BurnExplode);
            setup.Tower.EffectValues[TowerEffectCatalog.Burn] = 1;
            setup.Tower.EffectValues[TowerEffectCatalog.BurnExplode] = 20;
            var target = AddMonster(setup.State, 1, 5, 100, burn: 19);
            var adjacent = AddMonster(setup.State, 2, 6, 100);

            Resolve(setup, target, preBurn: 19);

            Require(target.BurnStacks == 20, "19-stack target must become 20 after the hit.");
            Require(Math.Abs(adjacent.CurrentHealth - 100f) < .001f,
                "Burn explosion must not trigger when pre-attack burn is below 20.");
        }

        private static void VerifyBurnExplosionAtConfiguredThreshold()
        {
            var setup = CreateSetup(TowerEffectCatalog.Burn, TowerEffectCatalog.BurnExplode);
            setup.Tower.EffectValues[TowerEffectCatalog.Burn] = 1;
            setup.Tower.EffectValues[TowerEffectCatalog.BurnExplode] = 20;
            var target = AddMonster(setup.State, 1, 5, 100, burn: 20);
            var adjacent = AddMonster(setup.State, 2, 6, 100);

            Resolve(setup, target, preBurn: 20);

            Require(Math.Abs(adjacent.CurrentHealth - 90f) < .001f,
                "Burn explosion must damage adjacent tiles at the configured threshold.");
        }

        private static void VerifyFrostbiteBurst()
        {
            var setup = CreateSetup(
                TowerEffectCatalog.Frostbite,
                TowerEffectCatalog.FrostbiteBurstThreshold,
                TowerEffectCatalog.FrostbiteBurstDamageMultiply);
            setup.Tower.LastResolvedDamage = 15;
            setup.Tower.EffectValues[TowerEffectCatalog.Frostbite] = 1;
            setup.Tower.EffectValues[TowerEffectCatalog.FrostbiteBurstThreshold] = 4;
            setup.Tower.EffectValues[TowerEffectCatalog.FrostbiteBurstDamageMultiply] = 2;
            var target = AddMonster(setup.State, 1, 5, 100, frostbite: 3);

            Resolve(setup, target, preFrostbite: 3);

            Require(target.FrostbiteStacks == 0, "Frostbite must reset after reaching its threshold.");
            Require(Math.Abs(target.CurrentHealth - 70f) < .001f,
                "Frostbite burst must use the configured threshold and multiplier.");
        }

        private static void VerifyStatusTransferAddsStacksAndTriggersFrostbite()
        {
            var setup = CreateSetup(
                TowerEffectCatalog.StatusTransfer,
                TowerEffectCatalog.FrostbiteBurstThreshold,
                TowerEffectCatalog.FrostbiteBurstDamageMultiply);
            setup.Tower.LastResolvedDamage = 18;
            setup.Tower.EffectValues[TowerEffectCatalog.StatusTransfer] = 1;
            setup.Tower.EffectValues[TowerEffectCatalog.FrostbiteBurstThreshold] = 3;
            setup.Tower.EffectValues[TowerEffectCatalog.FrostbiteBurstDamageMultiply] = 3;
            var source = AddMonster(setup.State, 1, 5, 100, burn: 5, frostbite: 3);
            source.FrostbiteBurstThreshold = 4;
            source.FrostbiteBurstDamageMultiplier = 2;
            var destination = AddMonster(setup.State, 2, 6, 100, burn: 1, frostbite: 1);

            Resolve(setup, source, preBurn: 5, preFrostbite: 3);

            Require(destination.BurnStacks == 6,
                "Status transfer must add the source's current burn stacks.");
            Require(destination.FrostbiteStacks == 0,
                "Transferred frostbite must burst at the source status's configured threshold.");
            Require(Math.Abs(destination.CurrentHealth - 64f) < .001f,
                "Transferred frostbite must preserve its 200% rule but use the electric tower's damage.");
        }

        private static void VerifyEffectDamageDoesNotRepeatOnHit()
        {
            var setup = CreateSetup(TowerEffectCatalog.Burn, TowerEffectCatalog.BurnDamage);
            setup.Tower.EffectValues[TowerEffectCatalog.Burn] = 1;
            setup.Tower.EffectValues[TowerEffectCatalog.BurnDamage] = .1f;
            var target = AddMonster(setup.State, 1, 5, 100, burn: 2);

            Resolve(setup, target, preBurn: 2);

            Require(target.BurnStacks == 3,
                "Conditional effect damage must not apply burn a second time.");
            Require(Math.Abs(target.CurrentHealth - 98f) < .001f,
                "Two pre-attack burn stacks must deal exactly 20% of 10 tower damage.");
        }

        private static void VerifyMultiHitResolvesEffectsBetweenAttacks()
        {
            var setup = CreateSetup(TowerEffectCatalog.Burn, TowerEffectCatalog.BurnExplode);
            setup.Tower.EffectValues[TowerEffectCatalog.Burn] = 1;
            setup.Tower.EffectValues[TowerEffectCatalog.BurnExplode] = 20;
            var target = AddMonster(setup.State, 1, 1, 200, burn: 19);
            var adjacent = AddMonster(setup.State, 2, 2, 100);
            var combat = new TowerCombatService();
            var stats = new Dictionary<int, TowerCombatStats>
            {
                [setup.Tower.InstanceId] = new TowerCombatStats(10, 3, 1, 2)
            };

            combat.ResolveByTower(setup.State, stats,
                attacks => setup.Service.ResolveAfterAttacks(setup.State, attacks));

            Require(target.BurnStacks == 20,
                "A multi-hit fire tower must apply its persistent effect after each attack.");
            Require(Math.Abs(adjacent.CurrentHealth - 90f) < .001f,
                "The second hit must see the first hit's 20 burn stacks and trigger one explosion.");
        }

        private static Setup CreateSetup(params string[] effects)
        {
            var state = new GameState();
            for (var index = 0; index < BoardState.DefaultTileCount; index++)
                state.Board.Tiles.Add(new TileState { Index = index });
            var tower = new TowerState
            {
                InstanceId = 100,
                DefinitionId = "TEST",
                LastResolvedDamage = 10
            };
            foreach (var effect in effects) tower.AppliedEffectIds.Add(effect);
            state.Board.Tiles[0].Tower = tower;
            return new Setup(state, tower, new TowerEffectService());
        }

        private static MonsterState AddMonster(
            GameState state, int id, int tile, float health, int burn = 0, int frostbite = 0)
        {
            var monster = new MonsterState
            {
                InstanceId = id,
                CurrentTileIndex = tile,
                CurrentHealth = health,
                MaxHealth = health,
                BurnStacks = burn,
                FrostbiteStacks = frostbite
            };
            state.Monsters.Add(monster);
            return monster;
        }

        private static void Resolve(
            Setup setup, MonsterState target, int preBurn = 0, int preFrostbite = 0)
        {
            setup.Service.ResolveAfterAttacks(setup.State, new[]
            {
                new TowerAttackResult(
                    setup.Tower.InstanceId,
                    target.InstanceId,
                    0,
                    false,
                    targetTileIndex: target.CurrentTileIndex,
                    visualKind: TowerAttackVisualKind.Projectile,
                    preAttackBurnStacks: preBurn,
                    preAttackFrostbiteStacks: preFrostbite)
            });
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private readonly struct Setup
        {
            public Setup(GameState state, TowerState tower, TowerEffectService service)
            {
                State = state;
                Tower = tower;
                Service = service;
            }

            public GameState State { get; }
            public TowerState Tower { get; }
            public TowerEffectService Service { get; }
        }
    }
}
