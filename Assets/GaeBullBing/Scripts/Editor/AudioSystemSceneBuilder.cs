using GaeBullBing.Presentation.Audio;
using GaeBullBing.Presentation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Editor
{
    public static class AudioSystemSceneBuilder
    {
        [MenuItem("GaeBullBing/Audio/Build Audio Settings UI")]
        public static void Build()
        {
            var pause = Object.FindFirstObjectByType<PauseMenuView>(FindObjectsInactive.Include);
            if (pause == null) throw new MissingReferenceException("PauseMenuView not found.");
            var pauseData = new SerializedObject(pause);
            var hanging = pauseData.FindProperty("hangingPanel").objectReferenceValue as RectTransform;
            if (hanging == null) throw new MissingReferenceException("Hanging Panel is not assigned.");

            EnsureAudioManager();
            var mainRoot = GetOrCreateRect("Main Options", hanging);
            Stretch(mainRoot);
            var continueButton = GetButton(pauseData, "continueButton");
            var restartButton = GetButton(pauseData, "restartButton");
            var titleButton = GetButton(pauseData, "titleButton");
            var settingsButton = GetButton(pauseData, "settingsButton");
            if (settingsButton == null)
                settingsButton = CloneButton(restartButton ?? titleButton, mainRoot, "Settings Button", "설정");
            var pauseHeading = hanging.Find("Pause");
            if (pauseHeading != null)
                pauseHeading.SetParent(mainRoot, false);

            var buttons = new[] { continueButton, restartButton, settingsButton, titleButton };
            var yPositions = new[] { 125f, 35f, -55f, -145f };
            for (var index = 0; index < buttons.Length; index++)
            {
                if (buttons[index] == null) continue;
                buttons[index].transform.SetParent(mainRoot, false);
                ((RectTransform)buttons[index].transform).anchoredPosition =
                    new Vector2(0f, yPositions[index]);
                if (buttons[index].GetComponent<UIButtonSound>() == null)
                    buttons[index].gameObject.AddComponent<UIButtonSound>();
            }

            var oldSettings = hanging.Find("Audio Settings");
            if (oldSettings != null) Object.DestroyImmediate(oldSettings.gameObject);
            var settingsRoot = GetOrCreateRect("Audio Settings", hanging);
            Stretch(settingsRoot);
            var view = settingsRoot.gameObject.AddComponent<AudioSettingsView>();
            var font = hanging.GetComponentInChildren<Text>(true)?.font;
            var heading = CreateText("Settings Title", settingsRoot, font,
                new Vector2(0f, 205f), new Vector2(420f, 55f), 38, TextAnchor.MiddleCenter);
            heading.text = "사운드 설정";

            var names = new[] { "전체 음량", "배경음", "효과음", "UI" };
            var y = new[] { 115f, 45f, -25f, -95f };
            var sliders = new Slider[4];
            var values = new Text[4];
            for (var index = 0; index < names.Length; index++)
            {
                var label = CreateText(names[index] + " Label", settingsRoot, font,
                    new Vector2(-260f, y[index]), new Vector2(180f, 42f), 25,
                    TextAnchor.MiddleLeft);
                label.text = names[index];
                sliders[index] = CreateSlider(names[index] + " Slider", settingsRoot,
                    new Vector2(35f, y[index]));
                values[index] = CreateText(names[index] + " Value", settingsRoot, font,
                    new Vector2(255f, y[index]), new Vector2(85f, 40f), 23,
                    TextAnchor.MiddleRight);
                values[index].text = "100%";
            }

            var backButton = CloneButton(titleButton ?? restartButton, settingsRoot,
                "Back Button", "뒤로");
            ((RectTransform)backButton.transform).anchoredPosition = new Vector2(0f, -180f);
            if (backButton.GetComponent<UIButtonSound>() == null)
                backButton.gameObject.AddComponent<UIButtonSound>();

            var viewData = new SerializedObject(view);
            Assign(viewData, "settingsRoot", settingsRoot.gameObject);
            Assign(viewData, "masterSlider", sliders[0]);
            Assign(viewData, "bgmSlider", sliders[1]);
            Assign(viewData, "sfxSlider", sliders[2]);
            Assign(viewData, "uiSlider", sliders[3]);
            Assign(viewData, "masterValueText", values[0]);
            Assign(viewData, "bgmValueText", values[1]);
            Assign(viewData, "sfxValueText", values[2]);
            Assign(viewData, "uiValueText", values[3]);
            Assign(viewData, "backButton", backButton);
            viewData.ApplyModifiedPropertiesWithoutUndo();

            pauseData.Update();
            Assign(pauseData, "settingsButton", settingsButton);
            Assign(pauseData, "mainOptionsRoot", mainRoot.gameObject);
            Assign(pauseData, "audioSettingsView", view);
            pauseData.ApplyModifiedPropertiesWithoutUndo();
            settingsRoot.gameObject.SetActive(false);
            EditorUtility.SetDirty(pause);
            EditorSceneManager.MarkSceneDirty(pause.gameObject.scene);
            EditorSceneManager.SaveScene(pause.gameObject.scene);
        }

        private static void EnsureAudioManager()
        {
            if (Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include) != null) return;
            new GameObject("Audio System", typeof(AudioManager));
        }

        private static Button GetButton(SerializedObject data, string name) =>
            data.FindProperty(name).objectReferenceValue as Button;

        private static RectTransform GetOrCreateRect(string name, Transform parent)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return (RectTransform)gameObject.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Button CloneButton(Button template, Transform parent,
            string name, string text)
        {
            if (template == null) throw new MissingReferenceException("Button template not found.");
            var clone = Object.Instantiate(template.gameObject, parent);
            clone.name = name;
            var label = clone.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
            return clone.GetComponent<Button>();
        }

        private static Text CreateText(string name, Transform parent, Font font,
            Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var rect = (RectTransform)gameObject.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = gameObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            return text;
        }

        private static Slider CreateSlider(string name, Transform parent, Vector2 position)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(280f, 34f);
            rootRect.anchoredPosition = position;
            var slider = root.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            var background = CreateImage("Background", root.transform,
                new Color(.12f, .15f, .12f, .9f));
            background.anchorMin = new Vector2(0f, .35f);
            background.anchorMax = new Vector2(1f, .65f);
            background.offsetMin = background.offsetMax = Vector2.zero;
            var fillArea = GetOrCreateRect("Fill Area", root.transform);
            fillArea.anchorMin = new Vector2(0f, .35f);
            fillArea.anchorMax = new Vector2(1f, .65f);
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = new Vector2(-10f, 0f);
            var fill = CreateImage("Fill", fillArea, new Color(.95f, .72f, .26f, 1f));
            Stretch(fill);
            var handleArea = GetOrCreateRect("Handle Slide Area", root.transform);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);
            var handle = CreateImage("Handle", handleArea, new Color(1f, .85f, .45f, 1f));
            handle.sizeDelta = new Vector2(24f, 32f);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            gameObject.GetComponent<Image>().color = color;
            return (RectTransform)gameObject.transform;
        }

        private static void Assign(SerializedObject data, string property, Object value) =>
            data.FindProperty(property).objectReferenceValue = value;
    }
}
