using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Towers;
using GaeBullBing.Presentation.Monsters;

namespace GaeBullBing.Presentation.Game
{
    public sealed partial class GameController
    {
        public bool TestLabExecuting { get; set; }
        public int TestLabHitCount { get; private set; }
        public float TestLabDamage { get; private set; }
        public int TestLabKillCount { get; private set; }

        public TowerStatCalculation? TestLabTowerStats(int index)
        {
            if (State == null || index < 0 || index >= State.Board.TileCount) return null;
            var tile = State.Board.Tiles[index];
            if (!tile.HasTower) return null;
            var definition = FindTowerDefinition(tile.Tower.DefinitionId);
            return definition == null ? null : Session.CalculateTowerStats(definition, tile, towerUpgradeDefinitions);
        }

        public IEnumerator TestLabRunAttacks(int tileIndex, bool allTowers, bool force,
            int forcedCount, int repetitions, int preferredTarget, bool ignoreRange)
        {
            TestLabHitCount = TestLabKillCount = 0; TestLabDamage = 0f;
            for (var repeat = 0; repeat < repetitions; repeat++)
            {
                State.CombatTrace?.Add($"Execution {repeat + 1}/{repetitions}; round {State.Round}. No automatic round advance.");
                var results = allTowers
                    ? Session.ResolveTowerCombat(towerDefinitions, towerUpgradeDefinitions)
                    : Session.ResolveLabTowerPhase(tileIndex, force, forcedCount, preferredTarget, ignoreRange,
                        towerDefinitions, towerUpgradeDefinitions);
                var killed = new HashSet<int>();
                var illuminated = new HashSet<int>();
                foreach (var result in results)
                {
                    if (result.TargetInstanceId > 0 && result.Damage > 0f)
                    { TestLabHitCount++; TestLabDamage += result.Damage; }
                    if (result.Killed && killed.Add(result.TargetInstanceId)) TestLabKillCount++;
                    yield return PlayAttackResult(result, illuminated);
                }
                TestLabRefreshViews();
            }
        }

        public bool TestLabApplyMonster(int id, TestMonsterSettings settings, out string error)
        {
            var monster = State.Monsters.Find(m => m.InstanceId == id);
            if (monster == null) { error = "Select a living monster."; return false; }
            var oldTile = monster.CurrentTileIndex;
            if (!settings.Apply(monster, State.Board.TileCount, out error)) return false;
            if (oldTile != monster.CurrentTileIndex && monsterPresenter.TryGetViewTransform(id, out var view))
                view.GetComponent<MonsterBoardView>()?.TeleportToTile(monster.CurrentTileIndex);
            TestLabRefreshViews(); return true;
        }

        public IEnumerator TestLabSpawnWithPresentation(string definitionId, int tile, bool stationary,
            System.Action<int> onSpawned)
        {
            if (!TestLabSpawnMonster(definitionId, tile, stationary, out var id, out var message))
                throw new System.InvalidOperationException(message);
            onSpawned?.Invoke(id);
            if (State.Monsters.Exists(m => m.InstanceId == id && m.IsBoss))
                yield return monsterPresenter.PlayBossSpawnEntrance(id);
            TestLabRefreshViews();
        }

        public IEnumerator TestLabPlaceField(int index, string effect)
        {
            var results = effect == "ignite" ? Session.PlaceFireField(index) : Session.PlaceIceField(index);
            foreach (var result in results) yield return PlayAttackResult(result, new HashSet<int>());
            TestLabRefreshViews();
        }
    }
}
