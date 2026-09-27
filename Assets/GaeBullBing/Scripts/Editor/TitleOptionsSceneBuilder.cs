using GaeBullBing.Presentation.Audio;
using GaeBullBing.Presentation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Editor
{
    public static class TitleOptionsSceneBuilder
    {
        [MenuItem("GaeBullBing/UI/Rebuild Monster Compendium")]
        public static void BuildCompendiumOnly()
        {
            var flow = Object.FindFirstObjectByType<GameFlowView>(FindObjectsInactive.Include);
            if (flow == null) throw new MissingReferenceException("GameFlowView not found.");
            var flowData = new SerializedObject(flow);
            var titleRoot = flowData.FindProperty("titleRoot").objectReferenceValue as GameObject;
            var startButton = flowData.FindProperty("startButton").objectReferenceValue as Button;
            if (titleRoot == null || startButton == null)
                throw new MissingReferenceException("Title root or start button not assigned.");
            var compendium = BuildCompendium(titleRoot.transform, startButton);
            flowData.Update();
            Assign(flowData, "monsterCompendiumView", compendium);
            flowData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(flow);
            EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
            EditorSceneManager.SaveScene(flow.gameObject.scene);
        }

        [MenuItem("GaeBullBing/UI/Build Title Options")]
        public static void Build()
        {
            var flow = Object.FindFirstObjectByType<GameFlowView>(FindObjectsInactive.Include);
            var pauseSettings = Object.FindFirstObjectByType<AudioSettingsView>(FindObjectsInactive.Include);
            if (flow == null || pauseSettings == null)
                throw new MissingReferenceException("GameFlowView or AudioSettingsView not found.");

            var flowData = new SerializedObject(flow);
            var titleRoot = flowData.FindProperty("titleRoot").objectReferenceValue as GameObject;
            var startButton = flowData.FindProperty("startButton").objectReferenceValue as Button;
            if (titleRoot == null || startButton == null)
                throw new MissingReferenceException("Title root or start button not assigned.");

            var oldMain = titleRoot.transform.Find("Title Main");
            var main = oldMain != null
                ? oldMain.gameObject
                : new GameObject("Title Main", typeof(RectTransform));
            main.transform.SetParent(titleRoot.transform, false);
            Stretch((RectTransform)main.transform);

            if (oldMain == null)
            {
                var children = new Transform[titleRoot.transform.childCount - 1];
                var write = 0;
                for (var index = 0; index < titleRoot.transform.childCount; index++)
                {
                    var child = titleRoot.transform.GetChild(index);
                    if (child != main.transform) children[write++] = child;
                }
                foreach (var child in children) child.SetParent(main.transform, true);
            }

            var settingsButton = FindOrCloneButton(main.transform, startButton,
                "Settings Button", "게임 설정", new Vector2(0f, -265f));
            var quitButton = FindOrCloneButton(main.transform, startButton,
                "Quit Button", "게임 종료", new Vector2(0f, -380f));
            var compendiumButton = FindOrCloneButton(main.transform, startButton,
                "Compendium Button", "몬스터 도감", new Vector2(0f, -150f));

            var oldSettings = titleRoot.transform.Find("Title Audio Settings");
            var settingsObject = Object.Instantiate(pauseSettings.gameObject, titleRoot.transform);
            if (oldSettings != null && oldSettings.gameObject != settingsObject)
                Object.DestroyImmediate(oldSettings.gameObject);
            settingsObject.name = "Title Audio Settings";
            var settingsRect = (RectTransform)settingsObject.transform;
            Stretch(settingsRect);
            var titleSettings = settingsObject.GetComponent<AudioSettingsView>();
            settingsObject.SetActive(false);
            var compendium = BuildCompendium(titleRoot.transform, startButton);

            flowData.Update();
            Assign(flowData, "titleMainRoot", main);
            Assign(flowData, "titleSettingsButton", settingsButton);
            Assign(flowData, "compendiumButton", compendiumButton);
            Assign(flowData, "monsterCompendiumView", compendium);
            Assign(flowData, "titleQuitButton", quitButton);
            Assign(flowData, "titleAudioSettingsView", titleSettings);
            flowData.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(flow);
            EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
            EditorSceneManager.SaveScene(flow.gameObject.scene);
        }

        private static MonsterCompendiumView BuildCompendium(Transform parent, Button template)
        {
            var existing = parent.Find("Monster Compendium");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var root = new GameObject("Monster Compendium", typeof(RectTransform), typeof(Image), typeof(MonsterCompendiumView));
            root.transform.SetParent(parent, false);
            Stretch((RectTransform)root.transform);
            root.GetComponent<Image>().color = new Color(.025f, .035f, .025f, .72f);

            var book = new GameObject("Book", typeof(RectTransform), typeof(Image));
            book.transform.SetParent(root.transform, false);
            var bookRect = (RectTransform)book.transform;
            bookRect.anchorMin = new Vector2(.14f, .1f); bookRect.anchorMax = new Vector2(.86f, .88f);
            bookRect.offsetMin = bookRect.offsetMax = Vector2.zero;
            var bookImage = book.GetComponent<Image>();
            var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            bookImage.sprite = uiSprite;
            bookImage.type = Image.Type.Sliced;
            bookImage.color = new Color(.25f, .12f, .045f, 1f);

            var pageStack = CreateDecorativeImage(book.transform, "Page Stack", uiSprite,
                new Vector2(.018f, .022f), new Vector2(.982f, .978f), new Color(.64f, .49f, .27f, 1f));
            pageStack.transform.SetAsFirstSibling();

            var leftPage = CreatePage(book.transform, "Left Page", new Vector2(.032f, .045f), new Vector2(.495f, .955f), uiSprite);
            var rightPage = CreatePage(book.transform, "Right Page", new Vector2(.505f, .045f), new Vector2(.968f, .955f), uiSprite);
            var seam = new GameObject("Book Seam", typeof(RectTransform), typeof(Image));
            seam.transform.SetParent(book.transform, false);
            var seamRect = (RectTransform)seam.transform;
            seamRect.anchorMin = new Vector2(.492f, .05f); seamRect.anchorMax = new Vector2(.508f, .95f);
            seamRect.offsetMin = seamRect.offsetMax = Vector2.zero;
            seam.GetComponent<Image>().color = new Color(.17f, .075f, .025f, .58f);

            CreateDecorativeImage(book.transform, "Top Page Edges", uiSprite,
                new Vector2(.045f, .94f), new Vector2(.955f, .958f), new Color(.72f, .59f, .36f, .72f));
            CreateDecorativeImage(book.transform, "Bottom Page Edges", uiSprite,
                new Vector2(.045f, .042f), new Vector2(.955f, .06f), new Color(.58f, .42f, .23f, .72f));
            CreateDecorativeImage(book.transform, "Left Inner Page Shadow", uiSprite,
                new Vector2(.465f, .065f), new Vector2(.493f, .935f), new Color(.33f, .20f, .08f, .2f));
            CreateDecorativeImage(book.transform, "Right Inner Page Shadow", uiSprite,
                new Vector2(.507f, .065f), new Vector2(.535f, .935f), new Color(.33f, .20f, .08f, .2f));

            var leftImage = CreatePagePortrait(leftPage.transform, "Left Monster Portrait");
            var rightImage = CreatePagePortrait(rightPage.transform, "Right Monster Portrait");
            var leftText = CreatePageDescription(leftPage.transform, "Left Monster Description");
            var rightText = CreatePageDescription(rightPage.transform, "Right Monster Description");

            var bookmarkRoot = new GameObject("Bookmark Root", typeof(RectTransform));
            bookmarkRoot.transform.SetParent(book.transform, false);
            Stretch((RectTransform)bookmarkRoot.transform);
            var regionButtons = new Button[4];
            for (var i = 0; i < regionButtons.Length; i++)
            {
                regionButtons[i] = FindOrCloneButton(bookmarkRoot.transform, template, $"Region Bookmark {i + 1}", $"지역 {i + 1}", Vector2.zero);
                regionButtons[i].image.sprite = uiSprite;
                regionButtons[i].image.type = Image.Type.Sliced;
                ConfigureBookmark(regionButtons[i], .29f + i * .14f, i);
            }

            var turningPage = CreatePage(book.transform, "Turning Page", rightPage.GetComponent<RectTransform>().anchorMin,
                rightPage.GetComponent<RectTransform>().anchorMax, uiSprite);
            turningPage.GetComponent<Button>().enabled = false;
            var turningBackground = turningPage.GetComponent<Image>();
            turningBackground.enabled = true;
            var turningImage = CreatePagePortrait(turningPage.transform, "Turning Monster Portrait");
            var turningText = CreatePageDescription(turningPage.transform, "Turning Monster Description");
            turningPage.SetActive(false);

            var foldRoot = new GameObject("Page Fold", typeof(RectTransform));
            foldRoot.transform.SetParent(book.transform, false);
            var foldRect = (RectTransform)foldRoot.transform;
            foldRect.anchorMin = new Vector2(.968f, .07f);
            foldRect.anchorMax = new Vector2(.968f, .93f);
            foldRect.pivot = new Vector2(.5f, .5f);
            foldRect.sizeDelta = new Vector2(12f, -72f);
            var foldShadow = CreateFoldStrip(foldRoot.transform, "Fold Shadow", uiSprite,
                new Vector2(0f, 0f), new Vector2(.72f, 1f), new Color(.16f, .08f, .025f, .08f));
            var foldHighlight = CreateFoldStrip(foldRoot.transform, "Fold Highlight", uiSprite,
                new Vector2(.62f, 0f), new Vector2(1f, 1f), new Color(1f, .94f, .76f, .18f));
            foldRoot.SetActive(false);
            bookmarkRoot.transform.SetAsLastSibling();

            var close = FindOrCloneButton(root.transform, template, "Close Button", "X", Vector2.zero);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(.955f, .075f);
            closeRect.pivot = new Vector2(.5f, .5f);
            closeRect.anchoredPosition = Vector2.zero;
            closeRect.sizeDelta = new Vector2(54f, 54f);
            var closeText = close.GetComponentInChildren<Text>(true); if (closeText != null) closeText.fontSize = 25;

            var view = root.GetComponent<MonsterCompendiumView>();
            var data = new SerializedObject(view);
            Assign(data, "root", root);
            AssignArray(data, "pageImages", new Object[] { leftImage, rightImage });
            AssignArray(data, "pageTexts", new Object[] { leftText, rightText });
            AssignArray(data, "pageButtons", new Object[] { leftPage.GetComponent<Button>(), rightPage.GetComponent<Button>() });
            AssignArray(data, "pageRoots", new Object[] { leftPage.transform, rightPage.transform });
            Assign(data, "turningPageRoot", turningPage.transform);
            Assign(data, "turningPageBackground", turningBackground);
            Assign(data, "turningPageImage", turningImage);
            Assign(data, "turningPageText", turningText);
            Assign(data, "pageFoldRoot", foldRoot.transform);
            Assign(data, "pageFoldShadow", foldShadow);
            Assign(data, "pageFoldHighlight", foldHighlight);
            var bookmarkRects = new Object[regionButtons.Length];
            for (var i = 0; i < regionButtons.Length; i++) bookmarkRects[i] = regionButtons[i].transform;
            AssignArray(data, "regionBookmarks", bookmarkRects);
            AssignArray(data, "regionButtons", regionButtons);
            Assign(data, "closeButton", close);
            data.FindProperty("revealAllForTesting").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);
            return view;
        }

        private static Image CreatePagePortrait(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(.2f, .51f); rect.anchorMax = new Vector2(.8f, .88f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>(); image.preserveAspect = true; image.enabled = false;
            return image;
        }

        private static Text CreatePageDescription(Transform parent, string name)
        {
            var text = CreateText(parent, name, new Vector2(.1f, .12f), new Vector2(.9f, .49f), 20);
            text.alignment = TextAnchor.UpperLeft; text.color = new Color(.2f, .12f, .055f);
            return text;
        }

        private static void ConfigureBookmark(Button button, float anchorX, int index)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.965f, .77f - index * .15f);
            rect.pivot = new Vector2(0f, .5f);
            rect.sizeDelta = new Vector2(130f, 58f);
            rect.anchoredPosition = Vector2.zero;
            button.image.color = new[]
            {
                new Color(.44f, .69f, .38f), new Color(.53f, .75f, .9f),
                new Color(.9f, .68f, .34f), new Color(.48f, .62f, .42f)
            }[index];
            var text = button.GetComponentInChildren<Text>(true); if (text != null) { text.fontSize = 18; text.color = Color.black; }
        }

        private static GameObject CreatePage(Transform parent, string name, Vector2 min, Vector2 max, Sprite sprite)
        {
            var page = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(UIButtonSound));
            page.transform.SetParent(parent, false);
            var rect = (RectTransform)page.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = page.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(.965f, .885f, .69f, 1f);
            return page;
        }

        private static Image CreateDecorativeImage(Transform parent, string name, Sprite sprite,
            Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = sprite; image.type = Image.Type.Sliced; image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateFoldStrip(Transform parent, string name, Sprite sprite,
            Vector2 min, Vector2 max, Color color)
        {
            var image = CreateDecorativeImage(parent, name, sprite, min, max, color);
            image.type = Image.Type.Sliced;
            return image;
        }

        private static Text CreateText(Transform parent, string name, Vector2 min, Vector2 max, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        private static Button FindOrCloneButton(Transform parent, Button template,
            string name, string label, Vector2 position)
        {
            var existing = parent.Find(name);
            var gameObject = existing != null
                ? existing.gameObject
                : Object.Instantiate(template.gameObject, parent);
            gameObject.name = name;
            var rect = (RectTransform)gameObject.transform;
            rect.anchoredPosition = position;
            var text = gameObject.GetComponentInChildren<Text>(true);
            if (text != null) text.text = label;
            if (gameObject.GetComponent<UIButtonSound>() == null)
                gameObject.AddComponent<UIButtonSound>();
            return gameObject.GetComponent<Button>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Assign(SerializedObject data, string property, Object value) =>
            data.FindProperty(property).objectReferenceValue = value;

        private static void AssignArray(SerializedObject data, string property, Object[] values)
        {
            var array = data.FindProperty(property);
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
