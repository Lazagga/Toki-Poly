using System.Collections;
using System.Collections.Generic;
using System;
using GaeBullBing.Core;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.Audio;
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

        [Header("Tile Illumination")]
        [SerializeField, Range(.05f, .45f)] private float illuminationFlashEnd = .18f;
        [SerializeField, Range(.2f, .9f)] private float illuminationHoldEnd = .58f;
        [SerializeField, Range(.5f, 1.2f)] private float illuminationStartScale = .96f;
        [SerializeField, Range(1f, 1.3f)] private float illuminationPeakScale = 1.05f;

        [Header("Experimental Area Effects")]
        [SerializeField] private bool useExperimentalAreaEffects = true;
        [SerializeField, Min(.05f)] private float experimentalAreaDuration = .34f;
        [SerializeField, Min(.1f)] private float experimentalEllipseWidth = 1.25f;
        [SerializeField] private Vector3 experimentalAreaOffset = new(0f, .05f, 0f);
        [SerializeField] private Color experimentalFireColor = new(1f, .18f, .04f, 1f);
        [SerializeField] private Color experimentalIceColor = new(.15f, .65f, 1f, 1f);
        [SerializeField] private Color experimentalElectricColor = new(.65f, .2f, 1f, 1f);

        [Header("Experimental Fire")]
        [SerializeField, Min(0f)] private float experimentalFireRiseHeight = .72f;
        [SerializeField, Min(0f)] private float experimentalFireSwayDistance = .07f;

        [Header("Experimental Ice")]
        [SerializeField, Range(.1f, .9f)] private float experimentalIceGrowEnd = .68f;
        [SerializeField, Range(.1f, .95f)] private float experimentalIceShatterStart = .78f;
        [SerializeField, Range(4, 20)] private int experimentalIceFragmentCount = 10;
        [SerializeField, Min(0f)] private float experimentalIceShatterDistance = .36f;
        [SerializeField, Min(0f)] private float experimentalIceShatterRotation = 120f;
        [SerializeField, Min(0f)] private float experimentalIceFragmentGravity = .18f;

        [Header("Experimental Electric")]
        [SerializeField, Min(.01f)] private float experimentalElectricBoltWidth = .10f;
        [SerializeField, Range(1f, 5f)] private float experimentalElectricGlowWidth = 2.4f;
        [SerializeField, Min(.01f)] private float experimentalElectricChainTileDuration = .23f;
        [SerializeField, Min(.01f)] private float experimentalElectricEndpointSize = .16f;
        [SerializeField, Min(0f)] private float experimentalElectricJitter = .045f;
        [SerializeField, Min(.05f)] private float experimentalElectricChainResetDelay = .4f;

        private static Sprite proceduralEllipseSprite;
        private static Sprite proceduralFlameSprite;
        private static Sprite proceduralIceShardSprite;
        private static Sprite proceduralLightningSprite;
        private static Sprite proceduralEllipseFillSprite;
        private readonly Dictionary<int, ElectricChainState> electricChainStates = new();
        private IReadOnlyList<TowerDefinition> towerDefinitions;

        public Sprite PhysicsAttackSprite => physicsAttackSprite;

        public void Initialize(
            BoardTilemapView view,
            IReadOnlyList<TowerDefinition> definitions = null)
        {
            boardView = view;
            towerDefinitions = definitions;
        }

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
                PlayAttackSound(definitionId, towerTileIndex);
                if (useExperimentalAreaEffects)
                {
                    electricChainStates.Remove(result.TowerInstanceId);
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
                PlayAttackSound(definitionId, towerTileIndex);
                if (useExperimentalAreaEffects && definitionId == "TOW_04")
                {
                    yield return PlayExperimentalAreaEffect(
                        ExperimentalAreaKind.Electric,
                        GetElectricChainTilePath(
                            result.TowerInstanceId,
                            result.TargetTileIndex),
                        onImpact,
                        experimentalElectricChainTileDuration);
                    yield break;
                }
                var chainSprite = GetAreaTileSprite(definitionId);
                if (chainSprite == null) onImpact?.Invoke();
                else yield return PlayTileIllumination(chainSprite, new[] { result.TargetTileIndex },
                    $"{definitionId} Chain Tile", onImpact, .08f,
                    GetIlluminationColor(definitionId));
                yield break;
            }

            
if (result.VisualKind == TowerAttackVisualKind.AreaTile && result.TargetTileIndex >= 0)
            {
                yield return PlayAreaTiles(state, result.TowerInstanceId,
                    new[] { result.TargetTileIndex }, onImpact, false);
                yield break;
            }

            if (result.VisualKind != TowerAttackVisualKind.Projectile || result.TargetTileIndex < 0)
            {
                onImpact?.Invoke();
                yield break;
            }

            var sprite = GetAttackSprite(definitionId);
            PlayAttackSound(definitionId, towerTileIndex);
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
                onImpact,
                illuminationColor: GetIlluminationColor("TOW_04"));
        }

        public IEnumerator PlayAreaTiles(
            GameState state,
            int towerInstanceId,
            IReadOnlyList<int> tileIndices,
            Action onImpact = null,
            bool playSound = true)
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
            if (playSound && TryFindTower(state, towerInstanceId, out var towerTileIndex, out _))
                PlayAttackSound(definitionId, towerTileIndex);
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
            yield return PlayTileIllumination(
                sprite,
                tileIndices,
                $"{definitionId} Area Tile",
                onImpact,
                illuminationColor: GetIlluminationColor(definitionId));
        }

        private IEnumerator PlayExperimentalAreaEffect(
            ExperimentalAreaKind kind,
            IReadOnlyList<int> tileIndices,
            Action onImpact,
            float duration = -1f)
        {
            EnsureProceduralSprites();
            var effects = CreateExperimentalAreaEffects(kind, tileIndices);
            var electricConnections = kind == ExperimentalAreaKind.Electric
                ? CreateElectricConnections(tileIndices)
                : new List<ElectricConnectionEffect>();
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
                foreach (var connection in electricConnections)
                    UpdateElectricConnection(connection, progress);
                yield return null;
            }

            if (!impactInvoked)
                onImpact?.Invoke();
            foreach (var effect in effects)
                if (effect.Root != null)
                    Destroy(effect.Root);
            foreach (var connection in electricConnections)
                if (connection.Root != null)
                    Destroy(connection.Root);
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

                var accentCount = kind == ExperimentalAreaKind.Electric ? 0 : 5;
                for (var index = 0; index < accentCount; index++)
                {
                    effect.Accents.Add(CreateEffectRenderer(
                        root.transform,
                        GetProceduralAccentSprite(kind),
                        sortingOrder + 1));
                }
                if (kind == ExperimentalAreaKind.Ice)
                {
                    for (var index = 0; index < experimentalIceFragmentCount; index++)
                    {
                        var fragment = CreateEffectRenderer(
                            root.transform,
                            proceduralIceShardSprite,
                            sortingOrder + 2);
                        fragment.color = Color.clear;
                        effect.Fragments.Add(fragment);
                    }
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
                        start +
                        Vector3.up * Mathf.Lerp(0f, experimentalFireRiseHeight, eased) +
                        Vector3.right *
                        Mathf.Sin((progress * 2.5f + index * .37f) * Mathf.PI) *
                        experimentalFireSwayDistance;
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
                var growEnd = Mathf.Min(
                    experimentalIceGrowEnd,
                    experimentalIceShatterStart - .01f);
                var grow = Mathf.Clamp01(progress / Mathf.Max(.01f, growEnd));
                var shatter = Mathf.Clamp01(
                    (progress - experimentalIceShatterStart) /
                    Mathf.Max(.01f, 1f - experimentalIceShatterStart));
                for (var index = 0; index < effect.Accents.Count; index++)
                {
                    var angle = index * Mathf.PI * 2f / effect.Accents.Count;
                    var direction = new Vector3(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle) * .5f,
                        0f);
                    var accent = effect.Accents[index];
                    accent.transform.localPosition =
                        direction * Mathf.Lerp(.12f, .43f, grow);
                    accent.transform.localRotation =
                        Quaternion.Euler(
                            0f,
                            0f,
                            -angle * Mathf.Rad2Deg);
                    accent.transform.localScale = new Vector3(
                        Mathf.Lerp(.25f, .65f, grow),
                        Mathf.Lerp(.2f, .75f, grow),
                        1f);
                    SetRendererAlpha(accent, color, shatter > 0f ? 0f : 1f);
                }

                for (var index = 0; index < effect.Fragments.Count; index++)
                {
                    var fragment = effect.Fragments[index];
                    if (shatter <= 0f)
                    {
                        fragment.color = Color.clear;
                        continue;
                    }

                    var angle = index * Mathf.PI * 2f / effect.Fragments.Count +
                                (index % 3) * .17f;
                    var speedVariation = .78f + (index * 37 % 29) / 100f;
                    var direction = new Vector3(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle) * .58f,
                        0f);
                    fragment.transform.localPosition =
                        direction *
                        (.2f +
                         shatter *
                         experimentalIceShatterDistance *
                         speedVariation) +
                        Vector3.down *
                        (shatter * shatter * experimentalIceFragmentGravity);
                    fragment.transform.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        angle * Mathf.Rad2Deg +
                        shatter *
                        experimentalIceShatterRotation *
                        speedVariation *
                        (index % 2 == 0 ? 1f : -1f));
                    var fragmentScale = Mathf.Lerp(.34f, .08f, shatter) *
                                        speedVariation;
                    fragment.transform.localScale = new Vector3(
                        fragmentScale * .7f,
                        fragmentScale,
                        1f);
                    var fragmentColor = Color.Lerp(
                        Color.white,
                        color,
                        Mathf.Clamp01(shatter / .18f));
                    SetRendererAlpha(fragment, fragmentColor, 1f - shatter);
                }
                return;
            }

        }

        private List<ElectricConnectionEffect> CreateElectricConnections(
            IReadOnlyList<int> tileIndices)
        {
            var connections = new List<ElectricConnectionEffect>();
            var orderedTiles = new List<int>();
            var uniqueTiles = new HashSet<int>();
            foreach (var tileIndex in tileIndices)
                if (uniqueTiles.Add(tileIndex))
                    orderedTiles.Add(tileIndex);

            for (var index = 1; index < orderedTiles.Count; index++)
            {
                var start = boardView.GetWorldPosition(orderedTiles[index - 1]) +
                            experimentalAreaOffset;
                var end = boardView.GetWorldPosition(orderedTiles[index]) +
                          experimentalAreaOffset;
                var root = new GameObject(
                    $"Electric Connection ({orderedTiles[index - 1]}-{orderedTiles[index]})");
                root.transform.SetParent(transform, false);
                var sortingOrder = BoardDepthSorting.GetOrder((start + end) * .5f, -40);
                connections.Add(new ElectricConnectionEffect
                {
                    Root = root,
                    GlowRenderer = CreateEffectRenderer(
                        root.transform,
                        proceduralLightningSprite,
                        sortingOrder),
                    CoreRenderer = CreateEffectRenderer(
                        root.transform,
                        proceduralLightningSprite,
                        sortingOrder + 1),
                    StartPointRenderer = CreateEffectRenderer(
                        root.transform,
                        proceduralEllipseFillSprite,
                        sortingOrder + 2),
                    EndPointRenderer = CreateEffectRenderer(
                        root.transform,
                        proceduralEllipseFillSprite,
                        sortingOrder + 2),
                    Start = start,
                    End = end,
                    Phase = index * 1.73f
                });
            }
            return connections;
        }

        private void UpdateElectricConnection(
            ElectricConnectionEffect connection,
            float progress)
        {
            var doubleBlink = Mathf.Abs(Mathf.Cos(progress * Mathf.PI * 2f));
            var fade = 1f - Mathf.Clamp01((progress - .72f) / .28f);
            var alpha = doubleBlink * fade;
            SetRendererAlpha(
                connection.GlowRenderer,
                experimentalElectricColor,
                alpha * .55f);
            SetRendererAlpha(
                connection.CoreRenderer,
                Color.white,
                alpha);
            SetRendererAlpha(
                connection.StartPointRenderer,
                experimentalElectricColor,
                alpha);
            SetRendererAlpha(
                connection.EndPointRenderer,
                experimentalElectricColor,
                alpha);

            var direction = connection.End - connection.Start;
            var length = direction.magnitude;
            if (length <= Mathf.Epsilon)
                return;

            var perpendicular = new Vector3(-direction.y, direction.x, 0f).normalized;
            var jitter = Mathf.Sin(progress * Mathf.PI * 12f + connection.Phase) *
                         experimentalElectricJitter;
            PositionElectricBolt(
                connection.GlowRenderer.transform,
                connection.Start,
                connection.End,
                perpendicular * jitter,
                experimentalElectricBoltWidth * experimentalElectricGlowWidth);
            PositionElectricBolt(
                connection.CoreRenderer.transform,
                connection.Start,
                connection.End,
                perpendicular * jitter * .35f,
                experimentalElectricBoltWidth);

            var endpointScale = experimentalElectricEndpointSize /
                                Mathf.Max(
                                    .001f,
                                    proceduralEllipseFillSprite.bounds.size.x);
            connection.StartPointRenderer.transform.position = connection.Start;
            connection.StartPointRenderer.transform.localScale =
                Vector3.one * endpointScale;
            connection.EndPointRenderer.transform.position = connection.End;
            connection.EndPointRenderer.transform.localScale =
                Vector3.one * endpointScale;
        }

        private static void PositionElectricBolt(
            Transform target,
            Vector3 start,
            Vector3 end,
            Vector3 offset,
            float width)
        {
            var direction = end - start;
            var length = direction.magnitude;
            target.position = (start + end) * .5f + offset;
            target.rotation = Quaternion.Euler(
                0f,
                0f,
                Vector2.SignedAngle(
                    Vector2.up,
                    new Vector2(direction.x, direction.y)));
            target.localScale = new Vector3(
                width /
                Mathf.Max(.001f, proceduralLightningSprite.bounds.size.x),
                length /
                Mathf.Max(.001f, proceduralLightningSprite.bounds.size.y),
                1f);
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

        private IReadOnlyList<int> GetElectricChainTilePath(
            int towerInstanceId,
            int currentTileIndex)
        {
            var path = new List<int>();
            if (electricChainStates.TryGetValue(towerInstanceId, out var previous) &&
                Time.unscaledTime - previous.LastUpdateTime <=
                experimentalElectricChainResetDelay &&
                previous.TileIndex ==
                (currentTileIndex + 1) %
                GaeBullBing.Core.Board.BoardState.DefaultTileCount)
            {
                path.Add(previous.TileIndex);
            }
            path.Add(currentTileIndex);
            electricChainStates[towerInstanceId] = new ElectricChainState
            {
                TileIndex = currentTileIndex,
                LastUpdateTime = Time.unscaledTime
            };
            return path;
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
            if (proceduralEllipseFillSprite == null)
                proceduralEllipseFillSprite = CreateEllipseFillSprite();
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

        private static Sprite CreateEllipseFillSprite()
        {
            return CreateMaskedSprite(
                "Procedural Electric Endpoint",
                48,
                24,
                (x, y) => x * x + y * y < .82f);
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
            public List<SpriteRenderer> Fragments { get; } = new();
        }

        private sealed class ElectricConnectionEffect
        {
            public GameObject Root;
            public SpriteRenderer GlowRenderer;
            public SpriteRenderer CoreRenderer;
            public SpriteRenderer StartPointRenderer;
            public SpriteRenderer EndPointRenderer;
            public Vector3 Start;
            public Vector3 End;
            public float Phase;
        }

        private struct ElectricChainState
        {
            public int TileIndex;
            public float LastUpdateTime;
        }

        private sealed class IlluminationView
        {
            public SpriteRenderer Renderer;
            public Vector3 BaseScale;
        }

        private IEnumerator PlayTileIllumination(
            Sprite sprite,
            IReadOnlyList<int> tileIndices,
            string objectName,
            Action onImpact = null,
            float duration = -1f,
            Color? illuminationColor = null)
        {
            var effectDuration = duration > 0f ? duration : chainLineDuration;
            var views = new List<IlluminationView>();
            var uniqueTiles = new HashSet<int>();
            for (var index = 0; index < tileIndices.Count; index++)
            {
                var tileIndex = tileIndices[index];
                if (!uniqueTiles.Add(tileIndex))
                    continue;

                var effectObject = new GameObject($"{objectName} ({tileIndex})");
                effectObject.transform.SetParent(transform, false);
                effectObject.transform.position =
                    boardView.GetWorldPosition(tileIndex) + chainTileOffset;
                var spriteWidth = sprite.bounds.size.x;
                var baseScale = Vector3.one *
                    (spriteWidth > .001f
                        ? areaTileScale / spriteWidth
                        : areaTileScale);
                effectObject.transform.localScale =
                    baseScale * illuminationStartScale;
                var renderer = effectObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = new Color(1f, 1f, 1f, 0f);
                renderer.sortingOrder = BoardDepthSorting.GetOrder(
                    boardView.GetWorldPosition(tileIndex),
                    -80);
                views.Add(new IlluminationView
                {
                    Renderer = renderer,
                    BaseScale = baseScale
                });
            }

            var impactInvoked = false;
            var targetColor = illuminationColor ?? Color.white;
            var holdEnd = Mathf.Max(illuminationFlashEnd + .01f, illuminationHoldEnd);
            for (var elapsed = 0f; elapsed < effectDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / effectDuration);
                if (!impactInvoked && progress >= illuminationFlashEnd)
                {
                    impactInvoked = true;
                    onImpact?.Invoke();
                }

                Color tint;
                float alpha;
                float scale;
                if (progress < illuminationFlashEnd)
                {
                    var phase = Mathf.SmoothStep(
                        0f,
                        1f,
                        progress / Mathf.Max(.01f, illuminationFlashEnd));
                    tint = Color.white;
                    alpha = phase;
                    scale = Mathf.Lerp(
                        illuminationStartScale,
                        illuminationPeakScale,
                        phase);
                }
                else if (progress < holdEnd)
                {
                    var phase = Mathf.Clamp01(
                        (progress - illuminationFlashEnd) /
                        Mathf.Max(.01f, holdEnd - illuminationFlashEnd));
                    tint = Color.Lerp(Color.white, targetColor, phase);
                    alpha = 1f;
                    scale = illuminationPeakScale;
                }
                else
                {
                    var phase = Mathf.SmoothStep(
                        0f,
                        1f,
                        (progress - holdEnd) / Mathf.Max(.01f, 1f - holdEnd));
                    tint = targetColor;
                    alpha = 1f - phase;
                    scale = Mathf.Lerp(illuminationPeakScale, 1f, phase);
                }

                foreach (var view in views)
                {
                    view.Renderer.color = new Color(
                        tint.r,
                        tint.g,
                        tint.b,
                        alpha);
                    view.Renderer.transform.localScale = view.BaseScale * scale;
                }
                yield return null;
            }
            if (!impactInvoked)
                onImpact?.Invoke();
            foreach (var view in views)
                if (view.Renderer != null)
                    Destroy(view.Renderer.gameObject);
        }

        private static Color GetIlluminationColor(string definitionId)
        {
            return definitionId switch
            {
                "TOW_01" => new Color(1f, .18f, .04f),
                "TOW_02" => new Color(.15f, .65f, 1f),
                "TOW_03" => new Color(.2f, .85f, .35f),
                "TOW_04" => new Color(.65f, .2f, 1f),
                _ => Color.white
            };
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

        private void PlayAttackSound(string definitionId, int towerTileIndex)
        {
            if (towerDefinitions == null || boardView == null)
                return;
            TowerDefinition definition = null;
            for (var index = 0; index < towerDefinitions.Count; index++)
                if (towerDefinitions[index] != null &&
                    towerDefinitions[index].Id == definitionId)
                {
                    definition = towerDefinitions[index];
                    break;
                }
            if (definition == null)
                return;

            var audio = AudioManager.Instance;
            if (audio == null)
                return;
            var clip = definition.Element switch
            {
                TowerElement.Fire => audio.Tower.FireAttack,
                TowerElement.Ice => audio.Tower.IceAttack,
                TowerElement.Electric => audio.Tower.ElectricAttack,
                TowerElement.Physics => audio.Tower.PhysicsAttack,
                _ => null
            };
            audio.PlayAt(clip, boardView.GetWorldPosition(towerTileIndex));
        }
    }
}
