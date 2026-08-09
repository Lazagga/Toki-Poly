using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Towers;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.Audio;
using UnityEngine;

namespace GaeBullBing.Presentation.Towers
{
    public sealed class StonePresenter : MonoBehaviour
    {
        [SerializeField] private BoardTilemapView boardView;
        [SerializeField, Min(0.05f)] private float diameter = 0.62f;
        [SerializeField, Min(0.05f)] private float moveDuration = 0.2f;
        [SerializeField, Min(0.05f)] private float exitDuration = 0.45f;
        [SerializeField, Min(0.1f)] private float fallDistance = 1.15f;
        [SerializeField] private Vector3 visualOffset = new(0f, .32f, 0f);
        [SerializeField] private int behindActorOffset = 5;

        private readonly Dictionary<int, SpriteRenderer> views = new();
        private Sprite circleSprite;
        private Sprite stoneSprite;

        public void Initialize(BoardTilemapView view, Sprite attackSprite = null)
        {
            boardView = view;
            if (attackSprite != null) stoneSprite = attackSprite;
            if (circleSprite == null) circleSprite = CreateCircleSprite();
        }

        public void Refresh(GameState state)
        {
            if (state == null || boardView == null) return;
            if (circleSprite == null) circleSprite = CreateCircleSprite();

            var activeIds = new HashSet<int>();
            foreach (var tile in state.Board.Tiles)
            {
                if (!tile.HasTower || !tile.Tower.StoneActive && tile.Tower.StoneTraversalTiles.Count == 0) continue;

                var tower = tile.Tower;
                activeIds.Add(tower.InstanceId);
                if (!views.TryGetValue(tower.InstanceId, out var renderer))
                {
                    var stoneObject = new GameObject($"Stone ({tower.InstanceId})");
                    stoneObject.transform.SetParent(transform, false);
                    renderer = stoneObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = stoneSprite != null ? stoneSprite : circleSprite;
                    renderer.color = Color.white;
                    views.Add(tower.InstanceId, renderer);
                }

                var displayTileIndex = tower.StoneTraversalTiles.Count > 0 ? tile.Index : tower.StoneTileIndex;
                var groundPosition = boardView.GetWorldPosition(displayTileIndex);
                renderer.transform.position = groundPosition + visualOffset;
                SetStoneScale(renderer, diameter);
                renderer.sortingOrder = GetStoneOrder(groundPosition, displayTileIndex);
                renderer.gameObject.SetActive(true);
            }

            foreach (var pair in views)
                if (!activeIds.Contains(pair.Key)) pair.Value.gameObject.SetActive(false);
        }

        public IEnumerator PlayResolvedMovement(
            GameState state,
            IReadOnlyList<TowerAttackResult> resolvedResults,
            ISet<int> consumedResultIndices,
            System.Func<TowerAttackResult, IEnumerator> playAttack,
            int onlyTowerInstanceId = 0)
        {
            if (state == null || boardView == null) yield break;
            foreach (var tile in state.Board.Tiles)
            {
                if (!tile.HasTower || tile.Tower.StoneTraversalTiles.Count == 0 ||
                    onlyTowerInstanceId > 0 && tile.Tower.InstanceId != onlyTowerInstanceId) continue;
                var tower = tile.Tower;
                if (!views.TryGetValue(tower.InstanceId, out var renderer)) continue;

                var audio = AudioManager.Instance;
                audio?.PlayAt(audio.Tower.RollingStone, boardView.GetWorldPosition(tile.Index));

                try
                {
                    renderer.gameObject.SetActive(true);
                    renderer.color = Color.white;
                    SetStoneScale(renderer, diameter);
                    var previousPosition = renderer.transform.position;
                    foreach (var tileIndex in tower.StoneTraversalTiles)
                    {
                        var groundPosition = boardView.GetWorldPosition(tileIndex);
                        var position = groundPosition + visualOffset;
                        yield return MoveStone(renderer, previousPosition, position, moveDuration,
                            false, false, groundPosition, tileIndex);
                        previousPosition = position;
                        if (resolvedResults != null && playAttack != null)
                            for (var resultIndex = 0; resultIndex < resolvedResults.Count; resultIndex++)
                            {
                                if (consumedResultIndices != null &&
                                    consumedResultIndices.Contains(resultIndex)) continue;
                                var result = resolvedResults[resultIndex];
                                if (result.VisualKind != TowerAttackVisualKind.RollingStone ||
                                    result.TowerInstanceId != tower.InstanceId ||
                                    result.TargetTileIndex != tileIndex) continue;
                                consumedResultIndices?.Add(resultIndex);
                                yield return playAttack(result);
                            }
                    }

                    if (tower.StoneExitAnimation == StoneExitAnimation.FallOffBoard)
                    {
                        var lastTileIndex = tower.StoneTraversalTiles[tower.StoneTraversalTiles.Count - 1];
                        var cornerGround = boardView.GetWorldPosition(lastTileIndex);
                        var previousGround = tower.StoneTraversalTiles.Count > 1
                            ? boardView.GetWorldPosition(
                                tower.StoneTraversalTiles[tower.StoneTraversalTiles.Count - 2])
                            : boardView.GetWorldPosition(tile.Index);
                        var exitDirection = cornerGround - previousGround;
                        if (exitDirection.sqrMagnitude < .001f) exitDirection = Vector3.down;
                        var outsideGround = cornerGround + exitDirection;
                        var outsidePosition = outsideGround + visualOffset;
                        yield return MoveStone(renderer, previousPosition, outsidePosition, moveDuration,
                            false, false, outsideGround, lastTileIndex);
                        var fallTarget = outsidePosition + Vector3.down * fallDistance;
                        yield return MoveStone(renderer, outsidePosition, fallTarget, exitDuration,
                            false, true, outsideGround, lastTileIndex);
                    }
                    else if (tower.StoneExitAnimation == StoneExitAnimation.ShrinkOnZeroDamage &&
                             tower.StoneExitTileIndex >= 0)
                    {
                        var targetGround = boardView.GetWorldPosition(tower.StoneExitTileIndex);
                        var target = targetGround + visualOffset;
                        yield return MoveStone(renderer, previousPosition, target, exitDuration,
                            true, true, targetGround, tower.StoneExitTileIndex);
                    }
                }
                finally
                {
                    // 공격 연출이나 씬 전환이 코루틴을 중단해도 돌이 중간에 남지 않게 정리한다.
                    if (renderer != null) renderer.gameObject.SetActive(false);
                    tower.StoneTraversalTiles.Clear();
                    tower.StoneExitAnimation = StoneExitAnimation.None;
                    tower.StoneExitTileIndex = -1;
                }
            }
        }

        private IEnumerator MoveStone(
            SpriteRenderer renderer,
            Vector3 start,
            Vector3 end,
            float duration,
            bool shrink,
            bool fade,
            Vector3 sortingGroundPosition,
            int sortingTileIndex)
        {
            var elapsed = 0f;
            var startScale = renderer.transform.localScale;
            var horizontalDelta = end.x - start.x;
            var rotationSpeed = Mathf.Abs(horizontalDelta) < .001f
                ? 0f
                : -Mathf.Sign(horizontalDelta) * 540f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var eased = normalized * normalized * (3f - 2f * normalized);
                var position = Vector3.LerpUnclamped(start, end, eased);
                renderer.transform.position = position;
                renderer.transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
                renderer.sortingOrder = GetStoneOrder(sortingGroundPosition, sortingTileIndex);
                if (shrink)
                    renderer.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
                if (fade)
                {
                    var color = renderer.color;
                    color.a = 1f - eased;
                    renderer.color = color;
                }
                yield return null;
            }

            renderer.transform.position = end;
            if (shrink)
                renderer.transform.localScale = Vector3.zero;
            if (fade)
            {
                var color = renderer.color;
                color.a = 0f;
                renderer.color = color;
            }
        }

        private int GetStoneOrder(Vector3 groundPosition, int tileIndex) =>
            BoardDepthSorting.GetActorOrder(groundPosition, tileIndex) - Mathf.Max(1, behindActorOffset);

        private static void SetStoneScale(SpriteRenderer renderer, float targetDiameter)
        {
            var spriteSize = renderer.sprite != null
                ? Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y)
                : 1f;
            renderer.transform.localScale = Vector3.one * (spriteSize > .001f
                ? targetDiameter / spriteSize
                : targetDiameter);
        }

        private static Sprite CreateCircleSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Temporary Stone Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radiusSquared = center * center;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                pixels[y * size + x] = dx * dx + dy * dy <= radiusSquared
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        }
    }
}
