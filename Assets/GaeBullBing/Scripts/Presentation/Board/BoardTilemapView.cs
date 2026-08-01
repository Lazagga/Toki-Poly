using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GaeBullBing.Presentation.Board
{
    [RequireComponent(typeof(Tilemap), typeof(TilemapRenderer))]
    public sealed class BoardTilemapView : MonoBehaviour
    {
        [SerializeField] private TileBase normalTile;
        [SerializeField] private TileBase frozenTile;
        [SerializeField] private TileBase igniteTile;
        [SerializeField] private TileBase featherTile;
        [Header("Build Element Overlays")]
        [SerializeField] private Sprite fireBottomRightSprite;
        [SerializeField] private Sprite fireTopLeftSprite;
        [SerializeField] private Sprite iceBottomRightSprite;
        [SerializeField] private Sprite iceTopLeftSprite;
        [SerializeField] private Sprite physicsBottomRightSprite;
        [SerializeField] private Sprite physicsTopLeftSprite;
        [SerializeField] private Sprite electricBottomRightSprite;
        [SerializeField] private Sprite electricTopLeftSprite;
        [Header("Bonus Tile Outline")]
        [SerializeField] private Sprite bonusTileOutlineSprite;
        [SerializeField] private Vector2 bonusTileOutlineOffset;
        [SerializeField, Min(1.001f)] private float bonusTileOutlineThickness = 1.06f;
        [SerializeField] private Color bonusTileOutlineColor = new(1f, 0.72f, 0.12f, 1f);
        [SerializeField, Min(0.01f)] private float bonusTileOutlineFadeDuration = 0.35f;
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField, Min(0f)] private float playerPressDepth = 0.1f;
        [SerializeField, Min(0.01f)] private float playerPressDuration = 0.07f;
        [Header("Selection Highlight")]
        [SerializeField, Min(0f)] private float selectionLiftHeight = 0.16f;
        [SerializeField, Min(0.01f)] private float selectionLiftDuration = 0.14f;

        private Tilemap tilemap;
        private readonly Dictionary<int, Coroutine> pressRoutines = new();
        private readonly float[] pressAmounts = new float[BoardState.DefaultTileCount];
        private readonly float[] transitionOffsets = new float[BoardState.DefaultTileCount];
        private readonly float[] selectionLiftAmounts = new float[BoardState.DefaultTileCount];
        private readonly bool[] selectionLiftTargets = new bool[BoardState.DefaultTileCount];
        private readonly Dictionary<int, SpriteRenderer> individualTileRenderers = new();
        private readonly Dictionary<int, SpriteRenderer> buildElementOverlayRenderers = new();
        private readonly Dictionary<int, SpriteRenderer> bonusTileBorderRenderers = new();
        private BoardState currentBoardState;
        private Coroutine selectionLiftRoutine;

        public Tilemap Tilemap => tilemap != null ? tilemap : tilemap = GetComponent<Tilemap>();
        public float PressPulseDuration => playerPressDuration * 2f;

        private void Awake()
        {
            ConfigureSorting();
            if (buildOnAwake)
                Rebuild();
        }

        private void Reset() => ConfigureSorting();

        private void LateUpdate()
        {
            foreach (var pair in individualTileRenderers)
                PositionIndividualTile(pair.Key, pair.Value);
            foreach (var pair in bonusTileBorderRenderers)
                PositionBonusTileBorder(pair.Key, pair.Value);
            foreach (var pair in buildElementOverlayRenderers)
                PositionBuildElementOverlay(pair.Key, pair.Value);
        }

        private void ConfigureSorting()
        {
            var tilemapRenderer = GetComponent<TilemapRenderer>();
            if (tilemapRenderer == null) return;

            tilemapRenderer.enabled = true;
            tilemapRenderer.mode = TilemapRenderer.Mode.Individual;
            tilemapRenderer.sortOrder = TilemapRenderer.SortOrder.TopRight;
        }

        [ContextMenu("Rebuild Board")]
        public void Rebuild()
        {
            if (normalTile == null)
            {
                Debug.LogWarning("BoardTilemapView requires a normal tile.", this);
                return;
            }

            Tilemap.ClearAllTiles();
            for (var index = 0; index < BoardLayout.Cells.Count; index++)
            {
                Tilemap.SetTile(GetCellPosition(index), normalTile);
                ApplyPressTransform(index);
            }

            if (Application.isPlaying)
                RebuildIndividualTileRenderers();
        }

public void RefreshTileEffects(BoardState board)
        {
            if (board == null) return;
            currentBoardState = board;
            var count = Mathf.Min(board.TileCount, BoardLayout.Cells.Count);
            for (var index = 0; index < count; index++)
                RefreshTileEffect(board, index);
            RefreshRendererSorting();
        }

        public void RefreshBuildElementOverlays(
            BoardState board,
            IReadOnlyList<TowerDefinition> towerDefinitions)
        {
            foreach (var renderer in buildElementOverlayRenderers.Values)
                if (renderer != null)
                    Destroy(renderer.gameObject);
            buildElementOverlayRenderers.Clear();

            if (board == null || towerDefinitions == null)
                return;

            var container = transform.Find("Build Element Overlays");
            if (container == null)
            {
                var containerObject = new GameObject("Build Element Overlays");
                container = containerObject.transform;
                container.SetParent(transform, false);
            }

            var tileCount = Mathf.Min(board.TileCount, BoardLayout.Cells.Count);
            for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
            {
                var tile = board.Tiles[tileIndex];
                if (!tile.CanBuildTower || IsCorner(tileIndex))
                    continue;

                TowerDefinition towerDefinition = null;
                foreach (var candidate in towerDefinitions)
                    if (candidate != null && candidate.Id == tile.BuildTowerDefinitionId)
                    {
                        towerDefinition = candidate;
                        break;
                    }
                if (towerDefinition == null)
                    continue;

                var sprite = GetBuildElementOverlaySprite(
                    towerDefinition.Element, tileIndex, out var flipX);
                if (sprite == null)
                    continue;

                var overlayObject = new GameObject(
                    $"Tile {tileIndex} {towerDefinition.Element} Build Overlay");
                overlayObject.transform.SetParent(container, false);
                var renderer = overlayObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.flipX = flipX;
                renderer.color = Color.white;
                var tilemapRenderer = GetComponent<TilemapRenderer>();
                if (tilemapRenderer != null)
                    renderer.sortingLayerID = tilemapRenderer.sortingLayerID;
                renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                buildElementOverlayRenderers.Add(tileIndex, renderer);
                PositionBuildElementOverlay(tileIndex, renderer);
            }
        }

        public void RefreshBonusTileBorders(BoardState board)
        {
            foreach (var renderer in bonusTileBorderRenderers.Values)
                if (renderer != null)
                    Destroy(renderer.gameObject);
            bonusTileBorderRenderers.Clear();

            if (board == null)
                return;

            currentBoardState = board;
            var container = transform.Find("Bonus Tile Borders");
            if (container == null)
            {
                var containerObject = new GameObject("Bonus Tile Borders");
                container = containerObject.transform;
                container.SetParent(transform, false);
            }

            var tilemapRenderer = GetComponent<TilemapRenderer>();
            var tileCount = Mathf.Min(board.TileCount, BoardLayout.Cells.Count);
            for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
            {
                RefreshIndividualTileRenderer(tileIndex);
                if (!board.Tiles[tileIndex].IsBonusTile || bonusTileOutlineSprite == null)
                    continue;

                var borderObject = new GameObject($"Tile {tileIndex} Bonus Border");
                borderObject.transform.SetParent(container, false);
                borderObject.transform.localScale = Vector3.one * bonusTileOutlineThickness;
                var renderer = borderObject.AddComponent<SpriteRenderer>();
                if (tilemapRenderer != null)
                    renderer.sortingLayerID = tilemapRenderer.sortingLayerID;
                renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                renderer.sprite = bonusTileOutlineSprite;
                renderer.color = new Color(
                    bonusTileOutlineColor.r,
                    bonusTileOutlineColor.g,
                    bonusTileOutlineColor.b,
                    0f);
                bonusTileBorderRenderers.Add(tileIndex, renderer);
                PositionBonusTileBorder(tileIndex, renderer);
            }
        }

        public IEnumerator PlayBonusTileOutlineFadeIn()
        {
            var duration = Mathf.Max(0.01f, bonusTileOutlineFadeDuration);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                SetBonusTileOutlineAlpha(
                    bonusTileOutlineColor.a *
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            SetBonusTileOutlineAlpha(bonusTileOutlineColor.a);
        }

        private void SetBonusTileOutlineAlpha(float alpha)
        {
            foreach (var renderer in bonusTileBorderRenderers.Values)
            {
                if (renderer == null)
                    continue;
                renderer.color = new Color(
                    bonusTileOutlineColor.r,
                    bonusTileOutlineColor.g,
                    bonusTileOutlineColor.b,
                    alpha);
            }
        }

public void RefreshTileEffect(BoardState board, int tileIndex)
        {
            if (board == null || tileIndex < 0 || tileIndex >= board.TileCount ||
                tileIndex >= BoardLayout.Cells.Count) return;
            currentBoardState = board;
            var state = board.Tiles[tileIndex];
            var tile = state.HasBossFeather && featherTile != null
                ? featherTile
                : state.FireTurnsRemaining > 0 && igniteTile != null
                    ? igniteTile
                    : state.IceTurnsRemaining > 0 && frozenTile != null
                        ? frozenTile
                        : normalTile;
            Tilemap.SetTile(GetCellPosition(tileIndex), tile);
            Tilemap.SetColor(GetCellPosition(tileIndex), Color.white);
            ApplyPressTransform(tileIndex);
            RefreshIndividualTileRenderer(tileIndex);
        }

        public void ApplyTileEffectVisual(
            int tileIndex,
            GaeBullBing.Core.Towers.TileEffectVisualKind effect)
        {
            if (tileIndex < 0 || tileIndex >= BoardLayout.Cells.Count)
                return;

            var tile = effect switch
            {
                GaeBullBing.Core.Towers.TileEffectVisualKind.Fire
                    when igniteTile != null => igniteTile,
                GaeBullBing.Core.Towers.TileEffectVisualKind.Ice
                    when frozenTile != null => frozenTile,
                _ => normalTile
            };
            var cell = GetCellPosition(tileIndex);
            Tilemap.SetTile(cell, tile);
            Tilemap.SetColor(cell, Color.white);
            ApplyPressTransform(tileIndex);
            RefreshIndividualTileRenderer(tileIndex);
        }


        public Vector3Int GetCellPosition(int tileIndex)
        {
            var cell = BoardLayout.GetCell(tileIndex);
            return new Vector3Int(cell.X, cell.Y, 0);
        }

        public Vector3 GetWorldPosition(int tileIndex) =>
            Tilemap.GetCellCenterWorld(GetCellPosition(tileIndex));

        public void SetBossFeatherVisual(int tileIndex, bool active)
        {
            if (tileIndex < 0 || tileIndex >= BoardLayout.Cells.Count) return;
            var cell = GetCellPosition(tileIndex);
            Tilemap.SetTileFlags(cell, TileFlags.None);
            var underlyingTile = normalTile;
            if (!active && currentBoardState != null)
            {
                var state = currentBoardState.Tiles[tileIndex];
                underlyingTile = state.FireTurnsRemaining > 0 && igniteTile != null
                    ? igniteTile
                    : state.IceTurnsRemaining > 0 && frozenTile != null
                        ? frozenTile
                        : normalTile;
            }
            Tilemap.SetTile(cell,
                active && featherTile != null ? featherTile : underlyingTile);
            Tilemap.SetColor(cell, Color.white);
            ApplyPressTransform(tileIndex);
            RefreshIndividualTileRenderer(tileIndex);
            RefreshRendererSorting();
        }

        private void RefreshRendererSorting()
        {
            Tilemap.RefreshAllTiles();
            ConfigureSorting();
            var tilemapRenderer = GetComponent<TilemapRenderer>();
            if (tilemapRenderer == null) return;
            if (Application.isPlaying && individualTileRenderers.Count > 0)
            {
                tilemapRenderer.enabled = false;
                foreach (var pair in individualTileRenderers)
                    RefreshIndividualTileRenderer(pair.Key);
                return;
            }
            tilemapRenderer.enabled = false;
            tilemapRenderer.enabled = true;
        }

        public Vector3 GetPlayerStandWorldPosition(int tileIndex) =>
            GetWorldPosition(tileIndex) + GetTileVisualWorldOffset(tileIndex);

        public Vector3 GetTileVisualWorldOffset(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= pressAmounts.Length)
                return Vector3.zero;
            var localOffset = Vector3.up *
                (transitionOffsets[tileIndex] + selectionLiftAmounts[tileIndex]) +
                Vector3.down * (playerPressDepth * pressAmounts[tileIndex]);
            return Tilemap.transform.TransformVector(localOffset);
        }

        public void SetSelectionHighlights(IEnumerable<int> tileIndices)
        {
            var nextTargets = new bool[selectionLiftTargets.Length];
            if (tileIndices != null)
                foreach (var tileIndex in tileIndices)
                    if (tileIndex >= 0 && tileIndex < nextTargets.Length)
                        nextTargets[tileIndex] = true;

            var changed = false;
            for (var index = 0; index < selectionLiftTargets.Length; index++)
            {
                if (selectionLiftTargets[index] == nextTargets[index]) continue;
                selectionLiftTargets[index] = nextTargets[index];
                changed = true;
            }
            if (!changed) return;

            if (selectionLiftRoutine != null)
                StopCoroutine(selectionLiftRoutine);
            selectionLiftRoutine = StartCoroutine(AnimateSelectionLift());
        }

        public void ClearSelectionHighlights(bool instant = false)
        {
            var changed = false;
            for (var index = 0; index < selectionLiftTargets.Length; index++)
            {
                if (!selectionLiftTargets[index] && selectionLiftAmounts[index] <= 0f)
                    continue;
                selectionLiftTargets[index] = false;
                changed = true;
            }
            if (!changed) return;

            if (selectionLiftRoutine != null)
            {
                StopCoroutine(selectionLiftRoutine);
                selectionLiftRoutine = null;
            }
            if (instant)
            {
                for (var index = 0; index < selectionLiftAmounts.Length; index++)
                {
                    selectionLiftAmounts[index] = 0f;
                    ApplyPressTransform(index);
                }
                return;
            }
            selectionLiftRoutine = StartCoroutine(AnimateSelectionLift());
        }

        private IEnumerator AnimateSelectionLift()
        {
            var starts = (float[])selectionLiftAmounts.Clone();
            var duration = Mathf.Max(0.01f, selectionLiftDuration);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                for (var index = 0; index < selectionLiftAmounts.Length; index++)
                {
                    var target = selectionLiftTargets[index] ? selectionLiftHeight : 0f;
                    selectionLiftAmounts[index] = Mathf.Lerp(starts[index], target, progress);
                    ApplyPressTransform(index);
                }
                yield return null;
            }

            for (var index = 0; index < selectionLiftAmounts.Length; index++)
            {
                selectionLiftAmounts[index] =
                    selectionLiftTargets[index] ? selectionLiftHeight : 0f;
                ApplyPressTransform(index);
            }
            selectionLiftRoutine = null;
        }

        public void SetTransitionOffset(int tileIndex, float localYOffset)
        {
            if (tileIndex < 0 || tileIndex >= transitionOffsets.Length)
                return;
            transitionOffsets[tileIndex] = localYOffset;
            ApplyPressTransform(tileIndex);
        }

        public void SetAllTransitionOffsets(float localYOffset)
        {
            for (var index = 0; index < transitionOffsets.Length; index++)
                SetTransitionOffset(index, localYOffset);
        }

        public Vector3 GetBoardCenterWorld()
        {
            var center = Vector3.zero;
            for (var index = 0; index < BoardLayout.Cells.Count; index++)
                center += GetWorldPosition(index);
            return center / BoardLayout.Cells.Count;
        }

        public Vector3 GetInwardDirectionWorld(int tileIndex)
        {
            var cell = GetCellPosition(tileIndex);
            var last = BoardLayout.SideLength - 1;
            Vector3Int inwardCellDirection;

            if (cell.x == 0 && cell.y > 0 && cell.y < last)
                inwardCellDirection = Vector3Int.right;
            else if (cell.y == last && cell.x > 0 && cell.x < last)
                inwardCellDirection = Vector3Int.down;
            else if (cell.x == last && cell.y > 0 && cell.y < last)
                inwardCellDirection = Vector3Int.left;
            else if (cell.y == 0 && cell.x > 0 && cell.x < last)
                inwardCellDirection = Vector3Int.up;
            else
                return Vector3.zero;

            return (Tilemap.CellToWorld(cell + inwardCellDirection) - Tilemap.CellToWorld(cell)).normalized;
        }

        public void ResetPress(int tileIndex)
        {
            SetPressAmount(tileIndex, 0f, true);
        }

        public void ReleasePlayerTile(int tileIndex)
        {
            SetPressAmount(tileIndex, 0f, false);
        }

        public void PlayPress(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= pressAmounts.Length)
                return;
            if (pressRoutines.Remove(tileIndex, out var routine) && routine != null)
                StopCoroutine(routine);
            pressRoutines[tileIndex] = StartCoroutine(AnimatePressPulse(tileIndex));
        }

        public IEnumerator WaitForPressCompletion(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= pressAmounts.Length)
                yield break;
            while (pressRoutines.ContainsKey(tileIndex))
                yield return null;
        }

        private void SetPressAmount(int tileIndex, float target, bool instant)
        {
            if (tileIndex < 0 || tileIndex >= pressAmounts.Length)
                return;
            if (pressRoutines.Remove(tileIndex, out var routine) && routine != null)
                StopCoroutine(routine);

            if (instant)
            {
                pressAmounts[tileIndex] = target;
                ApplyPressTransform(tileIndex);
                return;
            }

            pressRoutines[tileIndex] = StartCoroutine(AnimatePress(tileIndex, target));
        }

        private IEnumerator AnimatePressPulse(int tileIndex)
        {
            yield return AnimatePressPhase(tileIndex, 1f);
            yield return AnimatePressPhase(tileIndex, 0f);
            pressRoutines.Remove(tileIndex);
        }

        private IEnumerator AnimatePress(int tileIndex, float target)
        {
            yield return AnimatePressPhase(tileIndex, target);
            pressRoutines.Remove(tileIndex);
        }

        private IEnumerator AnimatePressPhase(int tileIndex, float target)
        {
            var start = pressAmounts[tileIndex];
            var elapsed = 0f;
            while (elapsed < playerPressDuration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / playerPressDuration));
                pressAmounts[tileIndex] = Mathf.Lerp(start, target, progress);
                ApplyPressTransform(tileIndex);
                yield return null;
            }

            pressAmounts[tileIndex] = target;
            ApplyPressTransform(tileIndex);
        }

        private void ApplyPressTransform(int tileIndex)
        {
            var cell = GetCellPosition(tileIndex);
            Tilemap.SetTileFlags(cell, TileFlags.None);
            Tilemap.SetTransformMatrix(cell, Matrix4x4.TRS(
                Vector3.up *
                (transitionOffsets[tileIndex] + selectionLiftAmounts[tileIndex]) +
                Vector3.down * (playerPressDepth * pressAmounts[tileIndex]),
                Quaternion.identity,
                Vector3.one));
        }

        private Sprite GetBuildElementOverlaySprite(
            GaeBullBing.Core.TowerElement element,
            int tileIndex,
            out bool flipX)
        {
            var boardSideDirection = -GetInwardDirectionWorld(tileIndex);
            var pointsRight = boardSideDirection.x > 0f;
            var pointsUp = boardSideDirection.y > 0f;
            flipX = pointsUp ? pointsRight : !pointsRight;

            return element switch
            {
                GaeBullBing.Core.TowerElement.Fire =>
                    pointsUp ? fireTopLeftSprite : fireBottomRightSprite,
                GaeBullBing.Core.TowerElement.Ice =>
                    pointsUp ? iceTopLeftSprite : iceBottomRightSprite,
                GaeBullBing.Core.TowerElement.Physics =>
                    pointsUp ? physicsTopLeftSprite : physicsBottomRightSprite,
                GaeBullBing.Core.TowerElement.Electric =>
                    pointsUp ? electricTopLeftSprite : electricBottomRightSprite,
                _ => null
            };
        }

        private void PositionBuildElementOverlay(int tileIndex, SpriteRenderer renderer)
        {
            if (renderer == null)
                return;
            var tilePosition = GetWorldPosition(tileIndex);
            renderer.transform.position = tilePosition + GetTileVisualWorldOffset(tileIndex);
            renderer.sortingOrder = BoardDepthSorting.GetOrder(tilePosition, -99);
        }

        public Color GetTileColor(int tileIndex)
        {
            return individualTileRenderers.TryGetValue(tileIndex, out var renderer) && renderer != null
                ? renderer.color
                : Tilemap.GetColor(GetCellPosition(tileIndex));
        }

        public void SetTileColor(int tileIndex, Color color)
        {
            if (tileIndex < 0 || tileIndex >= BoardLayout.Cells.Count)
                return;
            var cell = GetCellPosition(tileIndex);
            Tilemap.SetTileFlags(cell, TileFlags.None);
            Tilemap.SetColor(cell, color);
            if (individualTileRenderers.TryGetValue(tileIndex, out var renderer) && renderer != null)
                renderer.color = color;
        }

        private void RebuildIndividualTileRenderers()
        {
            foreach (var renderer in individualTileRenderers.Values)
                if (renderer != null)
                    Destroy(renderer.gameObject);
            individualTileRenderers.Clear();

            var container = transform.Find("Individual Tile Renderers");
            if (container == null)
            {
                var containerObject = new GameObject("Individual Tile Renderers");
                container = containerObject.transform;
                container.SetParent(transform, false);
            }

            var tilemapRenderer = GetComponent<TilemapRenderer>();
            for (var tileIndex = 0; tileIndex < BoardLayout.Cells.Count; tileIndex++)
            {
                var tileObject = new GameObject($"Tile {tileIndex} Visual");
                tileObject.transform.SetParent(container, false);
                var renderer = tileObject.AddComponent<SpriteRenderer>();
                if (tilemapRenderer != null)
                    renderer.sortingLayerID = tilemapRenderer.sortingLayerID;
                renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                individualTileRenderers.Add(tileIndex, renderer);
                RefreshIndividualTileRenderer(tileIndex);
            }

            if (tilemapRenderer != null)
                tilemapRenderer.enabled = false;
        }

        private void RefreshIndividualTileRenderer(int tileIndex)
        {
            if (!individualTileRenderers.TryGetValue(tileIndex, out var renderer) || renderer == null)
                return;
            var cell = GetCellPosition(tileIndex);
            Tilemap.RefreshTile(cell);
            renderer.sprite = Tilemap.GetSprite(cell);
            renderer.color = Tilemap.GetColor(cell);
            PositionIndividualTile(tileIndex, renderer);
        }

        private void PositionIndividualTile(int tileIndex, SpriteRenderer renderer)
        {
            if (renderer == null)
                return;
            var tilePosition = GetWorldPosition(tileIndex);
            renderer.transform.position = tilePosition + GetTileVisualWorldOffset(tileIndex);
            renderer.sortingOrder = BoardDepthSorting.GetOrder(tilePosition, -100);
        }

        private void PositionBonusTileBorder(int tileIndex, SpriteRenderer renderer)
        {
            if (renderer == null)
                return;
            var tilePosition = GetWorldPosition(tileIndex);
            renderer.transform.position = tilePosition +
                GetTileVisualWorldOffset(tileIndex) +
                (Vector3)bonusTileOutlineOffset;
            renderer.sortingOrder = BoardDepthSorting.GetOrder(tilePosition, -101);
        }

        private static bool IsCorner(int tileIndex) =>
            tileIndex == 0 || tileIndex == 9 || tileIndex == 18 || tileIndex == 27;
    }
}
