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

        [Header("Experimental Area Effects")]
        [SerializeField] private bool useExperimentalAreaEffects = true;
        [SerializeField, Min(.05f)] private float experimentalAreaDuration = .34f;
        [SerializeField, Min(.1f)] private float experimentalEllipseWidth = 1.25f;
        [SerializeField] private Vector3 experimentalAreaOffset = new(0f, .05f, 0f);
        [SerializeField] private Color experimentalFireColor = new(1f, .18f, .04f, 1f);
        [SerializeField] private Color experimentalIceColor = new(.15f, .65f, 1f, 1f);
        [SerializeField] private Color experimentalElectricColor = new(.65f, .2f, 1f, 1f);

        private static Sprite proceduralEllipseSprite;
        private static Sprite proceduralFlameSprite;
        private static Sprite proceduralIceShardSprite;
        private static Sprite proceduralLightningSprite;

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
                if (useExperimentalAreaEffects)
                {
                    yield return PlayExperimentalAreaEffect(
                        ExperimentalAreaKind.Electric,
                        GetLineTileIndices(towerTileIndex),
                        onImpact);
                    yield break;
                }
                yield return PlayChainLine(towerTileIndex, onImpact);
                yield break;
            }

            if (result.VisualKind == TowerAttackVisualKind.ChainTile && result.TargetTileIndex >= 0)
            {
                if (useExperimentalAreaEffects && definitionId == "TOW_04")
                {
                    yield return PlayExperimentalAreaEffect(
                        ExperimentalAreaKind.Electric,
                        new[] { result.TargetTileIndex },
                        onImpact,
                        .12f);
                    yield break;
                }
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
            yield return PlayTileIllumination(
                electricChainLineSprite,
                GetLineTileIndices(towerTileIndex),
                "Electric Chain Line",
                onImpact);
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
            if (useExperimentalAreaEffects &&
                TryGetExperimentalAreaKind(definitionId, out var areaKind))
            {
                yield return PlayExperimentalAreaEffect(areaKind, tileIndices, onImpact);
                yield break;
            }
            if (sprite == null)
            {
                onImpact?.Invoke();
                yield break;
            }
            yield return PlayTileIllumination(sprite, tileIndices, $"{definitionId} Area Tile", onImpact);
        }

        private IEnumerator PlayExperimentalAreaEffect(
            ExperimentalAreaKind kind,
            IReadOnlyList<int> tileIndices,
            Action onImpact,
            float duration = -1f)
        {
            EnsureProceduralSprites();
            var effects = CreateExperimentalAreaEffects(kind, tileIndices);
            var impactInvoked = false;
            var effectDuration = duration > 0f ? duration : experimentalAreaDuration;

            for (var elapsed = 0f; elapsed < effectDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / effectDuration);
                if (!impactInvoked && progress >= .5f)
                {
                    impactInvoked = true;
                    onImpact?.Invoke();
                }

                foreach (var effect in effects)
                    UpdateExperimentalAreaEffect(effect, kind, progress);
                yield return null;
            }

            if (!impactInvoked)
                onImpact?.Invoke();
            foreach (var effect in effects)
                if (effect.Root != null)
                    Destroy(effect.Root);
        }

        private List<ExperimentalAreaEffect> CreateExperimentalAreaEffects(
            ExperimentalAreaKind kind,
            IReadOnlyList<int> tileIndices)
        {
            var effects = new List<ExperimentalAreaEffect>();
            var uniqueTiles = new HashSet<int>();
            foreach (var tileIndex in tileIndices)
            {
                if (!uniqueTiles.Add(tileIndex))
                    continue;

                var root = new GameObject($"{kind} Experimental Area ({tileIndex})");
                root.transform.SetParent(transform, false);
                var position = boardView.GetWorldPosition(tileIndex) + experimentalAreaOffset;
                root.transform.position = position;
                // Above the tile (-100) and its overlays, but below towers/actors.
                var sortingOrder = BoardDepthSorting.GetOrder(position, -50);
                var effect = new ExperimentalAreaEffect
                {
                    Root = root,
                    Ring = CreateEffectRenderer(
                        root.transform,
                        proceduralEllipseSprite,
                        sortingOrder)
                };

                var accentCount = kind == ExperimentalAreaKind.Electric ? 7 : 5;
                for (var index = 0; index < accentCount; index++)
                {
                    effect.Accents.Add(CreateEffectRenderer(
                        root.transform,
                        GetProceduralAccentSprite(kind),
                        sortingOrder + 1));
                }

                InitializeExperimentalAccents(effect, kind);
                effects.Add(effect);
            }
            return effects;
        }

        private void InitializeExperimentalAccents(
            ExperimentalAreaEffect effect,
            ExperimentalAreaKind kind)
        {
            for (var index = 0; index < effect.Accents.Count; index++)
            {
                var accent = effect.Accents[index].transform;
                if (kind == ExperimentalAreaKind.Fire)
                {
                    var normalized = effect.Accents.Count <= 1
                        ? 0f
                        : index / (float)(effect.Accents.Count - 1);
                    accent.localPosition = new Vector3(
                        Mathf.Lerp(-.42f, .42f, normalized),
                        -.08f + (index % 2) * .04f,
                        0f);
                    accent.localRotation = Quaternion.Euler(0f, 0f, -12f + index * 6f);
                }
                else if (kind == ExperimentalAreaKind.Ice)
                {
                    var angle = index * Mathf.PI * 2f / effect.Accents.Count;
                    accent.localPosition = new Vector3(
                        Mathf.Cos(angle) * .12f,
                        Mathf.Sin(angle) * .06f,
                        0f);
                    accent.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        -angle * Mathf.Rad2Deg);
                }
                else
                {
                    var normalized = effect.Accents.Count <= 1
                        ? 0f
                        : index / (float)(effect.Accents.Count - 1);
                    accent.localPosition = new Vector3(
                        Mathf.Lerp(-.48f, .48f, normalized),
                        index % 2 == 0 ? .12f : -.12f,
                        0f);
                    accent.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        index % 2 == 0 ? 25f : -25f);
                }
            }
        }

        private void UpdateExperimentalAreaEffect(
            ExperimentalAreaEffect effect,
            ExperimentalAreaKind kind,
            float progress)
        {
            var color = GetExperimentalAreaColor(kind);
            var eased = Mathf.SmoothStep(0f, 1f, progress);
            var ringScale = experimentalEllipseWidth / proceduralEllipseSprite.bounds.size.x;
            SetRendererAlpha(effect.Ring, color, Mathf.Sin(progress * Mathf.PI));
            effect.Ring.transform.localScale =
                Vector3.one * ringScale * Mathf.Lerp(.45f, 1.12f, eased);

            if (kind == ExperimentalAreaKind.Fire)
            {
                for (var index = 0; index < effect.Accents.Count; index++)
                {
                    var accent = effect.Accents[index];
                    var start = new Vector3(
                        Mathf.Lerp(-.42f, .42f, index / 4f),
                        -.08f + (index % 2) * .04f,
                        0f);
                    accent.transform.localPosition =
                        start + Vector3.up * Mathf.Lerp(0f, .42f, eased);
                    accent.transform.localScale = new Vector3(
                        .45f,
                        Mathf.Lerp(.35f, .7f, Mathf.Sin(progress * Mathf.PI)),
                        1f);
                    SetRendererAlpha(accent, color, 1f - eased);
                }
                return;
            }

            if (kind == ExperimentalAreaKind.Ice)
            {
                var grow = Mathf.Clamp01(progress / .72f);
                var shatter = Mathf.Clamp01((progress - .72f) / .28f);
                for (var index = 0; index < effect.Accents.Count; index++)
                {
                    var angle = index * Mathf.PI * 2f / effect.Accents.Count;
                    var direction = new Vector3(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle) * .5f,
                        0f);
                    var accent = effect.Accents[index];
                    accent.transform.localPosition =
                        direction * Mathf.Lerp(.12f, .43f + shatter * .16f, grow);
                    accent.transform.localScale = new Vector3(
                        Mathf.Lerp(.25f, .65f, grow) * (1f - shatter),
                        Mathf.Lerp(.2f, .75f, grow) * (1f - shatter),
                        1f);
                    SetRendererAlpha(accent, color, 1f - shatter);
                }
                return;
            }

            var doubleBlink = Mathf.Abs(Mathf.Sin(progress * Mathf.PI * 2f));
            var fade = 1f - Mathf.Clamp01((progress - .65f) / .35f);
            foreach (var accent in effect.Accents)
            {
                accent.transform.localScale = new Vector3(.55f, .55f, 1f);
                SetRendererAlpha(accent, color, doubleBlink * fade);
            }
        }

        private static SpriteRenderer CreateEffectRenderer(
            Transform parent,
            Sprite sprite,
            int sortingOrder)
        {
            var effectObject = new GameObject("Effect Part");
            effectObject.transform.SetParent(parent, false);
            var renderer = effectObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static void SetRendererAlpha(
            SpriteRenderer renderer,
            Color color,
            float alpha)
        {
            renderer.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        private Color GetExperimentalAreaColor(ExperimentalAreaKind kind)
        {
            return kind switch
            {
                ExperimentalAreaKind.Fire => experimentalFireColor,
                ExperimentalAreaKind.Ice => experimentalIceColor,
                ExperimentalAreaKind.Electric => experimentalElectricColor,
                _ => Color.white
            };
        }

        private static bool TryGetExperimentalAreaKind(
            string definitionId,
            out ExperimentalAreaKind kind)
        {
            switch (definitionId)
            {
                case "TOW_01":
                    kind = ExperimentalAreaKind.Fire;
                    return true;
                case "TOW_02":
                    kind = ExperimentalAreaKind.Ice;
                    return true;
                case "TOW_04":
                    kind = ExperimentalAreaKind.Electric;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static IReadOnlyList<int> GetLineTileIndices(int towerTileIndex)
        {
            var line = MonsterService.GetLine(towerTileIndex);
            var tileIndices = new List<int>();
            for (var tileIndex = 0;
                 tileIndex < GaeBullBing.Core.Board.BoardState.DefaultTileCount;
                 tileIndex++)
            {
                if (MonsterService.GetLine(tileIndex) == line)
                    tileIndices.Add(tileIndex);
            }
            return tileIndices;
        }

        private static void EnsureProceduralSprites()
        {
            if (proceduralEllipseSprite == null)
                proceduralEllipseSprite = CreateEllipseSprite();
            if (proceduralFlameSprite == null)
                proceduralFlameSprite = CreateFlameSprite();
            if (proceduralIceShardSprite == null)
                proceduralIceShardSprite = CreateIceShardSprite();
            if (proceduralLightningSprite == null)
                proceduralLightningSprite = CreateLightningSprite();
        }

        private static Sprite GetProceduralAccentSprite(ExperimentalAreaKind kind)
        {
            return kind switch
            {
                ExperimentalAreaKind.Fire => proceduralFlameSprite,
                ExperimentalAreaKind.Ice => proceduralIceShardSprite,
                ExperimentalAreaKind.Electric => proceduralLightningSprite,
                _ => proceduralFlameSprite
            };
        }

        private static Sprite CreateEllipseSprite()
        {
            const int width = 128;
            const int height = 64;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Procedural Area Ellipse",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var normalizedX = (x + .5f - width * .5f) / (width * .5f);
                    var normalizedY = (y + .5f - height * .5f) / (height * .5f);
                    var distance = Mathf.Sqrt(
                        normalizedX * normalizedX + normalizedY * normalizedY);
                    var edgeDistance = Mathf.Abs(distance - .78f);
                    var edgeProgress = Mathf.Clamp01(
                        (edgeDistance - .07f) / (.15f - .07f));
                    edgeProgress =
                        edgeProgress * edgeProgress * (3f - 2f * edgeProgress);
                    var alpha = 1f - edgeProgress;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(.5f, .5f),
                100f);
        }

        private static Sprite CreateFlameSprite()
        {
            return CreateMaskedSprite(
                "Procedural Flame",
                32,
                48,
                (x, y) =>
                {
                    var normalizedX = Mathf.Abs(x);
                    var normalizedY = (y + 1f) * .5f;
                    var width = Mathf.Sin(normalizedY * Mathf.PI) * .7f +
                                (1f - normalizedY) * .18f;
                    var outer = normalizedX < width && normalizedY < 1f;
                    var innerCut = normalizedY < .42f &&
                                   normalizedX < (.42f - normalizedY) * .28f;
                    return outer && !innerCut;
                });
        }

        private static Sprite CreateIceShardSprite()
        {
            return CreateMaskedSprite(
                "Procedural Ice Shard",
                28,
                52,
                (x, y) => Mathf.Abs(x) * 1.35f + Mathf.Abs(y) < .92f);
        }

        private static Sprite CreateLightningSprite()
        {
            var points = new[]
            {
                new Vector2(-.2f, 1f),
                new Vector2(.12f, .35f),
                new Vector2(-.08f, .15f),
                new Vector2(.22f, -.25f),
                new Vector2(-.18f, -1f)
            };
            return CreateMaskedSprite(
                "Procedural Lightning",
                36,
                64,
                (x, y) =>
                {
                    var point = new Vector2(x, y);
                    var distance = float.MaxValue;
                    for (var index = 0; index < points.Length - 1; index++)
                    {
                        distance = Mathf.Min(
                            distance,
                            DistanceToSegment(point, points[index], points[index + 1]));
                    }
                    return distance < .105f;
                });
        }

        private static Sprite CreateMaskedSprite(
            string name,
            int width,
            int height,
            Func<float, float, bool> contains)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var normalizedX = (x + .5f) / width * 2f - 1f;
                    var normalizedY = (y + .5f) / height * 2f - 1f;
                    pixels[y * width + x] = contains(normalizedX, normalizedY)
                        ? Color.white
                        : Color.clear;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(.5f, .5f),
                64f);
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return Vector2.Distance(point, start);
            var projection = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * projection);
        }

        private enum ExperimentalAreaKind
        {
            Fire,
            Ice,
            Electric
        }

        private sealed class ExperimentalAreaEffect
        {
            public GameObject Root;
            public SpriteRenderer Ring;
            public List<SpriteRenderer> Accents { get; } = new();
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
