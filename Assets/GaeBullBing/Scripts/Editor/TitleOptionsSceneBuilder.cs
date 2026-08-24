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
                "Settings Button", "게임 설정", new Vector2(0f, -150f));
            var quitButton = FindOrCloneButton(main.transform, startButton,
                "Quit Button", "게임 종료", new Vector2(0f, -265f));

            var oldSettings = titleRoot.transform.Find("Title Audio Settings");
            if (oldSettings != null) Object.DestroyImmediate(oldSettings.gameObject);
            var settingsObject = Object.Instantiate(pauseSettings.gameObject, titleRoot.transform);
            settingsObject.name = "Title Audio Settings";
            var settingsRect = (RectTransform)settingsObject.transform;
            Stretch(settingsRect);
            var titleSettings = settingsObject.GetComponent<AudioSettingsView>();
            settingsObject.SetActive(false);

            flowData.Update();
            Assign(flowData, "titleMainRoot", main);
            Assign(flowData, "titleSettingsButton", settingsButton);
            Assign(flowData, "titleQuitButton", quitButton);
            Assign(flowData, "titleAudioSettingsView", titleSettings);
            flowData.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(flow);
            EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
            EditorSceneManager.SaveScene(flow.gameObject.scene);
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
    }
}
