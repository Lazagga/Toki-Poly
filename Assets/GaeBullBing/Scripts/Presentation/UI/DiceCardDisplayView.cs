using System.Collections.Generic;
using GaeBullBing.Core.Dice;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.UI
{
    public sealed class DiceCardDisplayView : MonoBehaviour
    {
        [SerializeField] private Text nameText;
        [SerializeField] private Text faceValuesText;
        [SerializeField] private DiceFacePipGraphic featuredFace;
        [SerializeField] private DiceFacePipGraphic[] faceGraphics;
        [SerializeField] private string emptyName = "주사위 선택";
        [SerializeField] private bool useDiceTextContrast;

        public void Bind(DiceState dice)
        {
            if (dice == null)
            {
                if (nameText != null) nameText.text = emptyName;
                if (faceValuesText != null) faceValuesText.text = string.Empty;
                if (featuredFace != null) featuredFace.gameObject.SetActive(false);
                SetFaceGraphicsVisible(false);
                return;
            }

            var values = BuildPhysicalFaces(dice);
            var diceColor = new Color(dice.Red, dice.Green, dice.Blue, 1f);
            if (useDiceTextContrast)
            {
                var textColor = dice.UsesBlackPips ? Color.black : Color.white;
                if (nameText != null) nameText.color = textColor;
                if (faceValuesText != null) faceValuesText.color = textColor;
            }

            if (nameText != null) nameText.text = dice.DisplayName;
            if (faceValuesText != null) faceValuesText.text = string.Join(" ", values);

            if (featuredFace != null)
            {
                featuredFace.gameObject.SetActive(true);
                featuredFace.SetColors(diceColor, dice.UsesBlackPips);
                featuredFace.SetValue(Max(values));
            }

            for (var index = 0; index < faceGraphics.Length; index++)
            {
                var graphic = faceGraphics[index];
                var active = index < values.Count;
                graphic.gameObject.SetActive(active);
                if (!active) continue;
                graphic.SetColors(diceColor, dice.UsesBlackPips);
                graphic.SetValue(values[index]);
            }
        }

        private void SetFaceGraphicsVisible(bool visible)
        {
            foreach (var graphic in faceGraphics)
                if (graphic != null)
                    graphic.gameObject.SetActive(visible);
        }

        private static List<int> BuildPhysicalFaces(DiceState dice)
        {
            var values = new List<int>(6);
            for (var faceIndex = 0; faceIndex < dice.Faces.Length; faceIndex++)
                for (var count = 0; count < dice.Weights[faceIndex] && values.Count < 6; count++)
                    values.Add(dice.Faces[faceIndex]);

            while (values.Count < 6)
                values.Add(dice.Faces[0]);
            return values;
        }

        private static int Max(IReadOnlyList<int> values)
        {
            var maximum = values[0];
            for (var index = 1; index < values.Count; index++)
                if (values[index] > maximum)
                    maximum = values[index];
            return maximum;
        }
    }
}
