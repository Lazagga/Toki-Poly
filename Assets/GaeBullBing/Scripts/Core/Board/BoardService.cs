using System;
using GaeBullBing.Core.Data;

namespace GaeBullBing.Core.Board
{
    public sealed class BoardService
    {
        private readonly Random random;

        public BoardService(Random random = null)
        {
            this.random = random ?? new Random();
        }

        public void Initialize(
            BoardState board,
            int tileCount = BoardState.DefaultTileCount,
            string buildTowerDefinitionId = "TOW_01")
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (tileCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(tileCount));

            board.Tiles.Clear();
            for (var index = 0; index < tileCount; index++)
            {
                board.Tiles.Add(new TileState
                {
                    Index = index,
                    DefinitionId = index == 0 ? "start" : "normal",
                    BuildTowerDefinitionId = IsCorner(index) ? string.Empty : buildTowerDefinitionId
                });
            }
            AssignBonusTiles(board);
        }

        public void Initialize(BoardState board, BoardDefinition definition)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.Tiles.Length != BoardState.DefaultTileCount)
                throw new InvalidOperationException($"보드에는 {BoardState.DefaultTileCount}개의 타일이 필요합니다.");

            board.Tiles.Clear();
            foreach (var placement in definition.Tiles)
            {
                if (placement.Definition == null)
                    throw new InvalidOperationException($"{placement.Index}번 타일 정의가 없습니다.");
                board.Tiles.Add(new TileState
                {
                    Index = placement.Index,
                    DefinitionId = placement.Definition.Id,
                    BuildTowerDefinitionId = placement.Definition.BuildTowerDefinitionId
                });
            }
            board.Tiles.Sort((left, right) => left.Index.CompareTo(right.Index));
            AssignBonusTiles(board);
        }

        private void AssignBonusTiles(BoardState board)
        {
            if (board.Tiles.Count != BoardState.DefaultTileCount)
                return;

            var sideStep = BoardLayout.SideLength - 1;
            for (var line = 0; line < 4; line++)
            {
                var selectedIndex = line * sideStep + random.Next(1, sideStep);
                board.Tiles[selectedIndex].IsBonusTile = true;
            }
        }

        private static bool IsCorner(int tileIndex)
        {
            var sideStep = BoardLayout.SideLength - 1;
            return tileIndex == 0 ||
                   tileIndex == sideStep ||
                   tileIndex == sideStep * 2 ||
                   tileIndex == sideStep * 3;
        }
    }
}
