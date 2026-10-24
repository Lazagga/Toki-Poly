using System;
using System.Linq;
using GaeBullBing.Core.Game;
using UnityEngine;

namespace GaeBullBing.Presentation.TestLab
{
    public sealed partial class TestLabController
    {
        private void DrawRegionGimmickTests()
        {
            var region = regions.FirstOrDefault(r => r.Id == game.State.CurrentRegionId);
            if (region?.GimmickId == RegionGimmickService.IceSheetId)
            {
                GUILayout.Label("빙판은 실제 게임과 동일한 턴 시작 로직으로 생성됩니다. ‘공격 / 효과’의 턴 종료 → 턴 시작으로 갱신하세요.", WrapStyle());
                for (var line = 0; line < 4; line++)
                {
                    var currentLine = line;
                    var indices = game.State.Board.Tiles.Where(t => t.IceTurnsRemaining > 0 && t.Index % 9 != 0 &&
                        GaeBullBing.Core.Monsters.MonsterService.GetLine(t.Index) == currentLine).Select(t => t.Index);
                    GUILayout.Label($"{line + 1}번 라인 빙판: {string.Join(", ", indices)}", WrapStyle());
                }
            }
            if (region?.GimmickId != RegionGimmickService.OasisId) return;
            GUILayout.Space(8);
            GUILayout.Label("오아시스 시험", SectionStyle());
            var action = game.Session.PendingOasisUpgrade;
            if (action == null)
            {
                var sources = game.State.Board.Tiles.Where(t => game.Session.IsOasisTile(t.Index)).Select(t => t.Index).ToArray();
                if (sources.Length == 0) { GUILayout.Label("JSON에 오아시스 출발 타일이 없습니다."); return; }
                oasisSourceIndex = Mathf.Clamp(oasisSourceIndex, 0, sources.Length - 1);
                GUILayout.Label("도착한 오아시스 타일");
                oasisSourceIndex = GUILayout.Toolbar(oasisSourceIndex, sources.Select(i => $"{i}번 타일").ToArray());
                DrawIntField("대상 타일", ref towerTile, ref towerTileText, 0, 35);
                GUILayout.Label($"기존 타워: 강화 {region.OverrideEffectValue:0}회 / 빈 타일: 선택 후 즉시 2티어 건설. 플레이어 이동 연출과 적 턴은 실행하지 않습니다.", WrapStyle());
                if (GUILayout.Button("오아시스 선택 시작"))
                    RunSync("오아시스 선택 시작", () => {
                        RequireTileInput(towerTileText);
                        if (game.Session.GetOasisUpgradeOptions(towerTile, towers, upgrades).Count == 0)
                            throw new InvalidOperationException("이 타일에는 적용 가능한 오아시스 강화가 없습니다.");
                        game.Session.TeleportPlayer(sources[oasisSourceIndex]);
                        game.Session.BeginOasisUpgrade(towerTile, towers, upgrades);
                        game.TestLabRefreshViews();
                    });
                return;
            }
            GUILayout.Label($"대상 타일 {action.TileIndex} / 남은 선택 {action.RemainingChoices}회", UpgradeTierStyle());
            GUILayout.Label("실제 게임과 같은 강화 풀입니다. 시험 편의를 위해 가능한 선택지를 모두 표시합니다.", WrapStyle());
            foreach (var option in game.Session.GetOasisUpgradeOptions(action.TileIndex, towers, upgrades))
            {
                var selected = option;
                if (GUILayout.Button($"{option.Tier}티어 · {option.Description}", UpgradeStyle()))
                    RunAction("오아시스 강화 선택", () => game.TestLabApplyOasisChoice(selected));
            }
            if (GUILayout.Button("오아시스 선택 종료"))
                RunSync("오아시스 선택 종료", () => game.Session.CancelOasisUpgrade());
        }
    }
}
