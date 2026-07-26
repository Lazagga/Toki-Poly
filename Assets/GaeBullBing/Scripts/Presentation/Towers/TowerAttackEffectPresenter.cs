using System.Collections;
using System.Collections.Generic;
using System;
using GaeBullBing.Core;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using GaeBullBing.Presentation.Board;
using UnityEngine;

namespace GaeBullBing.Presentation.Towers
{
    public sealed class TowerAttackEffectPresenter : MonoBehaviour
    {
        [SerializeField] private BoardTilemapView boardView;
        [SerializeField] private Sprite fireAttackSprite;
        [SerializeField] private Sprite iceAttackSprite;
        [SerializeField] private Sprite physicsAttackSprite;
        [SerializeField] private Sprite electricAttackSprite;
        [SerializeField] private Sprite fireAreaTileSprite;
        [SerializeField] private Sprite iceAreaTileSprite;
        [SerializeField] private Sprite physicsAreaTileSprite;
        [SerializeField] private Sprite electricChainLineSprite;
        [SerializeField, Min(.05f)] private float projectileDuration = .24f;
        [SerializeField, Min(.05f)] private float impactDuration = .1f;
        [SerializeField, Min(.05f)] private float chainLineDuration = .32f;
        [SerializeField, Min(.05f)] private float projectileDiameter = .58f;
        [SerializeField, Range(.1f, 2f)] private float physicsProjectileScale = .8f;
        [SerializeField, Min(.01f)] private float areaTileScale = 1.166667f;
        [SerializeField] private Vector3 projectileOffset = new(0f, .32f, 0f);
        [SerializeField] private Vector3 chainTileOffset = new(0f, .05f, 0f);

        public Sprite PhysicsAttackSprite => physicsAttackSprite;

        public void Initialize(BoardTilemapView view) => boardView = view;

        public IEnumerator Play(
            GameState state,
            TowerAttackResult result,
            ISet<int> illuminatedLineTowerIds,
            Action onImpact = null)
        {
            if (state == null || boardView == null || result.TowerInstanceId <= 0)
            {
                onImpact?.Invoke();
                yield break;
            }

            if (!TryFindTower(state, result.TowerInstanceId, out var towerTileIndex, out var definitionId))
            {
                onImpact?.Invoke();
                yield break;
            }

            if (result.VisualKind == TowerAttackVisualKind.ChainLine)
            {
                if (illuminatedLineTowerIds != null && !illuminatedLineTowerIds.Add(result.TowerInstanceId))
                {
                    onImpact?.Invoke();
                    yield break;
                }
                yield return PlayChainLine(towerTileIndex, onImpact);
                yield break;
            }

            if (result.VisualKind == TowerAttackVisualKind.ChainTile && result.TargetTileIndex >= 0)
            {
                var chainSprite = GetAreaTileSprite(definitionId);
                if (chainSprite == null) onImpact?.Invoke();
                else yield return PlayTileIllumination(chainSprite, new[] { result.TargetTileIndex },
                    $"{definitionId} Chain Tile", onImpact, .08f);
                yield break;
            }

            
if (result.VisualKind == TowerAttackVisualKind.AreaTile && result.TargetTileIndex >= 0)
            {
                yield return PlayAreaTiles(state, result.TowerInstanceId,
                    new[] { result.TargetTileIndex }, onImpact);
                yield break;
            }

            if (result.VisualKind != TowerAttackVisualKind.Projectile || result.TargetTileIndex < 0)
            {
                onImpact?.Invoke();
                yield break;
            }

            var sprite = GetAttackSprite(definitionId);
            if (sprite != null)
                yield return PlayProjectile(sprite, towerTileIndex, result.TargetTileIndex,
                    definitionId == "TOW_03" ? physicsProjectileScale : 1f, onImpact);
            else
                onImpact?.Invoke();
        }

        private IEnumerator PlayProjectile(
            Sprite sprite,
            int sourceTileIndex,
            int targetTileIndex,
            float visualScale,
            Action onImpact)
        {
            var effectObject = new GameObject("Tower Attack Effect");
            effectObject.transform.SetParent(transform, false);
            var renderer = effectObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;

            var source = boardView.GetWorldPosition(sourceTileIndex) + projectileOffset;
            var target = boardView.GetWorldPosition(targetTileIndex) + projectileOffset;
            var spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            var scale = (spriteSize > .001f ? projectileDiameter / spriteSize : 1f) * visualScale;
            effectObject.transform.localScale = Vector3.one * scale;
            effectObject.transform.position = source;

            for (var elapsed = 0f; elapsed < projectileDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / projectileDuration));
                var position = Vector3.Lerp(source, target, progress);
                position.y += Mathf.Sin(progress * Mathf.PI) * .12f;
                effectObject.transform.position = position;
                effectObject.transform.Rotate(0f, 0f, 360f * Time.deltaTime);
                renderer.sortingOrder = BoardDepthSorting.GetOrder(position, 100);
                yield return null;
            }

            effectObject.transform.position = target;
            onImpact?.Invoke();
            var initialScale = effectObject.transform.localScale;
            for (var elapsed = 0f; elapsed < impactDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / impactDuration);
                effectObject.transform.localScale = initialScale * Mathf.Lerp(1f, 1.22f, progress);
                renderer.color = new Color(1f, 1f, 1f, 1f - progress);
                yield return null;
            }
            Destroy(effectObject);
        }

        private IEnumerator PlayChainLine(int towerTileIndex, Action onImpact)
        {
            if (electricChainLineSprite == null)
            {
                onImpact?.Invoke();
                yield break;
            }
            var line = MonsterService.GetLine(towerTileIndex);
            var tileIndices = new List<int>();
            for (var tileIndex = 0; tileIndex < GaeBullBing.Core.Board.BoardState.DefaultTileCount; tileIndex++)
                if (MonsterService.GetLine(tileIndex) != line) continue;
                else tileIndices.Add(tileIndex);
            yield return PlayTileIllumination(electricChainLineSprite, tileIndices, "Electric Chain Line", onImpact);
        }

        public IEnumerator PlayAreaTiles(
            GameState state,
            int towerInstanceId,
            IReadOnlyList<int> tileIndices,
            Action onImpact = null)
        {
            if (state == null || tileIndices == null || tileIndices.Count == 0)
            {
                onImpact?.Invoke();
                yield break;
            }
            if (!TryFindTower(state, towerInstanceId, out _, out var definitionId))
            {
                onImpact?.Invoke();
                yield break;
            }
            var sprite = GetAreaTileSprite(definitionId);
            if (sprite == null)
            {
                onImpact?.Invoke();
                yield break;
            }
            yield return PlayTileIllumination(sprite, tileIndices, $"{definitionId} Area Tile", onImpact);
        }

        private IEnumerator PlayTileIllumination(
            Sprite sprite,
            IReadOnlyList<int> tileIndices,
            string objectName,
            Action onImpact = null,
            float duration = -1f)
        {
            
            var effectDuration = duration > 0f ? duration : chainLineDuration;
var renderers = new List<SpriteRenderer>();
            var uniqueTiles = new HashSet<int>();
            for (var index = 0; index < tileIndices.Count; index++)
            {
                var tileIndex = tileIndices[index];
                if (!uniqueTiles.Add(tileIndex)) continue;
                var effectObject = new GameObject($"Electric Chain Line ({tileIndex})");
                effectObject.name = $"{objectName} ({tileIndex})";
                effectObject.transform.SetParent(transform, false);
                effectObject.transform.position = boardView.GetWorldPosition(tileIndex) + chainTileOffset;
                var spriteWidth = sprite.bounds.size.x;
                effectObject.transform.localScale = Vector3.one *
                    (spriteWidth > .001f ? areaTileScale / spriteWidth : areaTileScale);
                var renderer = effectObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = new Color(1f, 1f, 1f, 0f);
                renderer.sortingOrder = BoardDepthSorting.GetOrder(boardView.GetWorldPosition(tileIndex), -80);
                renderers.Add(renderer);
            }

            var impactInvoked = false;
            for (var elapsed = 0f; elapsed < effectDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / effectDuration);
                if (!impactInvoked && progress >= .5f)
                {
                    impactInvoked = true;
                    onImpact?.Invoke();
                }
                var alpha = Mathf.Sin(progress * Mathf.PI);
                foreach (var renderer in renderers)
                    renderer.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }
            if (!impactInvoked) onImpact?.Invoke();
            foreach (var renderer in renderers)
                if (renderer != null) Destroy(renderer.gameObject);
        }

        private Sprite GetAttackSprite(string definitionId)
        {
            return definitionId switch
            {
                "TOW_01" => fireAttackSprite,
                "TOW_02" => iceAttackSprite,
                "TOW_03" => physicsAttackSprite,
                "TOW_04" => electricAttackSprite,
                _ => null
            };
        }

        private Sprite GetAreaTileSprite(string definitionId)
        {
            return definitionId switch
            {
                "TOW_01" => fireAreaTileSprite,
                "TOW_02" => iceAreaTileSprite,
                "TOW_03" => physicsAreaTileSprite,
                "TOW_04" => electricChainLineSprite,
                _ => null
            };
        }

        private static bool TryFindTower(
            GameState state,
            int towerInstanceId,
            out int tileIndex,
            out string definitionId)
        {
            foreach (var tile in state.Board.Tiles)
            {
                if (!tile.HasTower || tile.Tower.InstanceId != towerInstanceId) continue;
                tileIndex = tile.Index;
                definitionId = tile.Tower.DefinitionId;
                return true;
            }
            tileIndex = -1;
            definitionId = null;
            return false;
        }
    }
}
