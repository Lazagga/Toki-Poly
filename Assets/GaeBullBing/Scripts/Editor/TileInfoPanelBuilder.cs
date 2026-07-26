#if UNITY_EDITOR
using GaeBullBing.Presentation.Game;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Editor
{
    public static class TileInfoPanelBuilder
    {
        private const string UiFontPath = "Assets/Fonts/NanumDongHwaDdoBag.ttf";

        [MenuItem("GaeBullBing/UI/Build Tile Information Panel")]
        public static void Build()
        {
            var interactionPanelsRoot =
                GameObject.Find("Game UI/Gameplay UI Root/Interaction Panels Root")?.transform;
            var fallbackCanvas = Object.FindFirstObjectByType<Canvas>();
            var panelParent = interactionPanelsRoot != null
                ? interactionPanelsRoot
                : fallbackCanvas != null
                    ? fallbackCanvas.transform
                    : null;
            if (panelParent == null)
            {
                Debug.LogError("Tile information panel requires Interaction Panels Root or a Canvas in the active scene.");
                return;
            }

            var old = panelParent.Find("Tile Information Panel");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = CreateUiObject("Tile Information Panel", panelParent);
            Undo.RegisterCreatedObjectUndo(root, "Build Tile Information Panel");
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, .5f);
            rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            rect.sizeDelta = new Vector2(430f, 620f);

            var image = root.AddComponent<Image>();
            image.color = new Color(.055f, .065f, .09f, .96f);
            image.raycastTarget = false;
            var view = root.AddComponent<TileInfoPanelView>();

            var title = CreateText("Title", root.transform, 39, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -18f), new Vector2(-36f, 52f), new Vector2(0f, 1f));
            title.color = new Color(1f, .84f, .28f);

            var divider = CreateUiObject("Divider", root.transform);
            var dividerImage = divider.AddComponent<Image>();
            dividerImage.color = new Color(1f, 1f, 1f, .14f);
            dividerImage.raycastTarget = false;
            SetRect(divider.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -72f), new Vector2(-36f, 2f), new Vector2(0f, 1f));

            var scrollView = CreateUiObject("Information Scroll View", root.transform);
            var scrollRectTransform = scrollView.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.pivot = new Vector2(.5f, .5f);
            scrollRectTransform.offsetMin = new Vector2(14f, 40f);
            scrollRectTransform.offsetMax = new Vector2(-14f, -82f);
            var scrollImage = scrollView.AddComponent<Image>();
            scrollImage.color = Color.clear;
            scrollImage.raycastTarget = true;
            var scrollMask = scrollView.AddComponent<RectMask2D>();
            scrollMask.padding = Vector4.zero;
            scrollView.AddComponent<BoardPointerPassthrough>();
            var informationScroll = scrollView.AddComponent<ScrollRect>();
            informationScroll.viewport = scrollRectTransform;
            informationScroll.horizontal = false;
            informationScroll.vertical = true;
            informationScroll.movementType = ScrollRect.MovementType.Clamped;
            informationScroll.inertia = true;
            informationScroll.scrollSensitivity = 28f;

            var content = CreateUiObject("Content", scrollView.transform);
            var contentRect = content.GetComponent<RectTransform>();
            SetRect(contentRect, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero, new Vector2(.5f, 1f));
            var contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(4, 4, 12, 12);
            contentLayout.spacing = 18f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var contentFitter = content.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            informationScroll.content = contentRect;

            var tower = CreateText("Tower Information", content.transform, 27, FontStyle.Normal, TextAnchor.UpperLeft);
            tower.resizeTextForBestFit = false;
            tower.verticalOverflow = VerticalWrapMode.Overflow;

            var monsterBox = CreateUiObject("Monster Background", content.transform);
            var monsterLayout = monsterBox.AddComponent<VerticalLayoutGroup>();
            monsterLayout.padding = new RectOffset(12, 12, 12, 12);
            monsterLayout.childAlignment = TextAnchor.UpperLeft;
            monsterLayout.childControlWidth = true;
            monsterLayout.childControlHeight = true;
            monsterLayout.childForceExpandWidth = true;
            monsterLayout.childForceExpandHeight = false;
            var monsterBoxFitter = monsterBox.AddComponent<ContentSizeFitter>();
            monsterBoxFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            monsterBoxFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var monsters = CreateText("Monster Information", monsterBox.transform, 27, FontStyle.Normal, TextAnchor.UpperLeft);
            monsters.resizeTextForBestFit = false;
            monsters.verticalOverflow = VerticalWrapMode.Overflow;

            var hint = CreateText("Hint", root.transform, 21, FontStyle.Italic, TextAnchor.MiddleRight);
            hint.text = "다른 타일 선택 · 바깥 클릭으로 닫기";
            hint.color = new Color(1f, 1f, 1f, .55f);
            SetRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(18f, 8f), new Vector2(-36f, 26f), new Vector2(0f, 0f));

            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("panelRoot").objectReferenceValue = root;
            serializedView.FindProperty("titleText").objectReferenceValue = title;
            serializedView.FindProperty("towerText").objectReferenceValue = tower;
            serializedView.FindProperty("monsterText").objectReferenceValue = monsters;
            serializedView.FindProperty("informationScroll").objectReferenceValue = informationScroll;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            var controller = Object.FindFirstObjectByType<GameController>();
            if (controller != null)
            {
                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("tileInfoPanel").objectReferenceValue = view;
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(controller);
            }

            root.SetActive(false);
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("Built the persistent Tile Information Panel UI.");
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            value.transform.SetParent(parent, false);
            value.layer = parent.gameObject.layer;
            return value;
        }

        private static Text CreateText(string name, Transform parent, int size, FontStyle style, TextAnchor anchor)
        {
            var value = CreateUiObject(name, parent);
            var text = value.AddComponent<Text>();
            text.font = LoadUiFont();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Font LoadUiFont() =>
            AssetDatabase.LoadAssetAtPath<Font>(UiFontPath) ??
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
#endif
