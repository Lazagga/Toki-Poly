using System.Collections;
using GaeBullBing.Core.Board;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.Monsters;
using UnityEngine;

namespace GaeBullBing.Presentation.Game
{
    public sealed class GameBoardTransition : MonoBehaviour
    {
        [SerializeField] private BoardTilemapView boardView;
        [SerializeField] private PlayerBoardView playerView;
        [SerializeField] private MonsterPresenter monsterPresenter;
        [Tooltip("보드를 화면 위로 숨길 때 사용하는 최소 이동 거리입니다. 실제 거리는 현재 카메라 범위에 맞춰 자동으로 늘어납니다.")]
        [SerializeField, Min(0.5f)] private float verticalDistance = 6f;
        [Tooltip("시작 화면에서 타일의 가장자리나 그림자가 보이지 않도록 화면 위에 추가하는 여유 거리입니다.")]
        [SerializeField, Min(0f)] private float hiddenMargin = 1f;
        [SerializeField, Min(0.05f)] private float tileDuration = .28f;
        [SerializeField, Min(0f)] private float tileStagger = .035f;
        [SerializeField, Min(0.05f)] private float actorDuration = .45f;

        public void PrepareHidden()
        {
            var hiddenDistance = GetHiddenVerticalDistance();
            boardView.SetAllTransitionOffsets(hiddenDistance);
            SetActorOffset(hiddenDistance);
        }

        public IEnumerator PlayIntro()
        {
            var hiddenDistance = GetHiddenVerticalDistance();
            boardView.SetAllTransitionOffsets(hiddenDistance);
            SetActorOffset(hiddenDistance);
            yield return AnimateTiles(true, hiddenDistance);
            yield return AnimateActors(hiddenDistance, 0f);
            boardView.PlayPress(playerView.CurrentTileIndex);
            yield return new WaitForSeconds(.14f);
            yield return boardView.PlayBonusTileOutlineFadeIn();
        }

        public IEnumerator PlayOutro()
        {
            var hiddenDistance = GetHiddenVerticalDistance();
            yield return AnimateActors(0f, hiddenDistance);
            yield return AnimateTiles(false, hiddenDistance);
        }

        private IEnumerator AnimateTiles(bool entering, float hiddenDistance)
        {
            var count = BoardState.DefaultTileCount;
            var totalDuration = tileDuration + tileStagger * (count - 1);
            for (var elapsed = 0f; elapsed < totalDuration; elapsed += Time.deltaTime)
            {
                for (var index = 0; index < count; index++)
                {
                    var sequenceIndex = entering ? index : count - 1 - index;
                    var progress = Mathf.Clamp01((elapsed - sequenceIndex * tileStagger) / tileDuration);
                    var eased = Mathf.SmoothStep(0f, 1f, progress);
                    boardView.SetTransitionOffset(index, entering
                        ? Mathf.Lerp(hiddenDistance, 0f, eased)
                        : Mathf.Lerp(0f, hiddenDistance, eased));
                }
                yield return null;
            }
            boardView.SetAllTransitionOffsets(entering ? 0f : hiddenDistance);
        }

        private float GetHiddenVerticalDistance()
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null || boardView == null)
                return verticalDistance;

            var lowestTileY = float.MaxValue;
            for (var index = 0; index < BoardState.DefaultTileCount; index++)
                lowestTileY = Mathf.Min(lowestTileY, boardView.GetWorldPosition(index).y);

            // 가장 아래쪽 타일까지 카메라 상단보다 완전히 위로 올려야 보드 전체가 숨겨집니다.
            var cameraTopY = camera.transform.position.y + camera.orthographicSize;
            var requiredDistance = cameraTopY - lowestTileY + hiddenMargin;
            return Mathf.Max(verticalDistance, requiredDistance);
        }

        private IEnumerator AnimateActors(float from, float to)
        {
            for (var elapsed = 0f; elapsed < actorDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / actorDuration));
                SetActorOffset(Mathf.Lerp(from, to, progress));
                yield return null;
            }
            SetActorOffset(to);
        }

        private void SetActorOffset(float y)
        {
            var offset = Vector3.up * y;
            playerView.SetTransitionOffset(offset);
            monsterPresenter.SetTransitionOffset(offset);
        }
    }
}
