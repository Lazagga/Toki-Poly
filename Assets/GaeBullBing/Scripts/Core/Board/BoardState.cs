using System.Collections.Generic;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Board
{
    public sealed class BoardState
    {
        public const int DefaultTileCount = 36;
        public List<TileState> Tiles { get; } = new();

        public int TileCount => Tiles.Count;
    }

    public sealed class TileState
    {
        public const int OneTurnEffectDuration = 2;

        public int Index { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public string BuildTowerDefinitionId { get; set; } = string.Empty;
        public TowerState Tower { get; set; }
        public int IceTurnsRemaining { get; set; }
        public int FireTurnsRemaining { get; set; }
        public bool HasBossFeather { get; set; }
        public bool IsBonusTile { get; set; }

        public bool HasTower => Tower != null;
        public bool CanBuildTower => !string.IsNullOrEmpty(BuildTowerDefinitionId);
    }
}
