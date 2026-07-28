using System.Collections;
using System;

using GaeBullBing.Core.Monsters;
using GaeBullBing.Presentation.Board;
using UnityEngine;

namespace GaeBullBing.Presentation.Monsters
{
    public sealed class MonsterBoardView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float stepDuration = 0.18f;
        [SerializeField, Min(0f)] private float hopHeight = 0.18f;
        [SerializeField] private Vector3 positionOffset = new(0f, 0.22f, 0f);
        [SerializeField, Min(.05f)] private float spawnEntranceDuration = .45f;
        [SerializeField, Min(.5f)] private float spawnEntranceHeight = 3.2f;
        [Header("Regular Spawn Entrance")]
        [SerializeField, Min(.05f)] private float regularSpawnEntranceDuration = .38f;
        [SerializeField, Min(0f)] private float regularSpawnHopHeight = .22f;

        private BoardTilemapView boardView;
        private SpriteRenderer spriteRenderer;
        private SpriteRenderer healthBackgroundRenderer;
        
        private MonsterStatusIndicatorView statusIndicator;
        private Color monsterBaseColor = Color.white;
        private int hitFlashCount;
private SpriteRenderer healthFillRenderer;
        private BoardCharacterShadow shadow;
        private HoverMarkerView positionMarker;
        private Transform healthFill;
        private Coroutine layoutRoutine;
        private bool isMoving;
        private Vector3 visualHeightOffset = new(0f, .22f, 0f);
        private float healthBarY = .48f;
        private Sprite frontSprite;
        private Sprite backSprite;
        private Sprite flyingFrontSprite;
        private Sprite flyingBackSprite;
        private bool isBoss;
        private Vector3 shadowGroundPosition;
        private Vector3 transitionOffset;

        public int InstanceId { get; private set; }
        public Vector3 CameraFollowPosition { get; private set; }
        public Vector3 VisualCenterPosition => spriteRenderer != null
            ? spriteRenderer.bounds.center
            : transform.position;
        public event Action<int, int> TileChanged;
        public Vector3 RegularSpawnStartPosition { get; private set; }

        public void Initialize(
            int instanceId,
            BoardTilemapView view,
            int tileIndex,
            Vector3 baseVisualOffset,
            Sprite front,
            Sprite back,
            Sprite flightFront = null,
            Sprite flightBack = null,
            bool boss = false)
        {
            InstanceId = instanceId;
            CurrentTileIndex = tileIndex;
            boardView = view;
            visualHeightOffset = baseVisualOffset;
            positionOffset = baseVisualOffset;
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            frontSprite = front;
            backSprite = back != null ? back : front;
            flyingFrontSprite = flightFront != null ? flightFront : front;
            flyingBackSprite = flightBack != null ? flightBack : flyingFrontSprite;
            isBoss = boss;
            ApplyDirectionForDeparture(tileIndex);
            transform.position = GetStandingPosition(tileIndex);
            CameraFollowPosition = GetCameraTilePosition(tileIndex);
            shadowGroundPosition = GetShadowPosition(tileIndex);
            shadow = BoardCharacterShadow.Create(transform,
                spriteRenderer != null ? spriteRenderer.sortingLayerID : 0);
            if (spriteRenderer != null && spriteRenderer.sprite != null)
                healthBarY = Mathf.Max(.48f, spriteRenderer.sprite.bounds.max.y * spriteRenderer.transform.localScale.y + .08f);
            
            CreateHealthBar();
            statusIndicator = gameObject.AddComponent<MonsterStatusIndicatorView>();
            statusIndicator.Initialize();
            statusIndicator.SetLocalPosition(new Vector3(0f, healthBarY + .11f, 0f));
            if (isBoss)
                positionMarker = HoverMarkerView.Create(
                    transform,
                    new Vector3(0f, healthBarY + .42f, 0f),
                    new Color(.86f, .2f, 1f, 1f),
                    spriteRenderer != null ? spriteRenderer.sortingLayerID : 0);
        }

        public void SetLayoutOffset(Vector3 offset)
        {
            var targetOffset = offset + visualHeightOffset;
            if (layoutRoutine != null) StopCoroutine(layoutRoutine);
            layoutRoutine = StartCoroutine(AnimateLayoutOffset(targetOffset));
        }
        public void SetTransitionOffset(Vector3 offset)
        {
            transitionOffset = offset;
            if (isMoving || boardView == null) return;
            transform.position = GetStandingPosition(CurrentTileIndex);
            shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
        }
        private IEnumerator AnimateLayoutOffset(Vector3 targetOffset)
        {
            var startOffset = positionOffset;
            var elapsed = 0f;
            while (elapsed < .18f)
            {
                elapsed += Time.deltaTime;
                positionOffset = Vector3.Lerp(startOffset, targetOffset, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .18f)));
                if (!isMoving)
                    transform.position = GetStandingPosition(CurrentTileIndex);
                yield return null;
            }
            positionOffset = targetOffset;
            if (!isMoving)
                transform.position = GetStandingPosition(CurrentTileIndex);
            layoutRoutine = null;
        }
        public int CurrentTileIndex { get; private set; }
        public void SetVisible(bool visible)
        {
            if (spriteRenderer != null) spriteRenderer.enabled = visible;
            if (healthBackgroundRenderer != null) healthBackgroundRenderer.enabled = visible;
            
            statusIndicator?.SetVisible(visible);
if (healthFillRenderer != null) healthFillRenderer.enabled = visible;
            shadow?.SetVisible(visible);
            if (positionMarker != null) positionMarker.gameObject.SetActive(visible);
        }

        public void PrepareSpawnEntrance()
        {
            if (!isBoss || boardView == null) return;
            isMoving = true;
            ApplyBossEntranceDirection();
            SetSpawnDetailsVisible(false);
            if (spriteRenderer != null)
            {
                var color = monsterBaseColor;
                color.a = 0f;
                spriteRenderer.color = color;
            }
            transitionOffset = Vector3.up * spawnEntranceHeight;
            transform.position = GetStandingPosition(CurrentTileIndex);
            shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
        }

        public IEnumerator PlaySpawnEntrance()
        {
            if (!isBoss || boardView == null) yield break;
            var startOffset = transitionOffset;
            for (var elapsed = 0f; elapsed < spawnEntranceDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / spawnEntranceDuration);
                var smooth = Mathf.SmoothStep(0f, 1f, progress);
                transitionOffset = Vector3.Lerp(startOffset, Vector3.zero, smooth);
                transform.position = GetStandingPosition(CurrentTileIndex);
                shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
                if (spriteRenderer != null)
                {
                    var color = monsterBaseColor;
                    color.a = Mathf.Clamp01(smooth * 3f);
                    spriteRenderer.color = color;
                }
                yield return null;
            }

            transitionOffset = Vector3.zero;
            transform.position = GetStandingPosition(CurrentTileIndex);
            if (spriteRenderer != null) spriteRenderer.color = monsterBaseColor;
            boardView.PlayPress(CurrentTileIndex);
            for (var elapsed = 0f; elapsed < boardView.PressPulseDuration; elapsed += Time.deltaTime)
            {
                transform.position = GetStandingPosition(CurrentTileIndex);
                shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
                yield return null;
            }

            ApplyBossDirection(CurrentTileIndex, false);
            transform.position = GetStandingPosition(CurrentTileIndex);
            shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
            isMoving = false;
            SetSpawnDetailsVisible(true);
        }

        public void PrepareRegularSpawnEntrance()
        {
            if (isBoss || boardView == null)
                return;

            isMoving = true;
            ApplyDirectionForDeparture(0);
            SetSpawnDetailsVisible(false);

            var tileZero = GetTileGroundPosition(0);
            var tileOne = GetTileGroundPosition(1);
            var virtualMinusOne = tileZero - (tileOne - tileZero);
            RegularSpawnStartPosition = virtualMinusOne + positionOffset;
            transform.position = RegularSpawnStartPosition;
            shadowGroundPosition = virtualMinusOne + positionOffset - visualHeightOffset;

            if (spriteRenderer != null)
            {
                var color = monsterBaseColor;
                color.a = 0f;
                spriteRenderer.color = color;
            }
        }

        public IEnumerator PlayRegularSpawnEntrance()
        {
            if (isBoss || boardView == null)
                yield break;

            var start = RegularSpawnStartPosition;
            var destination = GetStandingPosition(0);
            for (var elapsed = 0f;
                 elapsed < regularSpawnEntranceDuration;
                 elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / regularSpawnEntranceDuration);
                var eased = Mathf.SmoothStep(0f, 1f, progress);
                var position = Vector3.Lerp(start, destination, eased);
                position.y += Mathf.Sin(progress * Mathf.PI) * regularSpawnHopHeight;
                transform.position = position;
                shadowGroundPosition = Vector3.Lerp(
                    start - visualHeightOffset,
                    destination - visualHeightOffset,
                    eased);

                if (spriteRenderer != null)
                {
                    var color = monsterBaseColor;
                    color.a = Mathf.Clamp01(progress * 2.5f);
                    spriteRenderer.color = color;
                }
                yield return null;
            }

            transform.position = destination;
            shadowGroundPosition = GetShadowPosition(0);
            if (spriteRenderer != null)
                spriteRenderer.color = monsterBaseColor;
            isMoving = false;
            SetSpawnDetailsVisible(true);
        }

        private void LateUpdate()
        {
            // Player tile presses are visual-only, so stationary monsters must follow
            // the pressed tile every frame without changing their logical tile index.
            if (!isMoving && boardView != null)
            {
                transform.position = GetStandingPosition(CurrentTileIndex);
                CameraFollowPosition = GetCameraTilePosition(CurrentTileIndex);
                shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
            }

            // The sprite is raised for readability, but depth belongs to its ground position.
            var order = BoardDepthSorting.GetActorOrder(
                transform.position - visualHeightOffset - GetTileVisualOffset(CurrentTileIndex),
                CurrentTileIndex);
            if (spriteRenderer != null) spriteRenderer.sortingOrder = order;
            if (healthBackgroundRenderer != null) healthBackgroundRenderer.sortingOrder = order + 1;
            
            statusIndicator?.SetSortingOrder(order + 3);
if (healthFillRenderer != null) healthFillRenderer.sortingOrder = order + 2;
            shadow?.Set(shadowGroundPosition);
        }

        public void UpdateHealth(float current, float max)
        {
            var ratio = Mathf.Clamp01(max > 0 ? (float)current / max : 0f);
            healthFill.localScale = new Vector3(.46f * ratio, .055f, 1f);
            healthFill.localPosition = new Vector3(-.23f + .23f * ratio, healthBarY, 0f);
        }

public void UpdateStatus(MonsterState state)
        {
            if (state == null) return;
            statusIndicator?.Refresh(state);
            monsterBaseColor = state.FrozenMovesRemaining > 0 || state.FreezeImmunityPending
                ? new Color(.62f, .82f, 1f, 1f)
                : Color.white;
            if (spriteRenderer != null && hitFlashCount == 0)
                spriteRenderer.color = monsterBaseColor;
        }


        private void CreateHealthBar()
        {
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            var back = new GameObject("Health Background"); back.transform.SetParent(transform, false); back.transform.localPosition = new Vector3(0,healthBarY); back.transform.localScale = new Vector3(.5f,.075f,1);
            healthBackgroundRenderer = back.AddComponent<SpriteRenderer>(); healthBackgroundRenderer.sprite = sprite; healthBackgroundRenderer.color = new Color(.12f,.12f,.12f,.9f);
            var fill = new GameObject("Health Fill"); fill.transform.SetParent(transform, false); healthFill = fill.transform;
            healthFillRenderer = fill.AddComponent<SpriteRenderer>(); healthFillRenderer.sprite = sprite; healthFillRenderer.color = new Color(.2f,.9f,.25f);
        }

        public IEnumerator MoveSteps(int startTileIndex, int distance)
        {
            isMoving = true;
            for (var step = 1; step <= distance; step++)
            {
                var fromIndex = (startTileIndex + step - 1) % GaeBullBing.Core.Board.BoardState.DefaultTileCount;
                var toIndex = (startTileIndex + step) % GaeBullBing.Core.Board.BoardState.DefaultTileCount;
                var from = GetTileGroundPosition(fromIndex);
                var to = GetTileGroundPosition(toIndex);
                ApplyDirectionForDeparture(fromIndex);
                var previousTileIndex = CurrentTileIndex;
                CurrentTileIndex = toIndex;
                TileChanged?.Invoke(previousTileIndex, toIndex);
                var elapsed = 0f;

                while (elapsed < stepDuration)
                {
                    elapsed += Time.deltaTime;
                    var progress = Mathf.Clamp01(elapsed / stepDuration);
                    var position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, progress)) + positionOffset;
                    shadowGroundPosition = position - visualHeightOffset;
                    position.y += Mathf.Sin(progress * Mathf.PI) * hopHeight;
                    transform.position = position;
                    yield return null;
                }
                transform.position = to + positionOffset;
                shadowGroundPosition = to + positionOffset - visualHeightOffset;
            }
            isMoving = false;
            transform.position = GetStandingPosition(CurrentTileIndex);
        }

        public IEnumerator MoveFlying(int startTileIndex, int distance, bool reachedBase)
        {
            transitionOffset = Vector3.zero;
            if (distance <= 0)
            {
                isMoving = false;
                CurrentTileIndex = startTileIndex;
                CameraFollowPosition = GetCameraTilePosition(startTileIndex);
                ApplyBossDirection(startTileIndex, false);
                transform.position = GetStandingPosition(startTileIndex);
                shadowGroundPosition = GetShadowPosition(startTileIndex);
                yield break;
            }
            isMoving = true;
            var targetTileIndex = (startTileIndex + distance) %
                GaeBullBing.Core.Board.BoardState.DefaultTileCount;
            var from = GetTileGroundPosition(startTileIndex) + positionOffset;
            var to = GetTileGroundPosition(targetTileIndex) + positionOffset;
            ApplyBossDirection(startTileIndex, true);
            var directionStep = 0;
            var duration = stepDuration * Mathf.Max(1f, distance * .65f);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / duration);
                var nextDirectionStep = Mathf.Min(Mathf.Max(0, distance - 1),
                    Mathf.FloorToInt(progress * distance));
                if (nextDirectionStep != directionStep)
                {
                    directionStep = nextDirectionStep;
                    ApplyBossDirection(startTileIndex + directionStep, true);
                }
                var smooth = Mathf.SmoothStep(0f, 1f, progress);
                var position = Vector3.Lerp(from, to, smooth);
                CameraFollowPosition = Vector3.Lerp(
                    GetCameraTilePosition(startTileIndex),
                    GetCameraTilePosition(targetTileIndex),
                    smooth);
                position.y += .45f + Mathf.Sin(progress * Mathf.PI) * .25f;
                transform.position = position;
                shadowGroundPosition = Vector3.Lerp(from, to, smooth) - visualHeightOffset;
                yield return null;
            }

            var previousTileIndex = CurrentTileIndex;
            CurrentTileIndex = targetTileIndex;
            CameraFollowPosition = GetCameraTilePosition(targetTileIndex);
            TileChanged?.Invoke(previousTileIndex, targetTileIndex);
            ApplyBossDirection(targetTileIndex, false);
            transform.position = to;
            shadowGroundPosition = to - visualHeightOffset;
            isMoving = false;
            if (!reachedBase) boardView.PlayPress(targetTileIndex);
        }

        public IEnumerator PlayGoalArrival()
        {
            if (!isBoss || boardView == null)
                yield break;

            var standingPosition = GetStandingPosition(CurrentTileIndex);
            isMoving = true;
            ApplyBossDirection(CurrentTileIndex, false);
            var landingHeight = 0.45f;
            var landingDuration = Mathf.Max(0.05f, stepDuration * 0.45f);
            for (var elapsed = 0f; elapsed < landingDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(elapsed / landingDuration));
                transform.position = standingPosition +
                    Vector3.up * Mathf.Lerp(landingHeight, 0f, progress);
                shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
                yield return null;
            }
            transform.position = standingPosition;
            isMoving = false;

            boardView.PlayPress(CurrentTileIndex);
            for (var elapsed = 0f;
                 elapsed < boardView.PressPulseDuration;
                 elapsed += Time.deltaTime)
            {
                transform.position = GetStandingPosition(CurrentTileIndex);
                shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
                yield return null;
            }
            transform.position = GetStandingPosition(CurrentTileIndex);
            shadowGroundPosition = GetShadowPosition(CurrentTileIndex);
        }

        public IEnumerator PlayKnockback(int fromTileIndex, int toTileIndex)
        {
            if (fromTileIndex == toTileIndex) yield break;
            isMoving = true;
            ApplyDirectionForDeparture(fromTileIndex);
            var from = GetTileGroundPosition(fromTileIndex);
            var to = GetTileGroundPosition(toTileIndex);
            CurrentTileIndex = toTileIndex;
            TileChanged?.Invoke(fromTileIndex, toTileIndex);
            for (var elapsed = 0f; elapsed < stepDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / stepDuration));
                transform.position = Vector3.Lerp(from, to, progress) + positionOffset;
                shadowGroundPosition = transform.position - visualHeightOffset;
                yield return null;
            }
            transform.position = to + positionOffset;
            shadowGroundPosition = to + positionOffset - visualHeightOffset;
            isMoving = false;
        }

        private Vector3 GetStandingPosition(int tileIndex)
        {
            return GetTileGroundPosition(tileIndex) + positionOffset + transitionOffset;
        }

        private Vector3 GetShadowPosition(int tileIndex)
        {
            return GetTileGroundPosition(tileIndex) + positionOffset + transitionOffset - visualHeightOffset;
        }

        private Vector3 GetTileGroundPosition(int tileIndex)
        {
            return boardView.GetWorldPosition(tileIndex) + GetTileVisualOffset(tileIndex);
        }

        private Vector3 GetCameraTilePosition(int tileIndex) =>
            boardView.GetWorldPosition(tileIndex);

        private Vector3 GetTileVisualOffset(int tileIndex)
        {
            return boardView != null ? boardView.GetTileVisualWorldOffset(tileIndex) : Vector3.zero;
        }

        private void ApplyDirectionForDeparture(int tileIndex)
        {
            if (spriteRenderer == null || frontSprite == null) return;
            if (isBoss)
            {
                ApplyBossDirection(tileIndex, false);
                return;
            }
            var tileCount = GaeBullBing.Core.Board.BoardState.DefaultTileCount;
            var normalized = (tileIndex % tileCount + tileCount) % tileCount;
            var line = normalized < 9 ? 0 : normalized < 18 ? 1 : normalized < 27 ? 2 : 3;
            spriteRenderer.sprite = line <= 1 ? backSprite : frontSprite;
            spriteRenderer.flipX = line == 1 || line == 2;
        }

        private void ApplyBossDirection(int tileIndex, bool flying)
        {
            if (spriteRenderer == null) return;
            var tileCount = GaeBullBing.Core.Board.BoardState.DefaultTileCount;
            var normalized = (tileIndex % tileCount + tileCount) % tileCount;
            var line = normalized < 9 ? 0 : normalized < 18 ? 1 : normalized < 27 ? 2 : 3;
            var showBack = line <= 1;
            spriteRenderer.sprite = flying
                ? showBack ? flyingBackSprite : flyingFrontSprite
                : showBack ? backSprite : frontSprite;
            spriteRenderer.flipX = line == 1 || line == 2;
        }

        private void ApplyBossEntranceDirection()
        {
            const int thirdLineStartTile = 27;
            ApplyBossDirection(thirdLineStartTile, true);
        }

        private void SetSpawnDetailsVisible(bool visible)
        {
            if (healthBackgroundRenderer != null) healthBackgroundRenderer.enabled = visible;
            if (healthFillRenderer != null) healthFillRenderer.enabled = visible;
            statusIndicator?.SetVisible(visible);
            shadow?.SetVisible(visible);
            if (positionMarker != null) positionMarker.gameObject.SetActive(visible);
        }

public IEnumerator PlayHit()
        {
            hitFlashCount++;
            if (healthFillRenderer != null) healthFillRenderer.color = new Color(1f, .12f, .1f);
            if (healthBackgroundRenderer != null) healthBackgroundRenderer.color = new Color(.45f, .02f, .02f, .95f);
            yield return new WaitForSeconds(0.12f);
            hitFlashCount = Mathf.Max(0, hitFlashCount - 1);
            if (hitFlashCount > 0) yield break;
            if (healthFillRenderer != null) healthFillRenderer.color = new Color(.2f, .9f, .25f);
            if (healthBackgroundRenderer != null) healthBackgroundRenderer.color = new Color(.12f, .12f, .12f, .9f);
            if (spriteRenderer != null) spriteRenderer.color = monsterBaseColor;
        }
    }
}
