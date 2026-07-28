using System;
using System.Collections.Generic;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;

namespace GaeBullBing.Core.Towers
{
    public sealed class TowerEffectService
    {
        private ICollection<TileEffectVisualChange> activeVisualChanges;
        private HashSet<int> activeFieldCollisionTiles;
        private int activeTriggerTileIndex = -1;
        private TowerAttackVisualKind activeTriggerVisualKind = TowerAttackVisualKind.None;

        public IReadOnlyList<TowerAttackResult> PlaceFireField(GameState state, int tileIndex, int sourceTowerInstanceId = 0) =>
            PlaceTileField(state, tileIndex, true, sourceTowerInstanceId);

        public IReadOnlyList<TowerAttackResult> PlaceIceField(GameState state, int tileIndex, int sourceTowerInstanceId = 0) =>
            PlaceTileField(state, tileIndex, false, sourceTowerInstanceId);

        public IReadOnlyList<TowerAttackResult> ResolveAfterAttacks(
            GameState state,
            IReadOnlyList<TowerAttackResult> attacks,
            ICollection<TileEffectVisualChange> visualChanges = null)
        {
            activeVisualChanges = visualChanges;
            activeFieldCollisionTiles = new HashSet<int>();
            var extra = new List<TowerAttackResult>();
            activeTriggerTileIndex = -1;
            activeTriggerVisualKind = TowerAttackVisualKind.ChainLine;
            SpreadChainLineFieldsFromSnapshot(state, attacks, extra);
            foreach (var attack in attacks)
            {
                activeTriggerTileIndex = attack.TargetTileIndex;
                activeTriggerVisualKind = attack.VisualKind;
                // Area markers carry every tile covered by an area attack. They do not deal
                // damage again, but tile-field traits must use the same attacked tile set.
                if (attack.VisualKind == TowerAttackVisualKind.AreaTile)
                {
                    var markerTowerTile = FindTowerTile(state, attack.TowerInstanceId);
                    if (markerTowerTile != null && attack.TargetTileIndex >= 0 &&
                        attack.TargetTileIndex < state.Board.TileCount)
                    {
                        var markerTower = markerTowerTile.Tower;
                        if (HasEffect(markerTower, TowerEffectCatalog.TileBurn))
                            PlaceFields(state, new[] { attack.TargetTileIndex }, true,
                                attack.TowerInstanceId, extra);
                        if (HasEffect(markerTower, TowerEffectCatalog.TileFreeze))
                            PlaceFields(state, new[] { attack.TargetTileIndex }, false,
                                attack.TowerInstanceId, extra);
                    }
                    continue;
                }
                var towerTile = FindTowerTile(state, attack.TowerInstanceId); if (towerTile == null) continue;
                var target = FindMonster(state, attack.TargetInstanceId);
                var tower = towerTile.Tower;
                var attackTileIndex = attack.TargetTileIndex >= 0
                    ? attack.TargetTileIndex
                    : target != null ? target.CurrentTileIndex : -1;
                if (attackTileIndex < 0 || attackTileIndex >= state.Board.TileCount) continue;
                var attackedTiles = new HashSet<int> { attackTileIndex };
                if (towerTile.Tower.DefinitionId == "TOW_04" &&
                    attack.VisualKind != TowerAttackVisualKind.ChainLine)
                    SpreadTileField(state, attackTileIndex,
                        1 + Math.Max(0, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f))),
                        attack.TowerInstanceId, extra);
                if (target != null) ApplyOnHitDebuffs(tower, target);
                if (target != null && !attack.KnockbackApplied && !target.IsImmuneTo("knockback") &&
                    HasEffect(tower, TowerEffectCatalog.Knockback) &&
                    !target.KnockbackConsumed)
                {
                    var fromTile = target.CurrentTileIndex;
                    var toTile = fromTile == 0
                        ? 0
                        : (fromTile + state.Board.TileCount - 1) % state.Board.TileCount;
                    target.KnockbackImmunityPending = true;
                    target.CurrentTileIndex = toTile;
                    if (toTile != fromTile) target.DistanceTravelled = Math.Max(0, target.DistanceTravelled - 1);
                    extra.Add(new TowerAttackResult(attack.TowerInstanceId, target.InstanceId, 0, false,
                        true, fromTile, toTile, attackTileIndex));
                    ResolveKnockbackDestination(state, target, toTile, attack.TowerInstanceId, extra);
                }
                if (target != null && HasEffect(tower, TowerEffectCatalog.BurnExplode) && target.BurnStacks >= 10)
                {
                    AddAreaTiles(state, attackTileIndex, 1, attackedTiles);
                    AddAreaTileMarkers(attack.TowerInstanceId, attackedTiles, extra);
                    DamageArea(state, attackTileIndex, attack.Damage, attack.TowerInstanceId, extra);
                }
                if (HasEffect(tower, TowerEffectCatalog.ChainTile))
                {
                    var chainDistance = Math.Max(1, (int)Math.Round(
                        tower.GetEffectValue(TowerEffectCatalog.ChainTile, 3f)));
                    extra.Add(new TowerAttackResult(attack.TowerInstanceId, -1, 0f, false,
                        targetTileIndex: attackTileIndex, visualKind: TowerAttackVisualKind.ChainTile));
                    for (var distance = 1; distance <= chainDistance; distance++)
                    {
                        var chainedTile = (attackTileIndex - distance + state.Board.TileCount) % state.Board.TileCount;
                        attackedTiles.Add(chainedTile);
                        activeTriggerTileIndex = chainedTile;
                        activeTriggerVisualKind = TowerAttackVisualKind.ChainTile;
                        if (tower.DefinitionId == "TOW_04")
                            SpreadTileField(state, chainedTile,
                                1 + Math.Max(0, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f))),
                                attack.TowerInstanceId, extra);
                        var resultCountBeforeTile = extra.Count;
                        DamageTile(state, chainedTile, attack.Damage, attack.TowerInstanceId, extra,
                            TowerAttackVisualKind.ChainTile);
                        if (extra.Count == resultCountBeforeTile)
                            extra.Add(new TowerAttackResult(attack.TowerInstanceId, -1, 0f, false,
                                targetTileIndex: chainedTile, visualKind: TowerAttackVisualKind.ChainTile));
                    }
                }
                activeTriggerTileIndex = attackTileIndex;
                activeTriggerVisualKind = attack.VisualKind;
                if (HasEffect(tower, TowerEffectCatalog.TileBurn))
                    PlaceAttackFields(state, attackedTiles, true, attack, tower, extra);
                if (HasEffect(tower, TowerEffectCatalog.TileFreeze))
                    PlaceAttackFields(state, attackedTiles, false, attack, tower, extra);
                if (target == null || target.IsDead) continue;
                if (HasEffect(tower, TowerEffectCatalog.SpreadDebuff))
                    SpreadStatuses(state, target, 1);
                if (HasEffect(tower, TowerEffectCatalog.TileBreak) && state.Board.Tiles[target.CurrentTileIndex].IceTurnsRemaining>0)
                {
                    state.Board.Tiles[target.CurrentTileIndex].IceTurnsRemaining=0;
                    AddAreaTileMarkers(attack.TowerInstanceId, new[] { target.CurrentTileIndex }, extra);
                    DamageTile(state,target.CurrentTileIndex,attack.Damage,attack.TowerInstanceId,extra,
                        TowerAttackVisualKind.None);
                }
                if (HasEffect(tower, TowerEffectCatalog.FreezeDamageMultiply) &&
                    target.FrozenMovesRemaining > 0)
                    ApplyDamage(state, target, attack.Damage * Math.Max(0f,
                        tower.GetEffectValue(TowerEffectCatalog.FreezeDamageMultiply, 5f) - 1f),
                        attack.TowerInstanceId, extra);
                if (HasEffect(tower, TowerEffectCatalog.BurnDamage) &&
                    target.BurnStacks > 0)
                    ApplyDamage(state, target, attack.Damage *
                        tower.GetEffectValue(TowerEffectCatalog.BurnDamage, .2f) * target.BurnStacks,
                        attack.TowerInstanceId, extra);
            }
            state.Monsters.RemoveAll(m => m.IsDead);
            activeVisualChanges = null;
            activeFieldCollisionTiles = null;
            return extra;
        }

        private void PlaceAttackFields(
            GameState state,
            IEnumerable<int> attackedTiles,
            bool placeFire,
            TowerAttackResult attack,
            TowerState tower,
            ICollection<TowerAttackResult> results)
        {
            var tiles = new List<int>(attackedTiles);
            var revealIndividually = HasEffect(tower, TowerEffectCatalog.ChainTile);
            var revealAsArea = !revealIndividually && tiles.Count > 1;
            foreach (var tileIndex in tiles)
            {
                activeTriggerTileIndex = revealIndividually || revealAsArea
                    ? tileIndex
                    : attack.TargetTileIndex;
                activeTriggerVisualKind = revealIndividually
                    ? TowerAttackVisualKind.ChainTile
                    : revealAsArea
                        ? TowerAttackVisualKind.AreaTile
                        : attack.VisualKind;
                var fieldResults = placeFire
                    ? PlaceFireField(state, tileIndex, attack.TowerInstanceId)
                    : PlaceIceField(state, tileIndex, attack.TowerInstanceId);
                foreach (var result in fieldResults)
                    results.Add(result);
            }
        }

        public IReadOnlyList<TowerAttackResult> ResolveMonsterTurnEnd(GameState state)
        {
            var results = new List<TowerAttackResult>();
            foreach (var tile in state.Board.Tiles) { if (tile.FireTurnsRemaining > 0) tile.FireTurnsRemaining--; if (tile.IceTurnsRemaining > 0) tile.IceTurnsRemaining--; }
            return results;
        }

        public IReadOnlyList<TowerAttackResult> ResolveMonsterStandby(GameState state)
        {
            var results = new List<TowerAttackResult>();
            var monsters = new List<MonsterState>(state.Monsters);
            monsters.Sort((left, right) => right.DistanceTravelled.CompareTo(left.DistanceTravelled));
            foreach (var monster in monsters)
            {
                if (monster.KnockbackImmunityPending)
                {
                    monster.KnockbackImmunityPending = false;
                    monster.KnockbackConsumed = true;
                }

                // Existing burn resolves before the current tile creates another stack.
                if (monster.BurnStacks > 0)
                    ApplyDamage(state, monster, BurnDamage(monster), 0, results);
                if (monster.IsDead) continue;

                var tile = state.Board.Tiles[monster.CurrentTileIndex];
                if (tile.FireTurnsRemaining > 0)
                {
                    monster.BurnStacks++;
                    ApplyDamage(state, monster, BurnDamage(monster), 0, results);
                }
                monster.TouchedFireThisMove = false;
            }
            state.Monsters.RemoveAll(monster => monster.IsDead);
            return results;
        }

        private static float BurnDamage(MonsterState m) => m.MaxHealth * .005f * m.BurnStacks;
        private static void AddAreaTileMarkers(
            int towerInstanceId,
            IEnumerable<int> tileIndices,
            ICollection<TowerAttackResult> results)
        {
            foreach (var tileIndex in tileIndices)
                results.Add(new TowerAttackResult(towerInstanceId, 0, 0f, false,
                    targetTileIndex: tileIndex, visualKind: TowerAttackVisualKind.AreaTile));
        }

        private static bool HasEffect(TowerState tower, string effectId) =>
            tower != null && tower.HasEffect(effectId);
        private static void AddAreaTiles(
            GameState state,
            int centerTileIndex,
            int radius,
            ISet<int> tiles)
        {
            var tileCount = state.Board.TileCount;
            for (var offset = -radius; offset <= radius; offset++)
                tiles.Add((centerTileIndex + offset + tileCount) % tileCount);
        }

        private void PlaceFields(
            GameState state,
            IEnumerable<int> tileIndices,
            bool placeFire,
            int sourceTowerInstanceId,
            ICollection<TowerAttackResult> results)
        {
            foreach (var tileIndex in tileIndices)
            {
                var fieldResults = placeFire
                    ? PlaceFireField(state, tileIndex, sourceTowerInstanceId)
                    : PlaceIceField(state, tileIndex, sourceTowerInstanceId);
                foreach (var result in fieldResults)
                    results.Add(result);
            }
        }

private void SpreadTileField(
            GameState state,
            int sourceTileIndex,
            int radius,
            int sourceTowerInstanceId,
            ICollection<TowerAttackResult> results)
        {
            var sourceTile = state.Board.Tiles[sourceTileIndex];
            if (sourceTile.FireTurnsRemaining <= 0 && sourceTile.IceTurnsRemaining <= 0) return;
            var spreadTiles = new HashSet<int>();
            AddAreaTiles(state, sourceTileIndex, radius, spreadTiles);
            PlaceFields(state, spreadTiles, sourceTile.FireTurnsRemaining > 0,
                sourceTowerInstanceId, results);
        }

        private IReadOnlyList<TowerAttackResult> PlaceTileField(
            GameState state,
            int tileIndex,
            bool placeFire,
            int sourceTowerInstanceId)
        {
            var results = new List<TowerAttackResult>();
            var tile = state.Board.Tiles[tileIndex];
            if (activeFieldCollisionTiles != null &&
                activeFieldCollisionTiles.Contains(tileIndex))
            {
                RecordTileVisualChange(
                    sourceTowerInstanceId,
                    tileIndex,
                    TileEffectVisualKind.Normal);
                return results;
            }
            var collides = placeFire ? tile.IceTurnsRemaining > 0 : tile.FireTurnsRemaining > 0;
            if (collides)
            {
                tile.FireTurnsRemaining = 0;
                tile.IceTurnsRemaining = 0;
                activeFieldCollisionTiles?.Add(tileIndex);
                RecordTileVisualChange(
                    sourceTowerInstanceId,
                    tileIndex,
                    TileEffectVisualKind.Normal);
                DamageTile(state, tileIndex, 15 + state.Difficulty.Level * 14, sourceTowerInstanceId, results);
                state.Monsters.RemoveAll(monster => monster.IsDead);
                return results;
            }

            if (placeFire) tile.FireTurnsRemaining = Board.TileState.OneTurnEffectDuration;
            else tile.IceTurnsRemaining = Board.TileState.OneTurnEffectDuration;
            RecordTileVisualChange(
                sourceTowerInstanceId,
                tileIndex,
                placeFire ? TileEffectVisualKind.Fire : TileEffectVisualKind.Ice);
            return results;
        }

        private void RecordTileVisualChange(
            int towerInstanceId,
            int tileIndex,
            TileEffectVisualKind effect)
        {
            activeVisualChanges?.Add(new TileEffectVisualChange(
                towerInstanceId,
                tileIndex,
                effect,
                activeTriggerTileIndex,
                activeTriggerVisualKind));
        }
        private static void ApplyDamage(GameState state, MonsterState monster, float damage, int tower, ICollection<TowerAttackResult> results,
            TowerAttackVisualKind visualKind = TowerAttackVisualKind.Projectile)
        {
            var actual = monster.ReceiveDamage(damage, state.Difficulty);
            if (tower > 0 && !monster.IsDead)
            {
                var sourceTile = FindTowerTile(state, tower);
                if (sourceTile != null)
                {
                    ApplyOnHitDebuffs(sourceTile.Tower, monster);
                    if (HasEffect(sourceTile.Tower, TowerEffectCatalog.SpreadDebuff))
                        SpreadStatuses(state, monster, 1);
                }
            }
            results.Add(new TowerAttackResult(tower,monster.InstanceId,actual,monster.IsDead,targetTileIndex:monster.CurrentTileIndex,visualKind:tower>0?visualKind:TowerAttackVisualKind.None));
        }

        private static void ApplyOnHitDebuffs(TowerState tower, MonsterState target)
        {
            if (tower == null || target == null || target.IsDead) return;
            if (HasEffect(tower, TowerEffectCatalog.Burn)) target.BurnStacks++;
            if (HasEffect(tower, TowerEffectCatalog.DoubleBurn))
                target.BurnStacks *= Math.Max(1, (int)Math.Round(
                    1f + tower.GetEffectValue(TowerEffectCatalog.DoubleBurn, 1f)));
            if (HasEffect(tower, TowerEffectCatalog.Freeze) && target.CanReceiveFreeze)
                target.FrozenMovesRemaining = Math.Max(target.FrozenMovesRemaining,
                    Math.Max(1, (int)Math.Round(tower.GetEffectValue(TowerEffectCatalog.Freeze, 1f))));
            if (HasEffect(tower, TowerEffectCatalog.Shock)) target.Shocked = true;
        }
        private static void DamageArea(GameState s,int center,float damage,int tower,ICollection<TowerAttackResult> r)
        { foreach(var m in s.Monsters) if(Math.Min(Math.Abs(m.CurrentTileIndex-center),s.Board.TileCount-Math.Abs(m.CurrentTileIndex-center))<=1) ApplyDamage(s,m,damage,tower,r,TowerAttackVisualKind.None); }
        private static void SpreadStatuses(GameState s, MonsterState source, int radius)
        { foreach(var m in s.Monsters) if(m.InstanceId!=source.InstanceId && Math.Min(Math.Abs(m.CurrentTileIndex-source.CurrentTileIndex),s.Board.TileCount-Math.Abs(m.CurrentTileIndex-source.CurrentTileIndex))<=radius) { m.BurnStacks=Math.Max(m.BurnStacks,source.BurnStacks); m.Shocked|=source.Shocked; if(source.FrozenMovesRemaining>0 && m.CanReceiveFreeze)m.FrozenMovesRemaining=source.FrozenMovesRemaining; } }
        private static MonsterState FindMonster(GameState s,int id)=>s.Monsters.Find(m=>m.InstanceId==id);
        private static Board.TileState FindTowerTile(GameState s,int id)=>s.Board.Tiles.Find(t=>t.HasTower&&t.Tower.InstanceId==id);
        private static void DamageTile(GameState s,int tile,float damage,int tower,ICollection<TowerAttackResult> r,
            TowerAttackVisualKind visualKind = TowerAttackVisualKind.Projectile)
        {foreach(var m in s.Monsters)if(m.CurrentTileIndex==tile)ApplyDamage(s,m,damage,tower,r,visualKind);}
        public void ResolveStone(GameState state, TowerState stone, ICollection<TowerAttackResult> results)
        {
                if (stone == null || !stone.StoneActive) return;
                stone.StoneTraversalTiles.Clear();
                stone.StoneExitAnimation = StoneExitAnimation.None;
                stone.StoneExitTileIndex = -1;

                // Resolve the whole roll in this turn. The final zero-damage step
                // is retained for presentation so the stone can shrink while moving.
                for (var step = 0; step < 12 && stone.StoneActive; step++)
                {
                    if (IsCorner(stone.StoneTileIndex))
                    {
                        stone.StoneExitAnimation = StoneExitAnimation.FallOffBoard;
                        stone.StoneActive = false;
                        break;
                    }

                    var nextTileIndex = (stone.StoneTileIndex + state.Board.TileCount - 1) % state.Board.TileCount;
                    var damage = (int)Math.Floor(stone.StoneBaseDamage * stone.StoneDamageMultiplier + .0001f);
                    if (damage <= 0)
                    {
                        stone.StoneExitAnimation = StoneExitAnimation.ShrinkOnZeroDamage;
                        stone.StoneExitTileIndex = nextTileIndex;
                        stone.StoneActive = false;
                        break;
                    }

                    stone.StoneTileIndex = nextTileIndex;
                    stone.StoneTraversalTiles.Add(stone.StoneTileIndex);
                    ResolveStoneAttack(state, stone, damage, results);
                    stone.StoneDamageMultiplier = Math.Max(0f, stone.StoneDamageMultiplier - .1f);
                    if (IsCorner(stone.StoneTileIndex))
                    {
                        stone.StoneExitAnimation = StoneExitAnimation.FallOffBoard;
                        stone.StoneActive = false;
                    }
                }

                stone.StoneActive = false;
            state.Monsters.RemoveAll(monster => monster.IsDead);
        }

        private static bool IsCorner(int tileIndex) =>
            tileIndex == 0 || tileIndex == 9 || tileIndex == 18 || tileIndex == 27;

        private static void ResolveStoneAttack(
            GameState state,
            TowerState sourceTower,
            int damage,
            ICollection<TowerAttackResult> results)
        {
            var appliesKnockback = HasEffect(sourceTower, TowerEffectCatalog.Knockback);
            var hitMonster = false;
            foreach (var monster in new List<MonsterState>(state.Monsters))
            {
                if (monster.IsDead || monster.CurrentTileIndex != sourceTower.StoneTileIndex) continue;
                hitMonster = true;

                var fromTile = monster.CurrentTileIndex;
                var actualDamage = monster.ReceiveDamage(damage, state.Difficulty);
                var toTile = fromTile;
                var knockbackApplied = appliesKnockback && !monster.IsDead &&
                    !monster.IsImmuneTo("knockback") && !monster.KnockbackConsumed;
                var destinationResults = new List<TowerAttackResult>();
                if (knockbackApplied)
                {
                    toTile = fromTile == 0
                        ? 0
                        : (fromTile + state.Board.TileCount - 1) % state.Board.TileCount;
                    monster.KnockbackImmunityPending = true;
                    monster.CurrentTileIndex = toTile;
                    if (toTile != fromTile)
                        monster.DistanceTravelled = Math.Max(0, monster.DistanceTravelled - 1);
                    ResolveKnockbackDestination(state, monster, toTile, sourceTower.InstanceId,
                        destinationResults);
                }
                results.Add(new TowerAttackResult(
                    sourceTower.InstanceId,
                    monster.InstanceId,
                    actualDamage,
                    monster.IsDead,
                    knockbackApplied,
                    fromTile,
                    toTile,
                    fromTile,
                    TowerAttackVisualKind.RollingStone));
                foreach (var destinationResult in destinationResults)
                    results.Add(destinationResult);
            }

            // 몬스터가 없는 타일도 돌의 이동 순서에는 포함되어야 한다.
            // 이 마커가 없으면 해당 돌의 공격 결과가 하나도 생성되지 않아
            // 프레젠터가 이동/퇴장 애니메이션을 실행하지 못하고 화면에 남을 수 있다.
            if (!hitMonster)
                results.Add(new TowerAttackResult(
                    sourceTower.InstanceId,
                    -1,
                    0f,
                    false,
                    targetTileIndex: sourceTower.StoneTileIndex,
                    visualKind: TowerAttackVisualKind.RollingStone));
        }

        private static void ResolveKnockbackDestination(
            GameState state,
            MonsterState monster,
            int tileIndex,
            int sourceTowerInstanceId,
            ICollection<TowerAttackResult> results)
        {
            if (monster == null || monster.IsDead || tileIndex < 0 || tileIndex >= state.Board.TileCount)
                return;
            var tile = state.Board.Tiles[tileIndex];
            if (tile.FireTurnsRemaining > 0)
            {
                monster.BurnStacks++;
                ApplyDamage(state, monster, BurnDamage(monster), sourceTowerInstanceId, results,
                    TowerAttackVisualKind.None);
            }
            if (monster.IsDead || !tile.HasTower ||
                !HasEffect(tile.Tower, TowerEffectCatalog.Wall)) return;
            ApplyDamage(state, monster, tile.Tower.LastResolvedDamage, tile.Tower.InstanceId, results,
                TowerAttackVisualKind.Projectile);
        }
    

private void SpreadChainLineFieldsFromSnapshot(
            GameState state,
            IReadOnlyList<TowerAttackResult> attacks,
            ICollection<TowerAttackResult> results)
        {
            var processedTowers = new HashSet<int>();
            foreach (var attack in attacks)
            {
                if (attack.VisualKind != TowerAttackVisualKind.ChainLine ||
                    !processedTowers.Add(attack.TowerInstanceId)) continue;
                var towerTile = FindTowerTile(state, attack.TowerInstanceId);
                if (towerTile == null || towerTile.Tower.DefinitionId != "TOW_04") continue;

                var line = MonsterService.GetLine(towerTile.Index);
                var sources = new List<KeyValuePair<int, bool>>();
                foreach (var tile in state.Board.Tiles)
                {
                    if (MonsterService.GetLine(tile.Index) != line) continue;
                    if (tile.FireTurnsRemaining > 0) sources.Add(new KeyValuePair<int, bool>(tile.Index, true));
                    else if (tile.IceTurnsRemaining > 0) sources.Add(new KeyValuePair<int, bool>(tile.Index, false));
                }

                var radius = 1 + Math.Max(0, (int)Math.Round(
                    towerTile.Tower.GetEffectValue(TowerEffectCatalog.SpreadRangeAdd, 0f)));
                foreach (var source in sources)
                {
                    var spreadTiles = new HashSet<int>();
                    AddAreaTiles(state, source.Key, radius, spreadTiles);
                    PlaceFields(state, spreadTiles, source.Value, attack.TowerInstanceId, results);
                }
            }
        }
}
}
