using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core.Dice;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.Dice
{
    public sealed class Dice3DPresenter : MonoBehaviour
    {
        [SerializeField, Min(0.2f)] private float rollDuration = 1.45f;
        [SerializeField, Min(0f)] private float resultHoldDuration = 0.18f;
        [SerializeField] private float diceSize = 1f;
        [SerializeField] private RawImage display;

        private Vector3 stageCenter;
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.forward, Vector3.right, Vector3.up,
            Vector3.down, Vector3.left, Vector3.back
        };
        private DieView first;
        private DieView second;
        private GameObject stageRoot;
        private Material firstDiceMaterial;
        private Material secondDiceMaterial;
        private Material blackPipMaterial;
        private Material whitePipMaterial;
        private int lastPreset = -1;

        public void Initialize(BoardTilemapView boardView)
        {
            if (first != null)
                return;
            ConfigureDisplay();
            stageCenter = boardView.GetBoardCenterWorld();
            CreateStage();
        }

        public IEnumerator Roll(IReadOnlyList<DiceState> diceStates, int firstResult, int secondResult)
        {
            var audio = AudioManager.Instance;
            audio?.PlaySfx(audio.Dice.Roll);
            ApplyAppearance(first, firstDiceMaterial, diceStates[0]);
            ApplyAppearance(second, secondDiceMaterial, diceStates[1]);

            var firstTarget = stageCenter + new Vector3(-0.65f, diceSize * 0.5f + 0.01f, 0f);
            var secondTarget = stageCenter + new Vector3(0.65f, diceSize * 0.5f + 0.01f, 0f);
            first.SetFaceValues(BuildPhysicalFaces(diceStates[0]));
            second.SetFaceValues(BuildPhysicalFaces(diceStates[1]));
            stageRoot.SetActive(true);

            var preset = SelectPreset();
            for (var elapsed = 0f; elapsed < rollDuration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / rollDuration);
                AnimatePreset(preset, progress, firstTarget, secondTarget);
                yield return null;
            }

            SetFinalPose(first, ProjectToBoard(firstTarget), firstResult);
            SetFinalPose(second, ProjectToBoard(secondTarget), secondResult);
            audio?.PlaySfx(audio.Dice.Land);
            if (resultHoldDuration > 0f)
                yield return new WaitForSeconds(resultHoldDuration);

            stageRoot.SetActive(false);
            yield return null;
        }

        private int SelectPreset()
        {
            if (lastPreset < 0)
                return lastPreset = Random.Range(0, 3);

            var selection = Random.Range(0, 2);
            if (selection >= lastPreset)
                selection++;
            lastPreset = selection;
            return selection;
        }

        private void AnimatePreset(int preset, float t, Vector3 firstTarget, Vector3 secondTarget)
        {
            AnimateCollisionPreset(preset, t, firstTarget, secondTarget);
        }

        private void AnimateCollisionPreset(int preset, float t, Vector3 firstTarget, Vector3 secondTarget)
        {
            Vector3 firstStartOffset;
            Vector3 secondStartOffset;
            Vector3 firstReboundOffset;
            Vector3 secondReboundOffset;
            Vector3 firstTurns;
            Vector3 secondTurns;

            switch (preset)
            {
                case 0: // 정면 충돌
                    firstStartOffset = new Vector3(-2.9f, 3.4f, -1.25f);
                    secondStartOffset = new Vector3(2.9f, 3.6f, -1.25f);
                    firstReboundOffset = new Vector3(-2.25f, 2.15f, 1.15f);
                    secondReboundOffset = new Vector3(2.25f, 2.2f, -1.15f);
                    firstTurns = new Vector3(-6f, 5f, 4f);
                    secondTurns = new Vector3(6f, -5f, -4f);
                    break;
                case 1: // 대각선 충돌
                    firstStartOffset = new Vector3(-2.8f, 3.8f, 1.7f);
                    secondStartOffset = new Vector3(2.75f, 3.3f, -1.8f);
                    firstReboundOffset = new Vector3(-2.05f, 2.35f, -1.4f);
                    secondReboundOffset = new Vector3(2.1f, 2.25f, 1.45f);
                    firstTurns = new Vector3(7f, -5f, 4f);
                    secondTurns = new Vector3(-6f, 7f, -5f);
                    break;
                default: // 교차 후 크게 튕김
                    firstStartOffset = new Vector3(-2.5f, 4.1f, -1.85f);
                    secondStartOffset = new Vector3(2.55f, 4f, 1.85f);
                    firstReboundOffset = new Vector3(-2.45f, 2.65f, 1.55f);
                    secondReboundOffset = new Vector3(2.45f, 2.7f, -1.55f);
                    firstTurns = new Vector3(-8f, 7f, 6f);
                    secondTurns = new Vector3(8f, -6f, -7f);
                    break;
            }

            var impactHalfDistance = diceSize * 0.53f;
            var firstStart = stageCenter + firstStartOffset;
            var secondStart = stageCenter + secondStartOffset;
            var firstImpact = stageCenter + new Vector3(-impactHalfDistance, diceSize * 0.62f, -0.04f);
            var secondImpact = stageCenter + new Vector3(impactHalfDistance, diceSize * 0.62f, 0.04f);
            var firstRebound = stageCenter + firstReboundOffset;
            var secondRebound = stageCenter + secondReboundOffset;

            first.Transform.position = ProjectToBoard(GetCollisionPathPosition(
                t, firstStart, firstImpact, firstRebound, firstTarget));
            second.Transform.position = ProjectToBoard(GetCollisionPathPosition(
                t, secondStart, secondImpact, secondRebound, secondTarget));
            first.Transform.rotation = Quaternion.Euler(firstTurns * (360f * t));
            second.Transform.rotation = Quaternion.Euler(secondTurns * (360f * t));
        }

        private static Vector3 GetCollisionPathPosition(
            float t, Vector3 start, Vector3 impact, Vector3 rebound, Vector3 target)
        {
            const float impactTime = 0.34f;
            const float reboundEndTime = 0.69f;
            if (t <= impactTime)
            {
                var approach = Mathf.SmoothStep(0f, 1f, t / impactTime);
                var position = Vector3.Lerp(start, impact, approach);
                position.y += Mathf.Sin(approach * Mathf.PI) * 1.05f;
                return position;
            }

            if (t <= reboundEndTime)
            {
                var bounce = Mathf.SmoothStep(0f, 1f,
                    (t - impactTime) / (reboundEndTime - impactTime));
                var position = Vector3.Lerp(impact, rebound, bounce);
                position.y += Mathf.Sin(bounce * Mathf.PI) * 1.35f;
                return position;
            }

            var landing = Mathf.SmoothStep(0f, 1f,
                (t - reboundEndTime) / (1f - reboundEndTime));
            var landingPosition = Vector3.Lerp(rebound, target, landing);
            landingPosition.y += Mathf.Abs(Mathf.Sin(landing * Mathf.PI * 2f)) *
                                 (1f - landing) * 0.48f;
            return landingPosition;
        }

        private static void SetFinalPose(DieView die, Vector3 target, int result)
        {
            die.Transform.position = target;
            var faceIndex = System.Array.IndexOf(die.FaceValues, result);
            if (faceIndex < 0) faceIndex = 0;
            die.Transform.rotation = Quaternion.FromToRotation(FaceNormals[faceIndex], Vector3.back);
        }

        private Vector3 ProjectToBoard(Vector3 position)
        {
            var offset = position - stageCenter;
            return new Vector3(stageCenter.x + offset.x * .55f + offset.z * .3f,
                stageCenter.y + offset.y * .48f + offset.z * .15f, -2f);
        }

        private void ConfigureDisplay()
        {
            if (display == null)
                throw new MissingReferenceException("Dice3DPresenter에 3D Dice Display UI가 연결되지 않았습니다.");
            display.enabled = false;
            display.raycastTarget = false;
            display.texture = null;
        }

        private void CreateStage()
        {
            var stage = new GameObject("3D Dice Animation Stage");
            stageRoot = stage;
            stage.transform.SetParent(transform, false);

            var shader = Shader.Find("GaeBullBing/DiceOverlay");
            if (shader == null) throw new MissingReferenceException("DiceOverlay shader was not found.");
            firstDiceMaterial = new Material(shader) { color = new Color(0.96f, 0.96f, 0.93f) };
            secondDiceMaterial = new Material(shader) { color = new Color(0.035f, 0.035f, 0.045f) };
            blackPipMaterial = new Material(shader) { color = Color.black };
            whitePipMaterial = new Material(shader) { color = Color.white };
            firstDiceMaterial.renderQueue = 4100;
            secondDiceMaterial.renderQueue = 4100;
            blackPipMaterial.renderQueue = 4110;
            whitePipMaterial.renderQueue = 4110;
            first = CreateDie(stage.transform, "3D Dice 1", firstDiceMaterial, blackPipMaterial);
            second = CreateDie(stage.transform, "3D Dice 2", secondDiceMaterial, whitePipMaterial);

            stage.SetActive(false);
        }

        private DieView CreateDie(Transform parent, string objectName, Material material, Material pipMaterial)
        {
            var die = GameObject.CreatePrimitive(PrimitiveType.Cube);
            die.name = objectName;
            die.transform.SetParent(parent, false);
            die.transform.localScale = Vector3.one * (diceSize * .48f);
            die.GetComponent<MeshRenderer>().sharedMaterial = material;
            Destroy(die.GetComponent<BoxCollider>());
            die.transform.position = ProjectToBoard(stageCenter + new Vector3(0f, diceSize * 0.5f, 0f));

            var faces = new DicePipFace[FaceNormals.Length];
            for (var i = 0; i < faces.Length; i++)
                faces[i] = DicePipFace.Create(die.transform, FaceNormals[i], pipMaterial, .105f);
            return new DieView(die.transform, faces);
        }

        private static int[] BuildPhysicalFaces(DiceState dice)
        {
            var values = new List<int>(6);
            for (var faceIndex = 0; faceIndex < dice.Faces.Length; faceIndex++)
                for (var count = 0; count < dice.Weights[faceIndex]; count++)
                    values.Add(dice.Faces[faceIndex]);
            while (values.Count < 6) values.Add(dice.Faces[0]);
            if (values.Count > 6) values.RemoveRange(6, values.Count - 6);
            return values.ToArray();
        }

        private void OnDestroy()
        {
            if (firstDiceMaterial != null)
                Destroy(firstDiceMaterial);
            if (secondDiceMaterial != null)
                Destroy(secondDiceMaterial);
            if (blackPipMaterial != null)
                Destroy(blackPipMaterial);
            if (whitePipMaterial != null)
                Destroy(whitePipMaterial);
        }

        private sealed class DieView
        {
            public DieView(Transform transform, DicePipFace[] faces) { Transform = transform; Faces = faces; }
            public Transform Transform { get; }
            public DicePipFace[] Faces { get; }
            public int[] FaceValues { get; private set; }
            public void SetFaceValues(int[] values)
            {
                FaceValues = values;
                for (var i = 0; i < Faces.Length; i++) Faces[i].SetValue(values[i]);
            }
        }


private void ApplyAppearance(DieView die, Material bodyMaterial, DiceState state)
        {
            bodyMaterial.color = new Color(state.Red, state.Green, state.Blue, 1f);
            var pipMaterial = state.UsesBlackPips ? blackPipMaterial : whitePipMaterial;
            foreach (var face in die.Faces)
                face.SetMaterial(pipMaterial);
        }
}
}
