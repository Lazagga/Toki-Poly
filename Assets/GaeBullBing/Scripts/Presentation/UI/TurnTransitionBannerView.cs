using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    [ExecuteAlways]
    public sealed class TurnTransitionBannerView : MonoBehaviour
    {
        [SerializeField] private RectTransform bannerRoot;
        [SerializeField] private Image turnImage;
        [Header("Layout")]
        [SerializeField] private RectTransform boxRect;
        [SerializeField] private RectTransform turnImageRect;
        [SerializeField] private RectTransform ropeLeftRect;
        [SerializeField] private RectTransform ropeRightRect;
        [SerializeField] private Vector2 boxSize = new Vector2(380f, 280f);
        [SerializeField] private Vector2 turnImageSize = new Vector2(270f, 180f);
        [SerializeField, Min(0f)] private float ropeSpacing = 220f;
        [SerializeField, Min(0f)] private float ropeWidth = 5f;
        [SerializeField, Min(0f)] private float ropeLength = 400f;

        [Header("Sprites")]
        [SerializeField] private Sprite playerTurnSprite;
        [SerializeField] private Sprite enemyTurnSprite;
        [Header("Animation")]
        [SerializeField, Min(1f)] private float hiddenDistance = 340f;
        [SerializeField, Min(.01f)] private float descendDuration = .48f;
        [SerializeField, Min(0f)] private float holdDuration = .55f;
        [SerializeField, Min(.01f)] private float ascendDuration = .38f;
        [SerializeField, Min(0f)] private float swingAngle = 3.5f;
        [SerializeField, Min(0f)] private float swingCycles = 1.5f;

        private Vector2 visiblePosition;
        private bool layoutDirty = true;

        private void Awake()
        {
            ApplyLayout();

            if (bannerRoot == null)
                return;

            visiblePosition = bannerRoot.anchoredPosition;
            bannerRoot.gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            layoutDirty = true;
        }

        private void Update()
        {
            if (Application.isPlaying || !layoutDirty)
                return;

            layoutDirty = false;
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (boxRect != null)
                boxRect.sizeDelta = boxSize;

            if (turnImageRect != null)
                turnImageRect.sizeDelta = turnImageSize;

            ApplyRopeLayout(ropeLeftRect, -ropeSpacing * .5f);
            ApplyRopeLayout(ropeRightRect, ropeSpacing * .5f);
        }

        private void ApplyRopeLayout(RectTransform ropeRect, float positionX)
        {
            if (ropeRect == null)
                return;

            ropeRect.sizeDelta = new Vector2(ropeWidth, ropeLength);
            var position = ropeRect.anchoredPosition;
            position.x = positionX;
            ropeRect.anchoredPosition = position;
        }

        public IEnumerator PlayPlayerTurn() => Play(playerTurnSprite);

        public IEnumerator PlayEnemyTurn() => Play(enemyTurnSprite);

        private IEnumerator Play(Sprite sprite)
        {
            if (bannerRoot == null || turnImage == null || sprite == null)
                yield break;

            turnImage.sprite = sprite;
            turnImage.preserveAspect = true;
            bannerRoot.gameObject.SetActive(true);

            var hiddenPosition = visiblePosition + Vector2.up * hiddenDistance;
            bannerRoot.anchoredPosition = hiddenPosition;
            bannerRoot.localRotation = Quaternion.identity;

            for (var elapsed = 0f; elapsed < descendDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / descendDuration);
                var eased = EaseOutBack(progress);
                bannerRoot.anchoredPosition = Vector2.LerpUnclamped(
                    hiddenPosition, visiblePosition, eased);
                ApplySwing(progress, 1f - progress * .45f);
                yield return null;
            }

            bannerRoot.anchoredPosition = visiblePosition;
            for (var elapsed = 0f; elapsed < holdDuration; elapsed += Time.deltaTime)
            {
                var progress = holdDuration > 0f
                    ? Mathf.Clamp01(elapsed / holdDuration)
                    : 1f;
                ApplySwing(progress + 1f, 1f - progress);
                yield return null;
            }

            bannerRoot.localRotation = Quaternion.identity;
            for (var elapsed = 0f; elapsed < ascendDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / ascendDuration));
                bannerRoot.anchoredPosition = Vector2.Lerp(
                    visiblePosition, hiddenPosition, progress);
                yield return null;
            }

            bannerRoot.anchoredPosition = visiblePosition;
            bannerRoot.localRotation = Quaternion.identity;
            bannerRoot.gameObject.SetActive(false);
        }

        private void ApplySwing(float progress, float strength)
        {
            var angle = Mathf.Sin(progress * Mathf.PI * 2f * swingCycles) *
                        swingAngle * Mathf.Clamp01(strength);
            bannerRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static float EaseOutBack(float progress)
        {
            const float overshoot = 1.70158f;
            var shifted = progress - 1f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted +
                   overshoot * shifted * shifted;
        }
    }
}
