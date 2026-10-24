using System;
using System.Collections.Generic;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Game
{
    /// <summary>Region effects share the real and test-lab turn/arrival clocks.</summary>
    public sealed class RegionGimmickService
    {
        public const string OvergrowthId = "GIMMICK_OVERGROWN_PATCH";
        public const string IceSheetId = "GIMMICK_ICE_SHEET";
        public const string OasisId = "GIMMICK_OASIS_TILE";
        private readonly TowerEffectService tileEffects = new();
        private readonly Random random;
        public RegionGimmickService(Random random = null) { this.random = random ?? new Random(); }

        public void Reset(GameState state)
        {
            foreach (var tile in state.Board.Tiles)
            {
                tile.OvergrowthTurnsRemaining = 0;
                tile.OvergrowthDisallowsBuild = tile.OvergrowthDisallowsUpgrade = false;
            }
            state.RegionGimmickStartedRound = state.RegionGimmickEndedRound = -1;
            state.Player.PendingExtraRolls = state.Player.AvailableExtraRolls = 0;
            state.Player.LastOvergrowthRewardArrival = -1;
        }

        public IReadOnlyList<TowerAttackResult> BeginTurn(GameState state, RegionDefinition region)
        {
            if (state.RegionGimmickStartedRound == state.Round) return Array.Empty<TowerAttackResult>();
            // Validate before advancing the clock so bad data cannot partially consume a turn.
            if (region?.GimmickId == IceSheetId &&
                (region.Scope != "per_line" || region.AppliedEffectId != TowerEffectCatalog.TileFreeze))
                throw new InvalidOperationException("빙판 기믹에는 scope: per_line, applied_effect_id: tile_freeze가 필요합니다.");
            if (region?.GimmickId == OvergrowthId && (region.Scope != "board" || region.DurationTurns < 1))
                throw new InvalidOperationException("수풀 기믹에는 scope: board, duration_turns: 1 이상이 필요합니다.");
            state.RegionGimmickStartedRound = state.Round;
            state.Player.AvailableExtraRolls = state.Player.PendingExtraRolls;
            state.Player.PendingExtraRolls = 0;
            if (region?.GimmickId == IceSheetId) return PlaceIceSheets(state, region);
            if (region == null || region.GimmickId != OvergrowthId) return Array.Empty<TowerAttackResult>();
            var candidates = new List<int>();
            for (var i = 0; i < state.Board.TileCount; i++) candidates.Add(i);
            var count = Math.Min(Math.Max(0, region.TilesPerTurn), candidates.Count);
            for (var placed = 0; placed < count; placed++)
            {
                var pick = random.Next(candidates.Count);
                var tile = state.Board.Tiles[candidates[pick]];
                candidates.RemoveAt(pick);
                tile.OvergrowthTurnsRemaining = region.DurationTurns;
                tile.OvergrowthDisallowsBuild = region.DisallowBuild;
                tile.OvergrowthDisallowsUpgrade = region.DisallowUpgrade;
            }
            state.CombatTrace?.Add($"수풀 생성: {count}개 타일, 지속 {region.DurationTurns}턴");
            return Array.Empty<TowerAttackResult>();
        }

        private IReadOnlyList<TowerAttackResult> PlaceIceSheets(GameState state, RegionDefinition region)
        {
            var results = new List<TowerAttackResult>();
            for (var line = 0; line < 4; line++)
            {
                var candidates = new List<int>();
                foreach (var tile in state.Board.Tiles)
                    if (tile.Index % 9 != 0 && MonsterService.GetLine(tile.Index) == line)
                        candidates.Add(tile.Index);
                var count = Math.Min(Math.Max(0, region.TilesPerTurn), candidates.Count);
                for (var placed = 0; placed < count; placed++)
                {
                    var pick = random.Next(candidates.Count);
                    var index = candidates[pick];
                    candidates.RemoveAt(pick);
                    var previousIceDuration = state.Board.Tiles[index].IceTurnsRemaining;
                    // Use the tower field rules, including fire/ice cancellation and its damage.
                    results.AddRange(tileEffects.PlaceIceField(state, index));
                    // Snow is created at turn start, so it must expire at this turn's end.
                    // Do not shorten or refresh an existing, longer-lived tower field.
                    if (state.Board.Tiles[index].IceTurnsRemaining > 0)
                        state.Board.Tiles[index].IceTurnsRemaining = Math.Max(previousIceDuration, 1);
                    state.CombatTrace?.Add($"설원 빙판 생성: {line + 1}번 라인, 타일 {index}");
                }
            }
            tileEffects.ResolveDeathEffects(state, results);
            state.Monsters.RemoveAll(monster => monster.IsDead);
            return results;
        }

        public static bool IsOasisTile(RegionDefinition region, int tileIndex)
        {
            if (region?.GimmickId != OasisId || region.OverrideEffectId != "oasis_extra_upgrade") return false;
            if (region.TargetTileIndices != null && region.TargetTileIndices.Length > 0)
                return Array.IndexOf(region.TargetTileIndices, tileIndex) >= 0;
            return region.TargetTileIndex == tileIndex;
        }

        public int ResolveArrival(GameState state, RegionDefinition region, int tileIndex)
        {
            if (region == null || region.GimmickId != OvergrowthId ||
                region.TriggerCondition != "stop_exactly_on_tile" || region.RewardEffectId != "extra_turn" ||
                tileIndex < 0 || tileIndex >= state.Board.TileCount || !state.Board.Tiles[tileIndex].HasOvergrowth ||
                state.Player.LastOvergrowthRewardArrival == state.Player.ArrivalSerial) return 0;
            var reward = Math.Max(0, (int)Math.Round(region.RewardEffectValue));
            if (reward == 0) return 0;
            state.Player.LastOvergrowthRewardArrival = state.Player.ArrivalSerial;
            state.Player.PendingExtraRolls += reward;
            state.CombatTrace?.Add($"수풀 타일 {tileIndex} 도착: 다음 턴 추가 굴림 +{reward}, 대기 {state.Player.PendingExtraRolls}회");
            return reward;
        }

        public bool TryConsumeExtraRoll(GameState state)
        {
            if (state.Player.AvailableExtraRolls <= 0) return false;
            state.Player.AvailableExtraRolls--;
            return true;
        }

        public void EndTurn(GameState state)
        {
            if (state.RegionGimmickEndedRound == state.Round) return;
            state.RegionGimmickEndedRound = state.Round;
            state.Player.AvailableExtraRolls = 0;
            foreach (var tile in state.Board.Tiles)
            {
                if (tile.OvergrowthTurnsRemaining > 0) tile.OvergrowthTurnsRemaining--;
                if (!tile.HasOvergrowth) tile.OvergrowthDisallowsBuild = tile.OvergrowthDisallowsUpgrade = false;
            }
        }
    }
}
