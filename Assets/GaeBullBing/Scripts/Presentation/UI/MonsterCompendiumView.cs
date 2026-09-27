using System;
using System.Collections.Generic;
using System.Collections;
using GaeBullBing.Core;
using GaeBullBing.Core.Data;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Presentation.Monsters;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class MonsterCompendiumView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image[] pageImages = new Image[2];
        [SerializeField] private Text[] pageTexts = new Text[2];
        [SerializeField] private Button[] pageButtons = new Button[2];
        [SerializeField] private RectTransform[] pageRoots = new RectTransform[2];
        [SerializeField] private RectTransform turningPageRoot;
        [SerializeField] private Image turningPageBackground;
        [SerializeField] private Image turningPageImage;
        [SerializeField] private Text turningPageText;
        [SerializeField] private RectTransform pageFoldRoot;
        [SerializeField] private Image pageFoldShadow;
        [SerializeField] private Image pageFoldHighlight;
        [SerializeField] private RectTransform[] regionBookmarks = Array.Empty<RectTransform>();
        [SerializeField] private Button[] regionButtons = Array.Empty<Button>();
        [SerializeField] private Button closeButton;
        [SerializeField] private bool revealAllForTesting = true;
        [SerializeField, Min(.05f)] private float pageFlipDuration = .46f;

        private MonsterDatabaseDefinition database;
        private MonsterPresenter monsterPresenter;
        private readonly List<MonsterDefinition> monsters = new();
        private int spreadIndex;
        private bool isFlipping;

        public void Bind(Action closed)
        {
            database = Resources.Load<MonsterDatabaseDefinition>("GaeBullBing/MonsterDatabase");
            monsterPresenter = FindFirstObjectByType<MonsterPresenter>(FindObjectsInactive.Include);
            monsters.Clear();
            if (database?.Monsters != null)
                foreach (var monster in database.Monsters) if (monster != null) monsters.Add(monster);

            BindButton(closeButton, () => { Hide(); closed?.Invoke(); });
            if (pageButtons.Length >= 2)
            {
                BindButton(pageButtons[0], () => RequestSpread(spreadIndex - 1));
                BindButton(pageButtons[1], () => RequestSpread(spreadIndex + 1));
            }
            for (var i = 0; i < regionButtons.Length; i++)
            {
                var regionNumber = i + 1;
                BindButton(regionButtons[i], () => JumpToRegion(regionNumber));
            }
            spreadIndex = 0;
            RefreshPages();
            Hide();
        }

        public void Show()
        {
            root.SetActive(true);
            RefreshPages();
        }

        public void Hide() => root.SetActive(false);

        private void RequestSpread(int target)
        {
            target = Mathf.Clamp(target, 0, Mathf.Max(0, SpreadCount - 1));
            if (target == spreadIndex || isFlipping) return;
            StartCoroutine(FlipToSpread(target));
        }

        private void JumpToRegion(int regionNumber)
        {
            var regionId = $"REGION_{regionNumber:00}";
            var monsterIndex = monsters.FindIndex(monster => monster.RegionId == regionId);
            if (monsterIndex < 0) return;
            var target = monsterIndex / 2;
            if (target != spreadIndex && !isFlipping) StartCoroutine(FlipToSpread(target));
        }

        private IEnumerator FlipToSpread(int target)
        {
            isFlipping = true;
            while (spreadIndex != target)
            {
                var direction = target > spreadIndex ? 1 : -1;
                var fromSpread = spreadIndex;
                var toSpread = spreadIndex + direction;
                var sourcePage = direction > 0 ? 1 : 0;
                var destinationPage = direction > 0 ? 0 : 1;
                var crossingBookmark = FindCrossingBookmark(fromSpread, toSpread, direction);

                ConfigureTurningPage(sourcePage, fromSpread * 2 + sourcePage);
                BindPage(sourcePage, monsters[toSpread * 2 + sourcePage]);
                turningPageRoot.gameObject.SetActive(true);
                if (pageFoldRoot != null) pageFoldRoot.gameObject.SetActive(false);
                yield return AnimateTurningPage(1f, .015f, direction, true, crossingBookmark);

                ConfigureTurningPage(destinationPage, toSpread * 2 + destinationPage);
                yield return AnimateTurningPage(.015f, 1f, direction, false, crossingBookmark);
                BindPage(destinationPage, monsters[toSpread * 2 + destinationPage]);
                turningPageRoot.gameObject.SetActive(false);
                if (pageFoldRoot != null) pageFoldRoot.gameObject.SetActive(false);
                turningPageRoot.anchoredPosition = Vector2.zero;
                turningPageRoot.localScale = Vector3.one;
                turningPageRoot.localRotation = Quaternion.identity;
                spreadIndex = toSpread;
                RefreshBookmarkSides();
                if (Mathf.Abs(target - spreadIndex) > 0)
                    yield return new WaitForSecondsRealtime(.025f);
            }
            isFlipping = false;
            RefreshPages();
        }

        private void ConfigureTurningPage(int page, int monsterIndex)
        {
            var source = pageRoots[page];
            turningPageRoot.anchorMin = source.anchorMin;
            turningPageRoot.anchorMax = source.anchorMax;
            turningPageRoot.offsetMin = source.offsetMin;
            turningPageRoot.offsetMax = source.offsetMax;
            turningPageRoot.pivot = page == 0 ? new Vector2(1f, .5f) : new Vector2(0f, .5f);
            turningPageRoot.localScale = Vector3.one;
            BindVisual(turningPageImage, turningPageText, monsters[monsterIndex]);
        }

        private IEnumerator AnimateTurningPage(float from, float to, int direction, bool firstHalf,
            RectTransform crossingBookmark)
        {
            var duration = pageFlipDuration * .5f;
            var basePagePosition = turningPageRoot.anchoredPosition;
            var startRotation = firstHalf ? 0f : direction * 3.5f;
            var endRotation = firstHalf ? direction * 3.5f : 0f;
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                var linear = Mathf.Clamp01(elapsed / duration);
                var global = firstHalf ? linear * .5f : .5f + linear * .5f;
                var easedGlobal = .5f - .5f * Mathf.Cos(global * Mathf.PI);
                var t = firstHalf ? easedGlobal * 2f : (easedGlobal - .5f) * 2f;
                var fold = Mathf.Sin(global * Mathf.PI);
                var scaleX = Mathf.Lerp(from, to, t);
                var scaleY = 1f - fold * .055f;
                var verticalWave = Mathf.Sin(global * Mathf.PI * 2f);
                var bow = new Vector2(direction * fold * 24f, verticalWave * 9f);

                turningPageRoot.localScale = new Vector3(scaleX, scaleY, 1f);
                turningPageRoot.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(startRotation, endRotation, t) + direction * verticalWave * 1.8f);
                turningPageRoot.anchoredPosition = basePagePosition + bow;
                AnimateFoldEdge(global, direction, fold, verticalWave);
                if (crossingBookmark != null)
                    AnimateBookmark(crossingBookmark, direction, firstHalf, t, scaleY, turningPageRoot.localRotation);
                var shade = firstHalf ? Mathf.Lerp(1f, .62f, t) : Mathf.Lerp(.62f, 1f, t);
                turningPageBackground.color = new Color(.98f * shade, .91f * shade, .73f * shade, 1f);
                yield return null;
            }
            turningPageRoot.localScale = new Vector3(to, 1f, 1f);
            turningPageRoot.localRotation = Quaternion.Euler(0f, 0f, endRotation);
            turningPageRoot.anchoredPosition = basePagePosition;
            if (crossingBookmark != null)
                AnimateBookmark(crossingBookmark, direction, firstHalf, 1f, 1f, turningPageRoot.localRotation);
        }

        private void AnimateFoldEdge(float progress, int direction, float curl, float verticalWave)
        {
            if (pageFoldRoot == null) return;

            var startX = direction > 0 ? .968f : .032f;
            var endX = direction > 0 ? .032f : .968f;
            var x = Mathf.Lerp(startX, endX, .5f - .5f * Mathf.Cos(progress * Mathf.PI));
            pageFoldRoot.anchorMin = new Vector2(x, .07f);
            pageFoldRoot.anchorMax = new Vector2(x, .93f);
            pageFoldRoot.sizeDelta = new Vector2(Mathf.Lerp(12f, 52f, curl), Mathf.Lerp(-72f, -18f, curl));
            pageFoldRoot.anchoredPosition = new Vector2(0f, verticalWave * 8f);
            pageFoldRoot.localRotation = Quaternion.Euler(0f, 0f, direction * verticalWave * 4f);

            if (pageFoldShadow != null)
                pageFoldShadow.color = new Color(.16f, .08f, .025f, Mathf.Lerp(.08f, .48f, curl));
            if (pageFoldHighlight != null)
                pageFoldHighlight.color = new Color(1f, .94f, .76f, Mathf.Lerp(.18f, .82f, curl));
        }

        private RectTransform FindCrossingBookmark(int fromSpread, int toSpread, int direction)
        {
            for (var i = 0; i < regionBookmarks.Length; i++)
            {
                var bookmarkSpread = FindRegionSpread(i + 1);
                if ((direction > 0 && bookmarkSpread == toSpread) ||
                    (direction < 0 && bookmarkSpread == fromSpread))
                    return regionBookmarks[i];
            }
            return null;
        }

        private int FindRegionSpread(int regionNumber)
        {
            var regionId = $"REGION_{regionNumber:00}";
            var monsterIndex = monsters.FindIndex(monster => monster.RegionId == regionId);
            return monsterIndex < 0 ? int.MaxValue : monsterIndex / 2;
        }

        private static void AnimateBookmark(RectTransform bookmark, int direction, bool firstHalf,
            float t, float scaleY, Quaternion rotation)
        {
            var startsOnRight = direction > 0;
            var fromX = startsOnRight ? .965f : .035f;
            var toX = startsOnRight ? .035f : .965f;
            var x = firstHalf ? Mathf.Lerp(fromX, .5f, t) : Mathf.Lerp(.5f, toX, t);
            var edgeScale = firstHalf ? Mathf.Lerp(1f, .08f, t) : Mathf.Lerp(.08f, 1f, t);
            var anchor = bookmark.anchorMin;
            bookmark.anchorMin = bookmark.anchorMax = new Vector2(x, anchor.y);
            bookmark.pivot = x < .5f ? new Vector2(1f, .5f) : new Vector2(0f, .5f);
            bookmark.localScale = new Vector3(edgeScale, scaleY, 1f);
            bookmark.localRotation = rotation;
        }

        private void RefreshBookmarkSides()
        {
            for (var i = 0; i < regionBookmarks.Length; i++)
            {
                var bookmark = regionBookmarks[i];
                if (bookmark == null) continue;
                var onLeft = FindRegionSpread(i + 1) <= spreadIndex;
                var anchor = bookmark.anchorMin;
                var x = onLeft ? .035f : .965f;
                bookmark.anchorMin = bookmark.anchorMax = new Vector2(x, anchor.y);
                bookmark.pivot = onLeft ? new Vector2(1f, .5f) : new Vector2(0f, .5f);
                bookmark.localScale = Vector3.one;
                bookmark.localRotation = Quaternion.identity;
            }
        }

        private int SpreadCount => Mathf.CeilToInt(monsters.Count / 2f);

        private void RefreshPages()
        {
            for (var page = 0; page < 2; page++)
            {
                var monsterIndex = spreadIndex * 2 + page;
                if (monsterIndex >= 0 && monsterIndex < monsters.Count)
                    BindPage(page, monsters[monsterIndex]);
                else
                    ClearPage(page);
            }
            if (pageButtons.Length >= 2)
            {
                pageButtons[0].interactable = true;
                pageButtons[1].interactable = true;
            }
            RefreshBookmarkSides();
        }

        private void BindPage(int page, MonsterDefinition monster)
        {
            BindVisual(pageImages[page], pageTexts[page], monster);
        }

        private void BindVisual(Image image, Text text, MonsterDefinition monster)
        {
            var captured = revealAllForTesting || MonsterCollectionProgress.IsCaptured(monster.Id);
            image.sprite = monsterPresenter != null ? monsterPresenter.GetFrontSprite(monster.Id) : null;
            image.color = captured ? Color.white : Color.black;
            image.preserveAspect = true;
            image.enabled = image.sprite != null;
            text.text = captured ? BuildDescription(monster) : "???";
        }

        private void ClearPage(int page)
        {
            pageImages[page].sprite = null;
            pageImages[page].enabled = false;
            pageTexts[page].text = string.Empty;
        }

        private static string BuildDescription(MonsterDefinition monster)
        {
            var passives = monster.PassiveEffects == null || monster.PassiveEffects.Length == 0
                ? "없음"
                : string.Join(", ", Array.ConvertAll(monster.PassiveEffects, effect => effect.Id));
            var extra = monster.Tier == MonsterTier.Boss
                ? $"\n\n[보스 패턴]\n{passives}\n\n[클리어 아이템]\nITEM_{monster.Id}"
                : $"\n\n[패시브]\n{passives}";
            return $"{monster.DisplayName}\n{monster.RegionId}\n\n" +
                   $"체력 {monster.MaxHp}\n이동 {monster.MoveDistance}\n방어 {monster.BaseDefense:0}\n\n" +
                   $"{(string.IsNullOrWhiteSpace(monster.Story) ? "스토리 준비 중" : monster.Story)}{extra}";
        }

        private static void BindButton(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }
    }
}
