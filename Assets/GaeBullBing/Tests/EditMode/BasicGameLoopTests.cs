using GaeBullBing.Core;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Dice;
using GaeBullBing.Core.Game;
using NUnit.Framework;
using UnityEngine;

namespace GaeBullBing.Tests.EditMode
{
    public sealed class BasicGameLoopTests
    {
        [Test]
        public void BoardService_CreatesThirtySixLoopTiles()
        {
            var board = new BoardState();

            new BoardService().Initialize(board);

            Assert.That(board.TileCount, Is.EqualTo(36));
            Assert.That(board.Tiles[0].DefinitionId, Is.EqualTo("start"));
            Assert.That(board.Tiles[35].Index, Is.EqualTo(35));
        }

        [Test]
        public void BoardService_DisablesTowerBuildingOnFourCorners()
        {
            var board = new BoardState();
            new BoardService().Initialize(board);

            Assert.That(board.Tiles[0].CanBuildTower, Is.False);
            Assert.That(board.Tiles[9].CanBuildTower, Is.False);
            Assert.That(board.Tiles[18].CanBuildTower, Is.False);
            Assert.That(board.Tiles[27].CanBuildTower, Is.False);
            Assert.That(board.Tiles[1].CanBuildTower, Is.True);
            Assert.That(board.Tiles[10].CanBuildTower, Is.True);
        }

        [Test]
        public void BoardLayout_CreatesTenByTenSquareBorderWithThirtySixUniqueCells()
        {
            Assert.That(BoardLayout.Cells, Has.Count.EqualTo(36));
            Assert.That(BoardLayout.GetCell(0), Is.EqualTo(new BoardCell(0, 0)));
            Assert.That(BoardLayout.GetCell(9), Is.EqualTo(new BoardCell(0, 9)));
            Assert.That(BoardLayout.GetCell(18), Is.EqualTo(new BoardCell(9, 9)));
            Assert.That(BoardLayout.GetCell(27), Is.EqualTo(new BoardCell(9, 0)));
            Assert.That(new System.Collections.Generic.HashSet<BoardCell>(BoardLayout.Cells), Has.Count.EqualTo(36));
        }

        [Test]
        public void BoardLayout_EveryConsecutiveCellIsAdjacentIncludingLoopEnd()
        {
            for (var index = 0; index < BoardLayout.Cells.Count; index++)
            {
                var current = BoardLayout.Cells[index];
                var next = BoardLayout.Cells[(index + 1) % BoardLayout.Cells.Count];
                var distance = System.Math.Abs(current.X - next.X) + System.Math.Abs(current.Y - next.Y);
                Assert.That(distance, Is.EqualTo(1), $"Cells {index} and {(index + 1) % 36} must be adjacent.");
            }
        }

        [Test]
        public void PlayerMovement_WrapsAroundBoard()
        {
            var board = new BoardState();
            new BoardService().Initialize(board);
            var player = new PlayerState { CurrentTileIndex = 32 };

            var destination = new PlayerMovementService().Move(player, board, 7);

            Assert.That(destination, Is.EqualTo(3));
        }

        [Test]
        public void WeightedDiceRoller_UsesConfiguredWeights()
        {
            var dice = new DiceState(
                new[] { 1, 2, 3 },
                new[] { 0, 0, 5 });

            var result = new WeightedDiceRoller(new FixedRandom(0)).Roll(dice);

            Assert.That(result, Is.EqualTo(3));
        }

[Test]
        public void DiceInventory_StartsWithDefaultDiceEquippedAndChangingSlotDoesNotDeleteDice()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();

            Assert.That(state.Dice[0].Id, Is.EqualTo("DICE_WHITE"));
            Assert.That(state.Dice[1].Id, Is.EqualTo("DICE_BLACK"));
            Assert.That(state.DiceInventory.Dice[0].Id, Is.EqualTo("DICE_WHITE"));
            Assert.That(state.DiceInventory.Dice[1].Id, Is.EqualTo("DICE_BLACK"));
            Assert.That(session.CanRollDice, Is.True);

            var reward = GaeBullBing.Core.Dice.DiceCatalog.GetReward(0);
            Assert.That(session.StoreDiceReward(reward), Is.True);
            Assert.That(session.QueueDiceEquip(0, 2), Is.True);
            Assert.That(state.Dice[0].Id, Is.EqualTo(reward.Id));

            Assert.That(session.QueueDiceEquip(0, 0), Is.True);
            session.CompleteRound();

            Assert.That(state.Dice[0].Id, Is.EqualTo("DICE_WHITE"));
            Assert.That(state.Dice[1].Id, Is.EqualTo("DICE_BLACK"));
            Assert.That(state.DiceInventory.Dice[0].Id, Is.EqualTo("DICE_WHITE"));
            Assert.That(state.DiceInventory.Dice[1].Id, Is.EqualTo("DICE_BLACK"));
            Assert.That(state.DiceInventory.Dice[2].Id, Is.EqualTo(reward.Id));
        }

        [Test]
        public void DiceCatalog_LoadsJsonGeneratedNameFacesColorAndGrade()
        {
            GaeBullBing.Core.Dice.DiceCatalog.ClearCache();

            var red = GaeBullBing.Core.Dice.DiceCatalog.GetById("DICE_RED");

            Assert.That(red, Is.Not.Null);
            Assert.That(red.DisplayName, Is.EqualTo("빨간색 주사위"));
            Assert.That(red.Faces, Is.EqualTo(new[] { 2, 2, 4, 4, 6, 6 }));
            Assert.That(red.Grade, Is.EqualTo(GaeBullBing.Core.Data.DiceGrade.Rare));
            Assert.That(red.Red, Is.EqualTo(0xE6 / 255f).Within(.0001f));
            Assert.That(red.Green, Is.EqualTo(0x39 / 255f).Within(.0001f));
            Assert.That(red.Blue, Is.EqualTo(0x46 / 255f).Within(.0001f));
        }

        [Test]
        public void DiceReward_SelectsWeightedGradeThenUniformEntryWithinGrade()
        {
            GaeBullBing.Core.Dice.DiceCatalog.ClearCache();

            var rareFirst = GaeBullBing.Core.Dice.DiceCatalog.GetRewardForRoll(0, .1f, 0);
            var rareSecond = GaeBullBing.Core.Dice.DiceCatalog.GetRewardForRoll(0, .1f, 1);
            var epic = GaeBullBing.Core.Dice.DiceCatalog.GetRewardForRoll(0, .8f, 0);
            var legendary = GaeBullBing.Core.Dice.DiceCatalog.GetRewardForRoll(0, .98f, 0);

            Assert.That(rareFirst.Id, Is.EqualTo("DICE_RED"));
            Assert.That(rareSecond.Id, Is.EqualTo("DICE_BLUE"));
            Assert.That(epic.Id, Is.EqualTo("DICE_ROSE"));
            Assert.That(legendary.Id, Is.EqualTo("DICE_RUBY"));
        }

        [Test]
        public void UpgradeRuntimeDatabase_IncludesJsonEntriesBeyondLegacySceneArray()
        {
            var database = Resources.Load<GaeBullBing.Core.Data.TowerUpgradeDatabaseDefinition>(
                "GaeBullBing/TowerUpgradeDatabase");

            Assert.That(database, Is.Not.Null);
            Assert.That(database.Upgrades, Has.Length.GreaterThan(32));
            Assert.That(System.Array.Exists(database.Upgrades,
                upgrade => upgrade != null && upgrade.Id == "UPG_FIRE_T3_03"), Is.True);
        }


        [Test]
        public void DiceInventory_HoldsFourDiceAndRejectsAFifth()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();

            Assert.That(session.StoreDiceReward(DiceCatalog.GetReward(0)), Is.True);
            Assert.That(session.StoreDiceReward(DiceCatalog.GetReward(1)), Is.True);
            Assert.That(state.DiceInventory.Dice, Has.Count.EqualTo(4));
            Assert.That(session.StoreDiceReward(DiceCatalog.GetReward(2)), Is.False);
        }

        [Test]
        public void GameSession_RollsTwoDiceAndMovesPlayer()
        {
            var state = new GameState();
            var session = new GameSession(
                state,
                new BoardService(),
                new PlayerMovementService(),
                new WeightedDiceRoller(new FixedRandom(0)),
                new GaeBullBing.Core.Monsters.MonsterService(),
                new GaeBullBing.Core.Towers.TowerService(),
                new GaeBullBing.Core.Towers.TowerCombatService());
            session.StartNewGame();
            session.QueueDiceEquip(0, 0);
            session.QueueDiceEquip(1, 1);

            var distance = session.RollDiceAndMovePlayer();

            Assert.That(distance, Is.EqualTo(2));
            Assert.That(state.LastDiceResults, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(state.Player.CurrentTileIndex, Is.EqualTo(2));
            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.TileResolve));
        }

        [Test]
        public void GameSession_AlwaysAddsFivePercentDamageOnLapCompletion()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            session.QueueDiceEquip(0, 0);
            session.QueueDiceEquip(1, 1);
            state.Player.CurrentTileIndex = 35;
            session.SetNextDiceResults(1, 1);

            session.RollDiceAndMovePlayer();

            Assert.That(state.PermanentAllTowerDamageRateBonus, Is.EqualTo(.05f));
        }

        [Test]
        public void MonsterService_MovesMonsterByTurnDistance()
        {
            var state = new GameState();
            state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 1,
                CurrentHealth = 30,
                CurrentTileIndex = 0,
                MoveDistance = 2
            });

            var results = new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state);

            Assert.That(results[0].Distance, Is.EqualTo(2));
            Assert.That(state.Monsters[0].CurrentTileIndex, Is.EqualTo(2));
            Assert.That(state.Monsters[0].DistanceTravelled, Is.EqualTo(2));
        }

        [Test]
        public void MonsterService_IceEnteredMidMoveLeavesOnlyOneRemainingStep()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[2].IceTurnsRemaining = 1;
            state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 1, CurrentHealth = 30, CurrentTileIndex = 0, MoveDistance = 5
            });

            var result = new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state)[0];

            Assert.That(result.Distance, Is.EqualTo(3));
            Assert.That(state.Monsters[0].CurrentTileIndex, Is.EqualTo(3));
        }

        [Test]
        public void MonsterService_RemovingIceRestoresBaseMovement()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[0].IceTurnsRemaining = 1;
            state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 1, CurrentHealth = 30, CurrentTileIndex = 0, MoveDistance = 4
            });
            var service = new GaeBullBing.Core.Monsters.MonsterService();
            Assert.That(service.MoveAll(state)[0].Distance, Is.EqualTo(1));

            state.Board.Tiles[1].IceTurnsRemaining = 0;
            Assert.That(service.MoveAll(state)[0].Distance, Is.EqualTo(4));
        }

        [Test]
        public void MonsterService_FireCrossedMidMoveAddsBurnStack()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[2].FireTurnsRemaining = 1;
            state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 1, CurrentHealth = 30, CurrentTileIndex = 0, MoveDistance = 5
            });

            new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state);

            Assert.That(state.Monsters[0].CurrentTileIndex, Is.EqualTo(5));
            Assert.That(state.Monsters[0].BurnStacks, Is.EqualTo(1));
        }

        [Test]
        public void MonsterState_BurnStacksAreClampedBetweenZeroAndTen()
        {
            var monster = new GaeBullBing.Core.Monsters.MonsterState { BurnStacks = 25 };
            Assert.That(monster.BurnStacks, Is.EqualTo(10));

            monster.BurnStacks = -3;
            Assert.That(monster.BurnStacks, Is.EqualTo(0));
        }

        [Test]
        public void MonsterState_ReceiveDamageUsesPatternDefensePerLevel()
        {
            var difficulty = new GaeBullBing.Core.Monsters.DifficultyState
            {
                Level = 6,
                DefensePerLevel = 20f
            };
            var monster = new GaeBullBing.Core.Monsters.MonsterState
            {
                CurrentHealth = 100f,
                MaxHealth = 100f,
                BaseDefense = 0f
            };

            var actualDamage = monster.ReceiveDamage(100f, difficulty);

            Assert.That(monster.GetDefense(difficulty), Is.EqualTo(100f));
            Assert.That(actualDamage, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(monster.CurrentHealth, Is.EqualTo(50f).Within(0.0001f));
        }

        [Test]
        public void MonsterState_ReceiveDamageCombinesBaseDefenseAndShock()
        {
            var difficulty = new GaeBullBing.Core.Monsters.DifficultyState
            {
                Level = 1,
                DefensePerLevel = 20f
            };
            var monster = new GaeBullBing.Core.Monsters.MonsterState
            {
                CurrentHealth = 200f,
                MaxHealth = 200f,
                BaseDefense = 20f,
                Shocked = true
            };

            var actualDamage = monster.ReceiveDamage(120f, difficulty);

            Assert.That(actualDamage, Is.EqualTo(130f).Within(0.0001f));
            Assert.That(monster.CurrentHealth, Is.EqualTo(70f).Within(0.0001f));
        }

        [Test]
        public void MonsterService_RemovesMonsterAfterFullLap()
        {
            var state = new GameState();
            state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 1,
                CurrentHealth = 30,
                CurrentTileIndex = 35,
                MoveDistance = 2,
                DistanceTravelled = 35
            });

            var results = new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state);

            Assert.That(results[0].Distance, Is.EqualTo(1));
            Assert.That(results[0].ReachedBase, Is.True);
            Assert.That(state.Monsters, Is.Empty);
        }

        [Test]
        public void DifficultyService_FinalPatternRepeatsWhileLevelAndHealthKeepIncreasing()
        {
            var patterns = new[]
            {
                new GaeBullBing.Core.Monsters.DifficultyPatternData
                {
                    RequiredKills = 0,
                    HealthMultiplier = 1f,
                    MonsterIds = new[] { "MON_001" }
                },
                new GaeBullBing.Core.Monsters.DifficultyPatternData
                {
                    RequiredKills = 10,
                    HealthMultiplier = 1.5f,
                    MonsterIds = new[] { "MON_002", "MON_003" }
                }
            };
            var service = new GaeBullBing.Core.Monsters.DifficultyService(patterns, 10, 1.5f);
            var state = new GaeBullBing.Core.Monsters.DifficultyState();
            service.Reset(state);

            service.AddKills(state, 10);
            Assert.That(service.GetNextMonsterId(state), Is.EqualTo("MON_002"));

            service.AddKills(state, 10);

            Assert.That(state.Level, Is.EqualTo(3));
            Assert.That(service.GetHealthMultiplier(state), Is.EqualTo(2.25f).Within(0.0001f));
            Assert.That(service.GetNextMonsterId(state), Is.EqualTo("MON_003"));
            Assert.That(service.GetRemainingKills(state), Is.EqualTo(10));
        }

        [Test]
        public void GameSession_EndsGameWhenFiveMonstersReachStartTile()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            for (var id = 1; id <= 5; id++)
                state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
                {
                    InstanceId = id,
                    CurrentHealth = 1,
                    CurrentTileIndex = 35,
                    MoveDistance = 1,
                    DistanceTravelled = 35
                });

            session.MoveMonsters();

            Assert.That(state.EscapedMonsterCount, Is.EqualTo(5));
            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.Defeat));
            Assert.That(state.Monsters, Is.Empty);
        }

        [Test]
        public void TowerService_BuildsOnlyTheTowerAssignedToTile()
        {
            var tile = new TileState { Index = 3, BuildTowerDefinitionId = "TOW_01" };
            var service = new GaeBullBing.Core.Towers.TowerService();

            var tower = service.Build(tile, "TOW_01");

            Assert.That(tile.HasTower, Is.True);
            Assert.That(tower.DefinitionId, Is.EqualTo("TOW_01"));
            Assert.That(tower.InstanceId, Is.GreaterThan(0));
        }

        [Test]
        public void TowerService_UpgradeAddsModifierWithoutReplacingBaseTower()
        {
            var tile = new TileState { Index = 3, BuildTowerDefinitionId = "TOW_01" };
            var service = new GaeBullBing.Core.Towers.TowerService();
            var tower = service.Build(tile, "TOW_01");

            service.Upgrade(tile, "UPG_FIRE_02", 2);

            Assert.That(tile.Tower, Is.SameAs(tower));
            Assert.That(tile.Tower.DefinitionId, Is.EqualTo("TOW_01"));
            Assert.That(tile.Tower.UpgradeTier, Is.EqualTo(2));
            Assert.That(tile.Tower.AppliedUpgradeIds, Contains.Item("UPG_FIRE_02"));
        }

        [Test]
        public void GameSession_ElementFlatDamageBonusAffectsMatchingTowerCombat()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Board.Tiles[5].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_01"
            };
            state.Monsters.Add(CreateMonster(1, 5, 100, 5));

            var definition = ScriptableObject.CreateInstance<GaeBullBing.Core.Data.TowerDefinition>();
            SetPrivateField(definition, "id", "TOW_01");
            SetPrivateField(definition, "element", TowerElement.Fire);
            SetPrivateField(definition, "damage", 10);
            SetPrivateField(definition, "range", 3);
            SetPrivateField(definition, "targetCount", 1);
            SetPrivateField(definition, "attackCount", 1);
            session.AddPermanentTowerDamageFlatBonus(TowerElement.Fire, 30);

            var results = session.ResolveTowerCombat(
                new[] { definition },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());

            Assert.That(results[0].Damage, Is.EqualTo(40));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void TowerDamage_AddsUpgradeAndMapMultipliersBeforeFlatBonuses()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Board.Tiles[5].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_01"
            };
            state.Board.Tiles[5].Tower.AppliedUpgradeIds.Add("UPG_TEST_DAMAGE_ORDER");
            state.Monsters.Add(CreateMonster(1, 5, 100, 5));

            var definition = ScriptableObject.CreateInstance<GaeBullBing.Core.Data.TowerDefinition>();
            SetPrivateField(definition, "id", "TOW_01");
            SetPrivateField(definition, "element", TowerElement.Fire);
            SetPrivateField(definition, "damage", 10);
            SetPrivateField(definition, "range", 3);
            SetPrivateField(definition, "targetCount", 1);
            SetPrivateField(definition, "attackCount", 1);

            var upgrade = ScriptableObject.CreateInstance<GaeBullBing.Core.Data.TowerUpgradeDefinition>();
            SetPrivateField(upgrade, "id", "UPG_TEST_DAMAGE_ORDER");
            SetPrivateField(upgrade, "statModifiers", new[]
            {
                new GaeBullBing.Core.Data.TowerStatModifier { Stat = "damage", Operation = "Multiply", Value = 2f },
                new GaeBullBing.Core.Data.TowerStatModifier { Stat = "damage", Operation = "Add", Value = 5f }
            });
            session.AddPermanentTowerDamageRateBonus(TowerElement.Fire, .5f);
            session.AddPermanentTowerDamageFlatBonus(TowerElement.Fire, 3);

            var results = session.ResolveTowerCombat(new[] { definition }, new[] { upgrade });

            Assert.That(results[0].Damage, Is.EqualTo(40));
            Object.DestroyImmediate(upgrade);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void TowerCombat_PrioritizesExistingTargetBeforeCloserMonster()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.TargetInstanceIds.Add(2);
            state.Monsters.Add(CreateMonster(1, 4, 10, 4));
            state.Monsters.Add(CreateMonster(2, 8, 10, 8));

            var results = ResolveCombat(state, 10, 3, 1);

            Assert.That(results[0].TargetInstanceId, Is.EqualTo(2));
        }

        [Test]
        public void TowerCombat_PrioritizesGoalProgressBeforeHealth()
        {
            var state = CreateCombatState();
            state.Monsters.Add(CreateMonster(1, 4, 10, 4));
            state.Monsters.Add(CreateMonster(2, 7, 30, 7));

            var results = ResolveCombat(state, 5, 3, 1);

            Assert.That(results[0].TargetInstanceId, Is.EqualTo(2));
        }

        [Test]
        public void TowerCombat_PrioritizesHigherHealthAtEqualDistance()
        {
            var state = CreateCombatState();
            state.Monsters.Add(CreateMonster(1, 4, 10, 4));
            state.Monsters.Add(CreateMonster(2, 6, 30, 6));

            var results = ResolveCombat(state, 5, 3, 1);

            Assert.That(results[0].TargetInstanceId, Is.EqualTo(2));
        }

        [Test]
        public void TowerCombat_ResolvesRearTowerBeforeFrontTower()
        {
            var state = CreateCombatState();
            state.Board.Tiles[5].Tower = null;
            state.Board.Tiles[3].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_01"
            };
            state.Board.Tiles[7].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 2,
                DefinitionId = "TOW_01"
            };
            state.Monsters.Add(CreateMonster(1, 5, 10, 5));

            var results = ResolveCombat(state, 10, 2, 1);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].TowerInstanceId, Is.EqualTo(2));
        }

        [Test]
        public void GameSession_ResolvesRearTowerSecondaryDamageBeforeFrontTowerTargets()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Board.Tiles[3].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_FRONT"
            };
            state.Board.Tiles[7].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 2,
                DefinitionId = "TOW_REAR"
            };
            state.Board.Tiles[7].Tower.AppliedEffectIds.Add("explode");
            state.Monsters.Add(CreateMonster(1, 6, 10, 6));
            var front = CreateTowerDefinition("TOW_FRONT", 10, 10);
            var rear = CreateTowerDefinition("TOW_REAR", 10, 10);

            var results = session.ResolveTowerCombat(
                new[] { front, rear },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());

            Assert.That(state.Monsters, Is.Empty);
            foreach (var result in results)
                Assert.That(result.TowerInstanceId, Is.Not.EqualTo(1));
            Object.DestroyImmediate(rear);
            Object.DestroyImmediate(front);
        }

        [Test]
        public void TowerCombat_DamagesFrozenMonsterNormally()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Board.Tiles[5].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_01"
            };
            var monster = CreateMonster(1, 5, 100, 5);
            monster.FrozenMovesRemaining = 1;
            state.Monsters.Add(monster);
            var tower = CreateTowerDefinition("TOW_01", 10, 1);

            session.ResolveTowerCombat(
                new[] { tower },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());

            Assert.That(monster.CurrentHealth, Is.EqualTo(90f));
            Assert.That(monster.FrozenMovesRemaining, Is.EqualTo(1));
            Object.DestroyImmediate(tower);
        }

        [Test]
        public void ElectricTransferRangeUpgradeOnlyExpandsBuiltInTileFieldTransfer()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_04"
            };
            tower.AppliedEffectIds.Add("spread_debuff");
            tower.AppliedEffectIds.Add("chain_tile");
            tower.AppliedEffectIds.Add("spread_range_add");
            tower.EffectValues["spread_range_add"] = 1f;

            state.Board.Tiles[5].Tower = tower;
            state.Board.Tiles[5].FireTurnsRemaining = TileState.OneTurnEffectDuration;

            var target = CreateMonster(1, 5, 100, 5);
            target.Shocked = true;
            var adjacent = CreateMonster(2, 6, 100, 6);
            var twoTilesAway = CreateMonster(3, 7, 100, 7);
            var fourTilesAway = CreateMonster(4, 9, 100, 9);
            state.Monsters.Add(target);
            state.Monsters.Add(adjacent);
            state.Monsters.Add(twoTilesAway);
            state.Monsters.Add(fourTilesAway);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(
                state,
                new[]
                {
                    new GaeBullBing.Core.Towers.TowerAttackResult(1, 1, 10f, false, targetTileIndex: 5)
                });

            Assert.That(state.Board.Tiles[3].FireTurnsRemaining, Is.GreaterThan(0));
            Assert.That(state.Board.Tiles[7].FireTurnsRemaining, Is.GreaterThan(0));
            Assert.That(adjacent.Shocked, Is.True);
            Assert.That(twoTilesAway.Shocked, Is.False);
            Assert.That(fourTilesAway.CurrentHealth, Is.EqualTo(100f));
        }

[Test]
        public void ElectricAttack_RefreshesSourceFieldAndSpreadsToBothSides()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var tower = new GaeBullBing.Core.Towers.TowerState { InstanceId = 1, DefinitionId = "TOW_04" };
            state.Board.Tiles[8].Tower = tower;
            state.Board.Tiles[4].FireTurnsRemaining = 1;
            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 10f, false, targetTileIndex: 4) });
            Assert.That(state.Board.Tiles[3].FireTurnsRemaining, Is.EqualTo(TileState.OneTurnEffectDuration));
            Assert.That(state.Board.Tiles[4].FireTurnsRemaining, Is.EqualTo(TileState.OneTurnEffectDuration));
            Assert.That(state.Board.Tiles[5].FireTurnsRemaining, Is.EqualTo(TileState.OneTurnEffectDuration));
        }

[Test]
        public void ChainTile_FieldTransferCascadesAtEachSequentialTile()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var tower = new GaeBullBing.Core.Towers.TowerState { InstanceId = 1, DefinitionId = "TOW_04" };
            tower.AppliedEffectIds.Add("chain_tile");
            tower.EffectValues["chain_tile"] = 3f;
            state.Board.Tiles[8].Tower = tower;
            state.Board.Tiles[4].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 10f, false, targetTileIndex: 4) });
            for (var tileIndex = 0; tileIndex <= 5; tileIndex++)
                Assert.That(state.Board.Tiles[tileIndex].FireTurnsRemaining, Is.GreaterThan(0), $"tile {tileIndex}");
            Assert.That(state.Board.Tiles[35].FireTurnsRemaining, Is.Zero);
        }

[Test]
        public void ChainLine_FieldTransferUsesInitialSourcesOnlyOnce()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var tower = new GaeBullBing.Core.Towers.TowerState { InstanceId = 1, DefinitionId = "TOW_04" };
            tower.AppliedEffectIds.Add("chain_line");
            state.Board.Tiles[5].Tower = tower;
            state.Board.Tiles[1].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            state.Board.Tiles[4].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            var attacks = new[]
            {
                new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 10f, false, targetTileIndex: 1, visualKind: GaeBullBing.Core.Towers.TowerAttackVisualKind.ChainLine),
                new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 10f, false, targetTileIndex: 4, visualKind: GaeBullBing.Core.Towers.TowerAttackVisualKind.ChainLine)
            };
            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state, attacks);
            for (var tileIndex = 0; tileIndex <= 5; tileIndex++)
                Assert.That(state.Board.Tiles[tileIndex].FireTurnsRemaining, Is.GreaterThan(0), $"tile {tileIndex}");
            Assert.That(state.Board.Tiles[6].FireTurnsRemaining, Is.Zero);
            Assert.That(state.Board.Tiles[35].FireTurnsRemaining, Is.Zero);
        }




[Test]
        public void ChainTile_TransfersThreeTilesTowardLowerTileIndices()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var tower = new GaeBullBing.Core.Towers.TowerState { InstanceId = 1, DefinitionId = "TOW_04" };
            tower.AppliedEffectIds.Add("chain_tile");
            state.Board.Tiles[5].Tower = tower;
            var target = CreateMonster(1, 10, 100, 10);
            var firstChain = CreateMonster(2, 9, 100, 9);
            var secondChain = CreateMonster(3, 8, 100, 8);
            var thirdChain = CreateMonster(4, 7, 100, 7);
            var oppositeTile = CreateMonster(5, 11, 100, 11);
            state.Monsters.Add(target); state.Monsters.Add(firstChain); state.Monsters.Add(secondChain);
            state.Monsters.Add(thirdChain); state.Monsters.Add(oppositeTile);

            var results = new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, target.InstanceId, 10f, false, targetTileIndex: 10) });

            Assert.That(firstChain.CurrentHealth, Is.EqualTo(90f));
            Assert.That(secondChain.CurrentHealth, Is.EqualTo(90f));
            Assert.That(thirdChain.CurrentHealth, Is.EqualTo(90f));
            
            var chainTileCount = 0;
            var areaMarkerCount = 0;
            foreach (var result in results)
            {
                if (result.VisualKind == GaeBullBing.Core.Towers.TowerAttackVisualKind.ChainTile)
                    chainTileCount++;
                if (result.VisualKind == GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile)
                    areaMarkerCount++;
            }
            Assert.That(chainTileCount, Is.EqualTo(4));
            Assert.That(areaMarkerCount, Is.Zero);
Assert.That(oppositeTile.CurrentHealth, Is.EqualTo(100f));
            Assert.That(results, Has.Some.Matches<GaeBullBing.Core.Towers.TowerAttackResult>(result =>
                result.VisualKind == GaeBullBing.Core.Towers.TowerAttackVisualKind.ChainTile &&
                result.TargetTileIndex == 7));
        }

[Test]
        public void ChainTile_AppliesShockToEveryChainedMonster()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var tower = new GaeBullBing.Core.Towers.TowerState { InstanceId = 1, DefinitionId = "TOW_04" };
            tower.AppliedEffectIds.Add("chain_tile"); tower.AppliedEffectIds.Add("shock");
            state.Board.Tiles[5].Tower = tower;
            var target = CreateMonster(1, 10, 100, 10);
            var firstChain = CreateMonster(2, 9, 100, 9);
            var secondChain = CreateMonster(3, 8, 100, 8);
            var thirdChain = CreateMonster(4, 7, 100, 7);
            state.Monsters.Add(target); state.Monsters.Add(firstChain); state.Monsters.Add(secondChain); state.Monsters.Add(thirdChain);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, target.InstanceId, 10f, false, targetTileIndex: 10) });

            Assert.That(target.Shocked, Is.True);
            Assert.That(firstChain.Shocked, Is.True);
            Assert.That(secondChain.Shocked, Is.True);
            Assert.That(thirdChain.Shocked, Is.True);
        }

        [Test]
        public void Explosion_AppliesBurnToSecondaryTargets()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("explode");
            tower.AppliedEffectIds.Add("burn");
            var primary = CreateMonster(1, 5, 100, 5);
            var adjacent = CreateMonster(2, 6, 100, 6);
            state.Monsters.Add(primary);
            state.Monsters.Add(adjacent);

            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [1] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 1, 1)
            };
            var attacks = new GaeBullBing.Core.Towers.TowerCombatService().ResolveByTower(state, stats);
            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state, attacks);

            Assert.That(primary.BurnStacks, Is.EqualTo(1));
            Assert.That(adjacent.BurnStacks, Is.EqualTo(1));
        }

        [Test]
        public void TileBreak_AppliesFreezeToEverySecondaryTarget()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("tile_break");
            tower.AppliedEffectIds.Add("freeze");
            state.Board.Tiles[5].IceTurnsRemaining = TileState.OneTurnEffectDuration;
            var first = CreateMonster(1, 5, 100, 5);
            var second = CreateMonster(2, 5, 100, 5);
            state.Monsters.Add(first);
            state.Monsters.Add(second);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(
                state,
                new[]
                {
                    new GaeBullBing.Core.Towers.TowerAttackResult(
                        1, first.InstanceId, 10f, false, targetTileIndex: 5)
                });

            Assert.That(first.FrozenMovesRemaining, Is.EqualTo(1));
            Assert.That(second.FrozenMovesRemaining, Is.EqualTo(1));
        }

        [Test]
        public void RollingStone_AppliesSourceTowerOnHitDebuff()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("rolling_stone");
            tower.AppliedEffectIds.Add("burn");
            tower.StoneActive = true;
            tower.StoneTileIndex = 5;
            tower.StoneBaseDamage = 10;
            tower.StoneDamageMultiplier = 1f;
            var target = CreateMonster(1, 4, 100, 4);
            state.Monsters.Add(target);

            var attacks = new System.Collections.Generic.List<GaeBullBing.Core.Towers.TowerAttackResult>();
            var effects = new GaeBullBing.Core.Towers.TowerEffectService();
            effects.ResolveStone(state, tower, attacks);
            effects.ResolveAfterAttacks(state, attacks);

            Assert.That(target.CurrentHealth, Is.EqualTo(90f));
            Assert.That(target.BurnStacks, Is.EqualTo(1));
        }

        [Test]
        public void RollingStone_WithKnockbackPushesAndHitsAgainOnEveryFollowingTile()
        {
            var state = CreateCombatState();
            state.Board.Tiles[5].Tower = null;
            var tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_03",
                StoneActive = true,
                StoneTileIndex = 8,
                StoneBaseDamage = 10,
                StoneDamageMultiplier = 1f
            };
            tower.AppliedEffectIds.Add("rolling_stone");
            tower.AppliedEffectIds.Add("knockback");
            state.Board.Tiles[8].Tower = tower;
            var target = CreateMonster(1, 7, 200, 7);
            state.Monsters.Add(target);
            var results = new System.Collections.Generic.List<GaeBullBing.Core.Towers.TowerAttackResult>();

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveStone(state, tower, results);

            var rollingHitCount = 0;
            foreach (var result in results)
                if (result.VisualKind == GaeBullBing.Core.Towers.TowerAttackVisualKind.RollingStone &&
                    result.TargetInstanceId == target.InstanceId)
                    rollingHitCount++;
            Assert.That(target.CurrentTileIndex, Is.EqualTo(0));
            Assert.That(rollingHitCount, Is.EqualTo(8));
            Assert.That(target.CurrentHealth, Is.EqualTo(148f));
        }

        [Test]
        public void RollingStone_EmitsMovementResultsWhenNoMonsterIsHit()
        {
            var state = CreateCombatState();
            state.Board.Tiles[5].Tower = null;
            var tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 99,
                DefinitionId = "TOW_03",
                StoneActive = true,
                StoneTileIndex = 8,
                StoneBaseDamage = 10,
                StoneDamageMultiplier = 1f
            };
            state.Board.Tiles[8].Tower = tower;
            var results = new System.Collections.Generic.List<GaeBullBing.Core.Towers.TowerAttackResult>();

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveStone(state, tower, results);

            Assert.That(tower.StoneTraversalTiles.Count, Is.GreaterThan(0));
            Assert.That(results.Count, Is.EqualTo(tower.StoneTraversalTiles.Count));
            foreach (var result in results)
            {
                Assert.That(result.VisualKind,
                    Is.EqualTo(GaeBullBing.Core.Towers.TowerAttackVisualKind.RollingStone));
                Assert.That(result.TargetInstanceId, Is.EqualTo(-1));
            }
        }

        [Test]
        public void AreaEffect_EmitsTileMarkersBeforeAreaDamage()
        {
            var state = CreateCombatState();
            state.Board.Tiles[5].Tower.AppliedEffectIds.Add("explode");
            state.Monsters.Add(CreateMonster(1, 5, 100, 5));
            state.Monsters.Add(CreateMonster(2, 6, 100, 6));

            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [1] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 1, 1)
            };
            var results = new GaeBullBing.Core.Towers.TowerCombatService().ResolveByTower(state, stats);

            Assert.That(results[0].VisualKind,
                Is.EqualTo(GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile));
            Assert.That(results[1].VisualKind,
                Is.EqualTo(GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile));
            Assert.That(results[2].VisualKind,
                Is.EqualTo(GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile));
            Assert.That(new[] { results[0].TargetTileIndex, results[1].TargetTileIndex, results[2].TargetTileIndex },
                Is.EquivalentTo(new[] { 5, 6, 7 }));
        }

[Test]
        public void AreaAttack_WithTileBurn_PlacesFieldOnEveryAttackedTile()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("tile_burn");
            var markers = new[]
            {
                new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 0, false, targetTileIndex: 5, visualKind: GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile),
                new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 0, false, targetTileIndex: 6, visualKind: GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile),
                new GaeBullBing.Core.Towers.TowerAttackResult(1, -1, 0, false, targetTileIndex: 7, visualKind: GaeBullBing.Core.Towers.TowerAttackVisualKind.AreaTile)
            };

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state, markers);

            Assert.That(state.Board.Tiles[5].FireTurnsRemaining, Is.GreaterThan(0));
            Assert.That(state.Board.Tiles[6].FireTurnsRemaining, Is.GreaterThan(0));
            Assert.That(state.Board.Tiles[7].FireTurnsRemaining, Is.GreaterThan(0));
        }


        [Test]
        public void BurnExplode_TriggersAfterAttackRaisesBurnToTen()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("burn");
            tower.AppliedEffectIds.Add("burn_explode");
            var primary = CreateMonster(1, 5, 100, 5);
            primary.BurnStacks = 9;
            var adjacent = CreateMonster(2, 6, 100, 6);
            state.Monsters.Add(primary);
            state.Monsters.Add(adjacent);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, 1, 10f, false, targetTileIndex: 5) });

            Assert.That(primary.BurnStacks, Is.EqualTo(10));
            Assert.That(primary.CurrentHealth, Is.EqualTo(90f));
            Assert.That(adjacent.CurrentHealth, Is.EqualTo(90f));
            Assert.That(adjacent.BurnStacks, Is.EqualTo(1));
        }

        [Test]
        public void ChainLine_ReplacesProjectile_AndEachHitStartsChainTile()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.DefinitionId = "TOW_04";
            tower.AppliedEffectIds.Add("chain_line");
            tower.AppliedEffectIds.Add("chain_tile");
            tower.AppliedEffectIds.Add("burn");
            var lineTarget = CreateMonster(1, 2, 100, 2);
            var chainedTarget = CreateMonster(2, 35, 100, 35);
            state.Monsters.Add(lineTarget);
            state.Monsters.Add(chainedTarget);
            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [1] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 10, 1)
            };

            var attacks = new GaeBullBing.Core.Towers.TowerCombatService().ResolveByTower(state, stats);
            var effects = new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state, attacks);

            Assert.That(attacks, Has.Count.EqualTo(1));
            Assert.That(attacks[0].VisualKind, Is.EqualTo(GaeBullBing.Core.Towers.TowerAttackVisualKind.ChainLine));
            Assert.That(lineTarget.BurnStacks, Is.EqualTo(1));
            Assert.That(chainedTarget.CurrentHealth, Is.EqualTo(90f));
            Assert.That(chainedTarget.BurnStacks, Is.EqualTo(1));
            Assert.That(effects, Has.Some.Matches<GaeBullBing.Core.Towers.TowerAttackResult>(x => x.TargetInstanceId == 2));
        }

        [Test]
        public void UpgradeTower_CopiesDeclaredEffectIdsToRuntimeTower()
        {
            var state = CreateCombatState();
            var session = CreateSession(state);
            var upgrade = ScriptableObject.CreateInstance<GaeBullBing.Core.Data.TowerUpgradeDefinition>();
            SetPrivateField(upgrade, "id", "UPG_TEST_AREA");
            SetPrivateField(upgrade, "tier", 2);
            SetPrivateField(upgrade, "effects", new[]
            {
                new GaeBullBing.Core.Data.TowerUpgradeEffect { Id = "aoe_tile", Value = 1f }
            });

            var tower = session.UpgradeTower(5, upgrade);

            Assert.That(tower.AppliedEffectIds, Does.Contain("aoe_tile"));
            Object.DestroyImmediate(upgrade);
        }

        [Test]
        public void EffectResolution_UsesEffectIdsInsteadOfLegacyUpgradeIdMeaning()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedUpgradeIds.Add("UPG_FIRE_T3_00");
            tower.AppliedEffectIds.Add("burn");
            var target = CreateMonster(1, 5, 100, 5);
            state.Monsters.Add(target);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(
                state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(
                    tower.InstanceId, target.InstanceId, 10f, false, targetTileIndex: 5) });

            Assert.That(target.BurnStacks, Is.EqualTo(1));
            Assert.That(state.Board.Tiles[5].FireTurnsRemaining, Is.EqualTo(0));
        }


        [Test]
        public void DifficultyService_EntersBossLevelAfterFiveNormalLevels()
        {
            var patterns = new GaeBullBing.Core.Monsters.DifficultyPatternData[5];
            for (var index = 0; index < patterns.Length; index++)
                patterns[index] = new GaeBullBing.Core.Monsters.DifficultyPatternData
                {
                    RequiredKills = index * 5,
                    HealthMultiplier = 1f,
                    MonsterIds = new[] { "MON_001" }
                };
            var service = new GaeBullBing.Core.Monsters.DifficultyService(patterns, 5, 1.2f);
            var difficulty = new GaeBullBing.Core.Monsters.DifficultyState();
            service.Reset(difficulty);

            service.AddKills(difficulty, 25);

            Assert.That(difficulty.Level, Is.EqualTo(6));
            Assert.That(service.BossLevel, Is.EqualTo(6));
            Assert.That(service.IsBossLevel(difficulty), Is.True);
            Assert.That(service.GetRemainingKills(difficulty), Is.EqualTo(0));
        }

        [Test]
        public void DifficultyService_UsesConfiguredBossAppearanceLevel()
        {
            var patterns = new[]
            {
                new GaeBullBing.Core.Monsters.DifficultyPatternData
                {
                    RequiredKills = 0,
                    HealthMultiplier = 1f,
                    MonsterIds = new[] { "MON_001" }
                }
            };
            var service = new GaeBullBing.Core.Monsters.DifficultyService(
                patterns, 5, 1.2f, 0f, 4);
            var difficulty = new GaeBullBing.Core.Monsters.DifficultyState();
            service.Reset(difficulty);

            service.AddKills(difficulty, 14);
            Assert.That(service.IsBossLevel(difficulty), Is.False);

            service.AddKills(difficulty, 1);
            Assert.That(difficulty.Level, Is.EqualTo(4));
            Assert.That(service.BossLevel, Is.EqualTo(4));
            Assert.That(service.IsBossLevel(difficulty), Is.True);
        }

        [Test]
        public void Boss_IgnoresHealthMultiplierAndLevelDefenseBonus()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Difficulty.Level = 6;
            state.Difficulty.DefensePerLevel = 20f;
            var definition = CreateMonsterDefinition("BOSS_001", MonsterTier.Boss, 6000, 4, 300f);

            var boss = session.SpawnMonster(definition, 9f);
            var damage = boss.ReceiveDamage(400f, state.Difficulty);

            Assert.That(boss.MaxHealth, Is.EqualTo(6000f));
            Assert.That(boss.GetDefense(state.Difficulty), Is.EqualTo(300f));
            Assert.That(damage, Is.EqualTo(100f).Within(.0001f));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Boss_AppliesOnlyStartAndDestinationTileEffectsWhileFlying()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[0].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            state.Board.Tiles[1].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            state.Board.Tiles[2].IceTurnsRemaining = TileState.OneTurnEffectDuration;
            state.Board.Tiles[4].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            state.Board.Tiles[3].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_03"
            };
            state.Board.Tiles[3].Tower.AppliedEffectIds.Add("wall");
            var boss = new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 99,
                Tier = MonsterTier.Boss,
                CurrentHealth = 6000,
                MaxHealth = 6000,
                CurrentTileIndex = 0,
                MoveDistance = 4,
                BaseDefense = 0
            };
            state.Monsters.Add(boss);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveMonsterStandby(state);
            var result = new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state)[0];

            Assert.That(result.IsBoss, Is.True);
            Assert.That(result.Distance, Is.EqualTo(4));
            Assert.That(boss.CurrentTileIndex, Is.EqualTo(4));
            Assert.That(boss.BurnStacks, Is.EqualTo(2));
        }

        [Test]
        public void FireTile_StandbyTicksExistingBurnBeforeApplyingTileBurn()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[5].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            var monster = CreateMonster(1, 5, 100, 5);
            monster.MaxHealth = 100;
            monster.BurnStacks = 1;
            state.Monsters.Add(monster);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveMonsterStandby(state);

            Assert.That(monster.BurnStacks, Is.EqualTo(2));
            Assert.That(monster.CurrentHealth, Is.EqualTo(98.5f).Within(.0001f));
        }

        [Test]
        public void FireTile_MovementAppliesBurnOncePerEnteredFireTile()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[1].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            state.Board.Tiles[2].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            var monster = CreateMonster(1, 0, 100, 0);
            monster.MaxHealth = 100;
            monster.MoveDistance = 2;
            state.Monsters.Add(monster);

            new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state);

            Assert.That(monster.BurnStacks, Is.EqualTo(2));
            Assert.That(monster.CurrentHealth, Is.EqualTo(98.5f).Within(.0001f));
        }

        [Test]
        public void Boss_ReceivesFreezeDebuffLikeOtherMonsters()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var boss = new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 99,
                Tier = MonsterTier.Boss,
                CurrentTileIndex = 0,
                CurrentHealth = 6000,
                MaxHealth = 6000,
                MoveDistance = 4,
                FrozenMovesRemaining = 1
            };
            state.Monsters.Add(boss);

            var result = new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state)[0];

            Assert.That(result.Distance, Is.EqualTo(0));
            Assert.That(boss.CurrentTileIndex, Is.EqualTo(0));
            Assert.That(boss.FrozenMovesRemaining, Is.EqualTo(0));
        }

[Test]
        public void Freeze_BlocksOneMoveThenGrantsOneMovingTurnOfImmunity()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var monster = new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 1,
                CurrentTileIndex = 0,
                CurrentHealth = 100,
                MaxHealth = 100,
                MoveDistance = 2,
                FrozenMovesRemaining = 1
            };
            state.Monsters.Add(monster);
            var service = new GaeBullBing.Core.Monsters.MonsterService();

            var frozenTurn = service.MoveAll(state)[0];
            Assert.That(frozenTurn.Distance, Is.Zero);
            Assert.That(monster.FreezeImmunityPending, Is.True);
            Assert.That(monster.CanReceiveFreeze, Is.False);

            var immuneTurn = service.MoveAll(state)[0];
            Assert.That(immuneTurn.Distance, Is.EqualTo(2));
            Assert.That(monster.FreezeImmuneThisTurn, Is.True);
            Assert.That(monster.CanReceiveFreeze, Is.False);

            var vulnerableTurn = service.MoveAll(state)[0];
            Assert.That(vulnerableTurn.Distance, Is.EqualTo(2));
            Assert.That(monster.FreezeImmuneThisTurn, Is.False);
            Assert.That(monster.CanReceiveFreeze, Is.True);
        }


        [Test]
        public void Boss_EscapeCausesImmediateDefeatWithoutUsingEscapeCount()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Monsters.Add(new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = 99,
                Tier = MonsterTier.Boss,
                CurrentHealth = 1,
                CurrentTileIndex = 32,
                DistanceTravelled = 32,
                MoveDistance = 4
            });

            session.MoveMonsters();

            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.Defeat));
            Assert.That(state.EscapedMonsterCount, Is.EqualTo(0));
            Assert.That(state.BossEscaped, Is.True);
            Assert.That(state.Monsters, Is.Empty);
        }

        [Test]
        public void Boss_FeatherSealsHighestDamageTowerUntilBossPassesIt()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            state.Board.Tiles[2].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_LOW"
            };
            state.Board.Tiles[8].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 2,
                DefinitionId = "TOW_HIGH"
            };
            var low = CreateTowerDefinition("TOW_LOW", 10, 20);
            var high = CreateTowerDefinition("TOW_HIGH", 30, 20);
            var bossDefinition = CreateMonsterDefinition("BOSS_001", MonsterTier.Boss, 6000, 4, 300f);
            var boss = session.SpawnMonster(bossDefinition);
            boss.StatusImmunities.Add("knockback");

            var firstMove = session.MoveMonsters(new[] { low, high },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>())[0];

            Assert.That(state.Board.Tiles[8].HasBossFeather, Is.True);
            Assert.That(state.Board.Tiles[8].Tower.IsFeatherSealed, Is.True);
            Assert.That(firstMove.FeatherEvents[0].Type,
                Is.EqualTo(GaeBullBing.Core.Monsters.BossFeatherEventType.Drop));
            Assert.That(firstMove.FeatherEvents[0].TileIndex, Is.EqualTo(8));

            var attacks = session.ResolveTowerCombat(new[] { low, high },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());
            foreach (var attack in attacks)
                Assert.That(attack.TowerInstanceId, Is.Not.EqualTo(2));

            var secondMove = session.MoveMonsters(new[] { low, high },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>())[0];
            Assert.That(boss.CurrentTileIndex, Is.EqualTo(8));
            Assert.That(state.Board.Tiles[8].HasBossFeather, Is.False);
            Assert.That(state.Board.Tiles[8].Tower.IsFeatherSealed, Is.False);
            Assert.That(secondMove.FeatherEvents[0].Type,
                Is.EqualTo(GaeBullBing.Core.Monsters.BossFeatherEventType.Recover));

            Object.DestroyImmediate(bossDefinition);
            Object.DestroyImmediate(high);
            Object.DestroyImmediate(low);
        }

        [Test]
        public void Boss_IgnoresKnockbackAndVictoryTriggersWhenKilled()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            var bossDefinition = CreateMonsterDefinition("BOSS_001", MonsterTier.Boss, 100, 4, 0f);
            var boss = session.SpawnMonster(bossDefinition);
            boss.StatusImmunities.Add("knockback");
            boss.CurrentTileIndex = 5;
            state.Board.Tiles[5].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_03"
            };
            state.Board.Tiles[5].Tower.AppliedEffectIds.Add("knockback");
            var tower = CreateTowerDefinition("TOW_03", 10, 1);

            var results = session.ResolveTowerCombat(new[] { tower },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());

            Assert.That(results[0].KnockbackApplied, Is.False);
            Assert.That(boss.CurrentTileIndex, Is.EqualTo(5));
            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.TowerCombat));

            boss.CurrentHealth = 10;
            session.ResolveTowerCombat(new[] { tower },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());
            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.Victory));
            Assert.That(state.BossDefeated, Is.True);
            Object.DestroyImmediate(tower);
            Object.DestroyImmediate(bossDefinition);
        }

        [Test]
        public void BossDefeat_WaitsForEveryRemainingMonsterBeforeVictory()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            var bossDefinition = CreateMonsterDefinition("BOSS_001", MonsterTier.Boss, 10, 4, 0f);
            var boss = session.SpawnMonster(bossDefinition);
            boss.CurrentTileIndex = 5;
            var remaining = CreateMonster(2, 6, 100, 6);
            state.Monsters.Add(remaining);
            state.Board.Tiles[5].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_01"
            };
            var tower = CreateTowerDefinition("TOW_01", 10, 2);

            session.ResolveTowerCombat(new[] { tower },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());

            Assert.That(state.BossDefeated, Is.True);
            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.TowerCombat));
            Assert.That(state.Monsters.Count, Is.EqualTo(1));
            Assert.That(state.Monsters[0], Is.SameAs(remaining));

            remaining.CurrentHealth = 10;
            session.ResolveTowerCombat(new[] { tower },
                System.Array.Empty<GaeBullBing.Core.Data.TowerUpgradeDefinition>());

            Assert.That(state.CurrentPhase, Is.EqualTo(TurnPhase.Victory));
            Object.DestroyImmediate(tower);
            Object.DestroyImmediate(bossDefinition);
        }

        [Test]
        public void TowerCombat_PrioritizesBossAfterExistingTargetPriority()
        {
            var state = CreateCombatState();
            var normal = CreateMonster(1, 7, 100, 30);
            var boss = CreateMonster(2, 6, 20, 6);
            boss.Tier = MonsterTier.Boss;
            state.Monsters.Add(normal);
            state.Monsters.Add(boss);

            var results = ResolveCombat(state, 10, 3, 1);

            Assert.That(results[0].TargetInstanceId, Is.EqualTo(boss.InstanceId));
        }

        [Test]
        public void ElectricTowerBuff_IncreasesHitTileTowerAttacksStartingNextCombat()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var source = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_04"
            };
            source.AppliedEffectIds.Add("tower_buff");
            source.EffectValues["tower_buff"] = 1f;
            var target = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 2,
                DefinitionId = "TOW_01"
            };
            state.Board.Tiles[10].Tower = source;
            state.Board.Tiles[9].Tower = target;
            state.Monsters.Add(CreateMonster(1, 9, 10, 9));
            state.Monsters.Add(CreateMonster(2, 8, 100, 8));
            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [source.InstanceId] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 1, 1),
                [target.InstanceId] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 1, 1)
            };
            var service = new GaeBullBing.Core.Towers.TowerCombatService();

            var firstCombat = service.ResolveByTower(state, stats);

            Assert.That(target.BonusAttackCount, Is.EqualTo(0));
            Assert.That(target.PendingBonusAttackCount, Is.EqualTo(1));
            Assert.That(System.Linq.Enumerable.Count(firstCombat,
                result => result.TowerInstanceId == target.InstanceId), Is.EqualTo(1));

            var secondCombat = service.ResolveByTower(state, stats);

            Assert.That(target.BonusAttackCount, Is.EqualTo(1));
            Assert.That(System.Linq.Enumerable.Count(secondCombat,
                result => result.TowerInstanceId == target.InstanceId), Is.EqualTo(2));
        }

        [Test]
        public void FeatherSealedTower_ContinuesCooldownAndAttacksWhenUnsealed()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AttackCooldownRounds = 1;
            tower.IsFeatherSealed = true;
            state.Monsters.Add(CreateMonster(1, 5, 100, 5));
            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [tower.InstanceId] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 1, 1)
            };
            var service = new GaeBullBing.Core.Towers.TowerCombatService();

            var sealedResults = service.ResolveByTower(state, stats);
            tower.IsFeatherSealed = false;
            var unsealedResults = service.ResolveByTower(state, stats);

            Assert.That(sealedResults, Is.Empty);
            Assert.That(tower.AttackCooldownRounds, Is.EqualTo(0));
            Assert.That(unsealedResults, Is.Not.Empty);
        }

        [Test]
        public void CooldownTower_DoesNotStartCooldownWithoutAnActualAttack()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("cooldown");
            tower.EffectValues["cooldown"] = 2f;
            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [tower.InstanceId] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 1, 1)
            };
            var service = new GaeBullBing.Core.Towers.TowerCombatService();

            service.ResolveByTower(state, stats);

            Assert.That(tower.AttackCooldownRounds, Is.EqualTo(0));
        }

        [Test]
        public void WallTower_DamagesAndStopsMonsterOnEntry()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var wall = new GaeBullBing.Core.Towers.TowerState { InstanceId = 7, DefinitionId = "TOW_03" };
            wall.AppliedEffectIds.Add("wall");
            state.Board.Tiles[2].Tower = wall;
            var monster = CreateMonster(1, 0, 100, 0);
            monster.MaxHealth = 100;
            monster.MoveDistance = 4;
            state.Monsters.Add(monster);

            var results = new GaeBullBing.Core.Monsters.MonsterService().MoveAll(state,
                new System.Collections.Generic.Dictionary<int, float> { [wall.InstanceId] = 25f });

            Assert.That(results[0].Distance, Is.EqualTo(2));
            Assert.That(monster.CurrentTileIndex, Is.EqualTo(2));
            Assert.That(monster.CurrentHealth, Is.EqualTo(75f));
        }

        [Test]
        public void Knockback_AppliesDestinationFireAndWallEffects()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            var source = new GaeBullBing.Core.Towers.TowerState { InstanceId = 1, DefinitionId = "TOW_03" };
            source.AppliedEffectIds.Add("knockback");
            state.Board.Tiles[10].Tower = source;
            var wall = new GaeBullBing.Core.Towers.TowerState
                { InstanceId = 2, DefinitionId = "TOW_03", LastResolvedDamage = 20 };
            wall.AppliedEffectIds.Add("wall");
            state.Board.Tiles[4].Tower = wall;
            state.Board.Tiles[4].FireTurnsRemaining = TileState.OneTurnEffectDuration;
            var monster = CreateMonster(1, 5, 100, 5);
            monster.MaxHealth = 100;
            state.Monsters.Add(monster);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state,
                new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, 1, 10, false, targetTileIndex: 5) });

            Assert.That(monster.CurrentTileIndex, Is.EqualTo(4));
            Assert.That(monster.BurnStacks, Is.EqualTo(1));
            Assert.That(monster.CurrentHealth, Is.LessThan(80f));
            Assert.That(monster.KnockbackImmunityPending, Is.True);
            Assert.That(monster.KnockbackConsumed, Is.False);
        }

        [Test]
        public void Knockback_AppliesToNextTargetAfterEarlierTargetDiesDuringMultiAttack()
        {
            var state = CreateCombatState();
            var tower = state.Board.Tiles[5].Tower;
            tower.AppliedEffectIds.Add("knockback");
            var first = CreateMonster(1, 6, 15, 10);
            var second = CreateMonster(2, 7, 100, 9);
            state.Monsters.Add(first);
            state.Monsters.Add(second);
            var stats = new System.Collections.Generic.Dictionary<int, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                [tower.InstanceId] = new GaeBullBing.Core.Towers.TowerCombatStats(10, 3, 1, 3)
            };
            var attacks = new GaeBullBing.Core.Towers.TowerCombatService().ResolveByTower(state, stats);

            new GaeBullBing.Core.Towers.TowerEffectService().ResolveAfterAttacks(state, attacks);

            Assert.That(first.IsDead, Is.True);
            Assert.That(second.CurrentTileIndex, Is.EqualTo(6));
            Assert.That(second.KnockbackImmunityPending, Is.True);
        }

        [Test]
        public void KillReward_AddsJsonConfiguredDicePointsOnlyOnce()
        {
            var state = new GameState();
            var session = CreateSession(state);
            session.StartNewGame();
            var definition = CreateMonsterDefinition("MON_REWARD", MonsterTier.Normal, 10, 1, 0f);
            SetPrivateField(definition, "killRewardDicePoints", 5);
            var monster = session.SpawnMonster(definition);
            var kill = new[] { new GaeBullBing.Core.Towers.TowerAttackResult(1, monster.InstanceId, 10, true) };

            session.CollectKillRewards(kill);
            session.CollectKillRewards(kill);

            Assert.That(state.Player.DicePoints, Is.EqualTo(5));
            Object.DestroyImmediate(definition);
        }

        private static GameState CreateCombatState()
        {
            var state = new GameState();
            new BoardService().Initialize(state.Board);
            state.Board.Tiles[5].Tower = new GaeBullBing.Core.Towers.TowerState
            {
                InstanceId = 1,
                DefinitionId = "TOW_01"
            };
            return state;
        }

        private static GameSession CreateSession(GameState state) => new(
            state,
            new BoardService(),
            new PlayerMovementService(),
            new WeightedDiceRoller(new FixedRandom(0)),
            new GaeBullBing.Core.Monsters.MonsterService(),
            new GaeBullBing.Core.Towers.TowerService(),
            new GaeBullBing.Core.Towers.TowerCombatService());

        private static GaeBullBing.Core.Monsters.MonsterState CreateMonster(
            int id, int tileIndex, int health, int distanceTravelled)
        {
            return new GaeBullBing.Core.Monsters.MonsterState
            {
                InstanceId = id,
                CurrentTileIndex = tileIndex,
                CurrentHealth = health,
                DistanceTravelled = distanceTravelled,
                MoveDistance = 1
            };
        }

        private static GaeBullBing.Core.Data.MonsterDefinition CreateMonsterDefinition(
            string id, MonsterTier tier, int maxHealth, int moveDistance, float defense)
        {
            var definition = ScriptableObject.CreateInstance<GaeBullBing.Core.Data.MonsterDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "tier", tier);
            SetPrivateField(definition, "maxHp", maxHealth);
            SetPrivateField(definition, "moveDistance", moveDistance);
            SetPrivateField(definition, "baseDefense", defense);
            return definition;
        }

        private static GaeBullBing.Core.Data.TowerDefinition CreateTowerDefinition(
            string id, int damage, int range)
        {
            var definition = ScriptableObject.CreateInstance<GaeBullBing.Core.Data.TowerDefinition>();
            SetPrivateField(definition, "id", id);
            SetPrivateField(definition, "element", TowerElement.Physics);
            SetPrivateField(definition, "damage", damage);
            SetPrivateField(definition, "range", range);
            SetPrivateField(definition, "targetCount", 1);
            SetPrivateField(definition, "attackCount", 1);
            return definition;
        }

        private static System.Collections.Generic.IReadOnlyList<GaeBullBing.Core.Towers.TowerAttackResult> ResolveCombat(
            GameState state, int damage, int range, int targetCount)
        {
            var stats = new System.Collections.Generic.Dictionary<string, GaeBullBing.Core.Towers.TowerCombatStats>
            {
                ["TOW_01"] = new GaeBullBing.Core.Towers.TowerCombatStats(damage, range, targetCount)
            };
            return new GaeBullBing.Core.Towers.TowerCombatService().Resolve(state, stats);
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(target, value);
        }

        private sealed class FixedRandom : IDiceRandom
        {
            private readonly int value;
            public FixedRandom(int value) => this.value = value;
            public int Next(int maxExclusive) => value % maxExclusive;
        }
    }
}
