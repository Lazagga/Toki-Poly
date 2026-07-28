using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core;
using GaeBullBing.Core.Data;
using GaeBullBing.Presentation.Board;
using UnityEngine;

namespace GaeBullBing.Presentation.Towers
{
    public sealed class TowerPresenter : MonoBehaviour
    {
        [SerializeField] private BoardTilemapView boardView;
        [SerializeField] private Sprite towerSprite;
        [SerializeField] private Sprite[] fireSprites = new Sprite[6];
        [SerializeField] private Sprite[] iceSprites = new Sprite[6];
        [SerializeField] private Sprite[] physicsSprites = new Sprite[6];
        [SerializeField] private Sprite[] electricSprites = new Sprite[6];

        [Header("Build Animation")]
        [SerializeField, Min(0.01f)] private float buildGrowDuration = 0.22f;
        [SerializeField, Min(0.01f)] private float buildSettleDuration = 0.10f;
        [SerializeField, Range(1f, 2f)] private float buildOvershootScale = 1.15f;
        [SerializeField, Min(0f)] private float buildRiseHeight = 0.16f;

        [Header("Upgrade Animation")]
        [SerializeField, Min(0.01f)] private float upgradeShrinkDuration = 0.14f;
        [SerializeField, Min(0.01f)] private float upgradeGrowDuration = 0.20f;
        [SerializeField, Min(0.01f)] private float upgradeSettleDuration = 0.10f;
        [SerializeField, Range(1f, 2f)] private float upgradeOvershootScale = 1.15f;
        [SerializeField, Min(0f)] private float upgradeDropHeight = 0.18f;

        [Header("Over Upgrade Animation")]
        [SerializeField, Min(0.01f)] private float overUpgradeEffectDuration = 0.40f;
        [SerializeField, Range(1f, 2f)] private float overUpgradeFlashStartScale = 1.08f;
        [SerializeField, Range(1f, 2f)] private float overUpgradeFlashEndScale = 1.35f;
        [SerializeField, Range(1f, 1.5f)] private float overUpgradeTowerPulseScale = 1.08f;

        private readonly Dictionary<int, SpriteRenderer> towerViews = new();
        private readonly Dictionary<int, float> towerVerticalOffsets = new();

        private void LateUpdate()
        {
            foreach (var pair in towerViews)
                PositionTower(pair.Key, pair.Value);
        }

        public void SetTower(int tileIndex, TowerDefinition definition, int tier = 1)
        {
            var renderer = GetOrCreateTowerView(tileIndex);
            ApplyTowerVisual(tileIndex, renderer, definition, tier);
            renderer.transform.localScale = Vector3.one;
        }

        public IEnumerator PlayBuildAnimation(
            int tileIndex,
            TowerDefinition definition,
            int tier = 1)
        {
            var renderer = GetOrCreateTowerView(tileIndex);
            ApplyTowerVisual(tileIndex, renderer, definition, tier);
            renderer.transform.localScale = Vector3.zero;

            yield return ScaleTower(
                tileIndex,
                renderer.transform,
                Vector3.zero,
                Vector3.one * buildOvershootScale,
                buildGrowDuration,
                0f,
                buildRiseHeight);
            yield return ScaleTower(
                tileIndex,
                renderer.transform,
                Vector3.one * buildOvershootScale,
                Vector3.one,
                buildSettleDuration,
                buildRiseHeight,
                0f);
            towerVerticalOffsets.Remove(tileIndex);
        }

        public IEnumerator PlayOverUpgradeAnimation(
            IReadOnlyList<int> tileIndices,
            TowerElement element)
        {
            if (tileIndices == null || tileIndices.Count == 0)
                yield break;

            var affectedTowers = new List<SpriteRenderer>();
            var flashes = new List<(int TileIndex, SpriteRenderer Renderer)>();
            var flashColor = GetEnhancementColor(element);

            foreach (var tileIndex in tileIndices)
            {
                if (!towerViews.TryGetValue(tileIndex, out var towerRenderer) ||
                    towerRenderer == null)
                    continue;

                affectedTowers.Add(towerRenderer);

                var flashObject = new GameObject($"Tower {tileIndex} Over Upgrade Flash");
                flashObject.transform.SetParent(transform, false);
                var flashRenderer = flashObject.AddComponent<SpriteRenderer>();
                flashRenderer.sprite = towerRenderer.sprite;
                flashRenderer.flipX = towerRenderer.flipX;
                flashes.Add((tileIndex, flashRenderer));
            }

            var elapsed = 0f;
            while (elapsed < overUpgradeEffectDuration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / overUpgradeEffectDuration);
                var easedProgress = progress * progress * (3f - 2f * progress);
                var pulse = 1f +
                    Mathf.Sin(progress * Mathf.PI) *
                    (overUpgradeTowerPulseScale - 1f);

                foreach (var towerRenderer in affectedTowers)
                    towerRenderer.transform.localScale = Vector3.one * pulse;

                foreach (var flash in flashes)
                {
                    var flashRenderer = flash.Renderer;
                    flashRenderer.color = new Color(
                        flashColor.r,
                        flashColor.g,
                        flashColor.b,
                        1f - easedProgress);
                    flashRenderer.transform.localScale = Vector3.one * Mathf.Lerp(
                        overUpgradeFlashStartScale,
                        overUpgradeFlashEndScale,
                        easedProgress);
                    PositionOverUpgradeFlash(flash.TileIndex, flashRenderer);
                }

                yield return null;
            }

            foreach (var towerRenderer in affectedTowers)
                towerRenderer.transform.localScale = Vector3.one;
            foreach (var flash in flashes)
            {
                if (flash.Renderer != null)
                    Destroy(flash.Renderer.gameObject);
            }
        }

        public IEnumerator PlayUpgradeAnimation(
            int tileIndex,
            TowerDefinition definition,
            int tier)
        {
            if (!towerViews.TryGetValue(tileIndex, out var renderer))
            {
                yield return PlayBuildAnimation(tileIndex, definition, tier);
                yield break;
            }

            yield return ScaleTower(
                tileIndex,
                renderer.transform,
                renderer.transform.localScale,
                Vector3.zero,
                upgradeShrinkDuration,
                0f,
                0f);

            ApplyTowerVisual(tileIndex, renderer, definition, tier);
            renderer.transform.localScale = Vector3.zero;
            yield return ScaleTower(
                tileIndex,
                renderer.transform,
                Vector3.zero,
                Vector3.one * upgradeOvershootScale,
                upgradeGrowDuration,
                upgradeDropHeight,
                0f);
            yield return ScaleTower(
                tileIndex,
                renderer.transform,
                Vector3.one * upgradeOvershootScale,
                Vector3.one,
                upgradeSettleDuration,
                0f,
                0f);
            towerVerticalOffsets.Remove(tileIndex);
        }

        private SpriteRenderer GetOrCreateTowerView(int tileIndex)
        {
            if (towerViews.TryGetValue(tileIndex, out var renderer))
                return renderer;

            var towerObject = new GameObject($"Tower {tileIndex}");
            towerObject.transform.SetParent(transform, false);
            renderer = towerObject.AddComponent<SpriteRenderer>();
            towerViews.Add(tileIndex, renderer);
            return renderer;
        }

        private void ApplyTowerVisual(
            int tileIndex,
            SpriteRenderer renderer,
            TowerDefinition definition,
            int tier)
        {
            var inwardDirection = boardView.GetInwardDirectionWorld(tileIndex);
            // Art direction names describe the tower's board-side position,
            // which is opposite to the direction from the tile toward center.
            renderer.sprite = GetTowerSprite(definition.Element, tier, -inwardDirection, out var flipX);
            renderer.flipX = flipX;
            renderer.color = renderer.sprite != null && renderer.sprite != towerSprite
                ? Color.white
                : GetElementColor(definition.Element);
            PositionTower(tileIndex, renderer);
            renderer.gameObject.name = $"Tower {tileIndex} ({definition.Id})";
        }

        private IEnumerator ScaleTower(
            int tileIndex,
            Transform target,
            Vector3 from,
            Vector3 to,
            float duration,
            float fromHeight,
            float toHeight)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                progress = progress * progress * (3f - 2f * progress);
                target.localScale = Vector3.LerpUnclamped(from, to, progress);
                towerVerticalOffsets[tileIndex] = Mathf.LerpUnclamped(
                    fromHeight,
                    toHeight,
                    progress);
                yield return null;
            }

            target.localScale = to;
            towerVerticalOffsets[tileIndex] = toHeight;
        }

        private void PositionTower(int tileIndex, SpriteRenderer renderer)
        {
            if (boardView == null || renderer == null)
                return;
            var tilePosition = boardView.GetWorldPosition(tileIndex);
            var verticalOffset = towerVerticalOffsets.TryGetValue(tileIndex, out var height)
                ? height
                : 0f;
            renderer.transform.position =
                tilePosition +
                boardView.GetTileVisualWorldOffset(tileIndex) +
                Vector3.up * verticalOffset;
            renderer.sortingOrder = BoardDepthSorting.GetTowerOrder(tilePosition, tileIndex);
        }

        private void PositionOverUpgradeFlash(int tileIndex, SpriteRenderer renderer)
        {
            if (boardView == null || renderer == null)
                return;

            var tilePosition = boardView.GetWorldPosition(tileIndex);
            renderer.transform.position =
                tilePosition + boardView.GetTileVisualWorldOffset(tileIndex);
            renderer.sortingOrder =
                BoardDepthSorting.GetTowerOrder(tilePosition, tileIndex) + 1;
        }

        private Sprite GetTowerSprite(
            TowerElement element,
            int tier,
            Vector3 inwardDirection,
            out bool flipX)
        {
            var sprites = element switch
            {
                TowerElement.Fire => fireSprites,
                TowerElement.Ice => iceSprites,
                TowerElement.Physics => physicsSprites,
                TowerElement.Electric => electricSprites,
                _ => null
            };

            flipX = false;
            if (sprites == null || sprites.Length < 6)
                return towerSprite;

            var clampedTier = Mathf.Clamp(tier, 1, 3);
            var pointsRight = inwardDirection.x > 0f;
            var pointsUp = inwardDirection.y > 0f;
            int index;

            if (clampedTier == 1)
            {
                // Tier 1 originals: top-right and bottom-left.
                index = pointsUp ? 0 : 1;
                flipX = pointsUp ? !pointsRight : pointsRight;
            }
            else
            {
                // Tier 2/3 originals: top-left and bottom-right.
                index = (clampedTier - 1) * 2 + (pointsUp ? 0 : 1);
                flipX = pointsUp ? pointsRight : !pointsRight;
            }

            return sprites[index] != null ? sprites[index] : towerSprite;
        }

        private static Color GetElementColor(TowerElement element)
        {
            return element switch
            {
                TowerElement.Fire => new Color(1f, 0.28f, 0.08f),
                TowerElement.Ice => new Color(0.25f, 0.75f, 1f),
                TowerElement.Physics => new Color(0.72f, 0.72f, 0.72f),
                TowerElement.Electric => new Color(1f, 0.9f, 0.12f),
                _ => Color.white
            };
        }

        private static Color GetEnhancementColor(TowerElement element)
        {
            return element switch
            {
                TowerElement.Fire => new Color(1f, 0.15f, 0.08f),
                TowerElement.Ice => new Color(0.2f, 0.65f, 1f),
                TowerElement.Physics => new Color(0.2f, 0.85f, 0.35f),
                TowerElement.Electric => new Color(0.65f, 0.25f, 1f),
                _ => Color.white
            };
        }
    }
}
