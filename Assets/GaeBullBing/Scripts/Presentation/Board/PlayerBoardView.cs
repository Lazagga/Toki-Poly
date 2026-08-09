using System.Collections;
using System;
using GaeBullBing.Presentation.Audio;
using UnityEngine;

namespace GaeBullBing.Presentation.Board
{
    public sealed class PlayerBoardView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float stepDuration = 0.14f;
        [SerializeField, Min(0f)] private float hopHeight = 0.18f;
        [SerializeField] private Vector3 positionOffset = Vector3.zero;
        [SerializeField] private Sprite frontSprite;
        [SerializeField] private Sprite backSprite;

        private BoardTilemapView boardView;
        private SpriteRenderer visualRenderer;
        private BoardCharacterShadow shadow;
        private HoverMarkerView positionMarker;
        private int currentTileIndex;
        private Coroutine layoutRoutine;
        private bool isMoving;
        private Vector3 transitionOffset;
        private Vector3 shadowGroundPosition;

        public Vector3 CameraFollowPosition { get; private set; }
        public int CurrentTileIndex => currentTileIndex;
        public event Action<int> TileEntered;
        public event Action<int> TileMoveStarted;

        public void Initialize(BoardTilemapView view, int tileIndex)
        {
            boardView = view;
            ConfigureVisual();
            SnapTo(tileIndex);
        }

        private void ConfigureVisual()
        {
            var source = GetComponent<SpriteRenderer>();
            var visual = transform.Find("Visual");
            if (visual == null && source != null)
            {
                visual = new GameObject("Visual").transform;
                visual.SetParent(transform, false);
                visual.localScale = Vector3.one;
                var renderer = visual.gameObject.AddComponent<SpriteRenderer>();
                if (frontSprite == null) frontSprite = source.sprite;
                renderer.sprite = frontSprite; renderer.color = source.color; renderer.sharedMaterial = source.sharedMaterial;
                renderer.sortingLayerID = source.sortingLayerID; renderer.sortingOrder = source.sortingOrder;
                renderer.flipX = source.flipX; renderer.flipY = source.flipY; source.enabled = false;
            }
            visualRenderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
            if (shadow == null)
                shadow = BoardCharacterShadow.Create(transform,
                    visualRenderer != null ? visualRenderer.sortingLayerID : 0);
            if (positionMarker == null)
            {
                var markerY = visualRenderer != null && visualRenderer.sprite != null
                    ? visualRenderer.sprite.bounds.max.y *
                      visualRenderer.transform.localScale.y + .26f
                    : .75f;
                positionMarker = HoverMarkerView.Create(
                    transform,
                    new Vector3(0f, markerY, 0f),
                    new Color(1f, .85f, .12f, 1f),
                    visualRenderer != null ? visualRenderer.sortingLayerID : 0);
            }
        }

        private void LateUpdate()
        {
            if (visualRenderer == null)
                visualRenderer = transform.Find("Visual")?.GetComponent<SpriteRenderer>();
            if (!isMoving && boardView != null)
            {
                transform.position = boardView.GetPlayerStandWorldPosition(currentTileIndex) + positionOffset + transitionOffset;
                CameraFollowPosition = GetCameraTilePosition(currentTileIndex);
                shadowGroundPosition = GetShadowGroundPosition(currentTileIndex);
            }
            if (visualRenderer != null)
                visualRenderer.sortingOrder = BoardDepthSorting.GetOrder(shadowGroundPosition, 20);
            shadow?.Set(shadowGroundPosition);
        }

        public void SnapTo(int tileIndex)
        {
            EnsureBoardView();
            currentTileIndex = tileIndex;
            boardView.ResetPress(tileIndex);
            transform.position = boardView.GetPlayerStandWorldPosition(tileIndex) + positionOffset + transitionOffset;
            CameraFollowPosition = GetCameraTilePosition(tileIndex);
            shadowGroundPosition = GetShadowGroundPosition(tileIndex);
        }

        public void SetLayoutOffset(Vector3 offset)
        {
            var targetOffset = offset;
            if (layoutRoutine != null) StopCoroutine(layoutRoutine);
            layoutRoutine = StartCoroutine(AnimateLayoutOffset(targetOffset));
        }

        public void SetTransitionOffset(Vector3 offset)
        {
            transitionOffset = offset;
            if (isMoving || boardView == null) return;
            transform.position = boardView.GetPlayerStandWorldPosition(currentTileIndex) + positionOffset + transitionOffset;
            CameraFollowPosition = GetCameraTilePosition(currentTileIndex);
            shadowGroundPosition = GetShadowGroundPosition(currentTileIndex);
        }

        private IEnumerator AnimateLayoutOffset(Vector3 targetOffset)
        {
            var startOffset = positionOffset;
            var elapsed = 0f;
            while (elapsed < .18f)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .18f));
                positionOffset = Vector3.Lerp(startOffset, targetOffset, progress);
                if (!isMoving)
                {
                    transform.position = boardView.GetPlayerStandWorldPosition(currentTileIndex) + positionOffset + transitionOffset;
                    CameraFollowPosition = GetCameraTilePosition(currentTileIndex);
                    shadowGroundPosition = GetShadowGroundPosition(currentTileIndex);
                }
                yield return null;
            }
            positionOffset = targetOffset;
            if (!isMoving)
            {
                transform.position = boardView.GetPlayerStandWorldPosition(currentTileIndex) + positionOffset + transitionOffset;
                CameraFollowPosition = GetCameraTilePosition(currentTileIndex);
                shadowGroundPosition = GetShadowGroundPosition(currentTileIndex);
            }
            layoutRoutine = null;
        }

        public IEnumerator MoveSteps(
            int startTileIndex,
            int distance,
            Action<int> stepStarted = null,
            Func<int, IEnumerator> stepCompleted = null)
        {
            EnsureBoardView();
            isMoving = true;

            for (var step = 1; step <= distance; step++)
            {
                var fromIndex = (startTileIndex + step - 1) % GaeBullBing.Core.Board.BoardState.DefaultTileCount;
                var toIndex = (startTileIndex + step) % GaeBullBing.Core.Board.BoardState.DefaultTileCount;
                var from = boardView.GetPlayerStandWorldPosition(fromIndex);
                boardView.ReleasePlayerTile(fromIndex);
                ApplyDirectionForDeparture(fromIndex);
                var audio = AudioManager.Instance;
                audio?.PlayAt(audio.Player.Step, transform.position);
                TileMoveStarted?.Invoke(toIndex);
                stepStarted?.Invoke(toIndex);
                var elapsed = 0f;
                var triggeredPress = false;

                while (elapsed < stepDuration)
                {
                    elapsed += Time.deltaTime;
                    var progress = Mathf.Clamp01(elapsed / stepDuration);
                    if (!triggeredPress && progress >= .45f)
                    {
                        boardView.PlayPress(toIndex);
                        triggeredPress = true;
                    }
                    var to = boardView.GetPlayerStandWorldPosition(toIndex);
                    var easedProgress = Mathf.SmoothStep(0f, 1f, progress);
                    var position = Vector3.Lerp(from, to, easedProgress) + positionOffset + transitionOffset;
                    // 점프 높이는 적용하기 전 좌표를 그림자 지면 좌표로 사용한다.
                    shadowGroundPosition = position;
                    // 캐릭터는 혼잡도에 따른 시각적 슬롯으로 이동하지만,
                    // 카메라는 타일 클릭과 동일한 논리적 타일 중심 경로를 따라간다.
                    CameraFollowPosition = Vector3.Lerp(
                        boardView.GetWorldPosition(fromIndex),
                        boardView.GetWorldPosition(toIndex),
                        easedProgress);
                    position.y += Mathf.Sin(progress * Mathf.PI) * hopHeight;
                    transform.position = position;
                    yield return null;
                }

                currentTileIndex = toIndex;
                if (!triggeredPress)
                    boardView.PlayPress(toIndex);
                transform.position = boardView.GetPlayerStandWorldPosition(toIndex) + positionOffset + transitionOffset;
                CameraFollowPosition = GetCameraTilePosition(toIndex);
                shadowGroundPosition = GetShadowGroundPosition(toIndex);
                audio?.PlayAt(audio.Player.Land, transform.position);
                TileEntered?.Invoke(toIndex);
                if (stepCompleted != null)
                {
                    var completionRoutine = stepCompleted(toIndex);
                    if (completionRoutine != null)
                        yield return completionRoutine;
                }
            }

            isMoving = false;
            transform.position = boardView.GetPlayerStandWorldPosition(currentTileIndex) + positionOffset + transitionOffset;
            CameraFollowPosition = GetCameraTilePosition(currentTileIndex);
            shadowGroundPosition = GetShadowGroundPosition(currentTileIndex);
        }

        private Vector3 GetCameraTilePosition(int tileIndex) =>
            boardView.GetWorldPosition(tileIndex);

        private Vector3 GetShadowGroundPosition(int tileIndex) =>
            boardView.GetPlayerStandWorldPosition(tileIndex) + positionOffset + transitionOffset;

        private void EnsureBoardView()
        {
            if (boardView == null)
                throw new MissingReferenceException("PlayerBoardView has not been initialized with a BoardTilemapView.");
        }

        private void ApplyDirectionForDeparture(int tileIndex)
        {
            if (visualRenderer == null) return;
            var normalized = (tileIndex % GaeBullBing.Core.Board.BoardState.DefaultTileCount +
                              GaeBullBing.Core.Board.BoardState.DefaultTileCount) %
                             GaeBullBing.Core.Board.BoardState.DefaultTileCount;
            var line = normalized < 9 ? 0 : normalized < 18 ? 1 : normalized < 27 ? 2 : 3;
            visualRenderer.sprite = line <= 1 ? backSprite : frontSprite;
            visualRenderer.flipX = line == 1 || line == 2;
        }
    }
}
