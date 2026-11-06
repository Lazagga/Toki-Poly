using System.Collections.Generic;
using GaeBullBing.Core.Board;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GaeBullBing.Presentation.Board
{
    public sealed partial class BoardTilemapView
    {
        [Header("Water Trail (temporary visual)")]
        [SerializeField] private Color waterTrailColor = new(.15f, .7f, .95f, .4f);
        [SerializeField] private Vector2 waterTrailOffset = new(0f, .015f);
        private readonly Dictionary<int, MeshRenderer> waterTrailRenderers = new();
        private Mesh waterTrailMesh;
        private Material waterTrailMaterial;

        private void RefreshWaterTrails(BoardState board)
        {
            if (!Application.isPlaying) return;
            foreach (var tile in board.Tiles)
            {
                var active = tile.WaterTrailTurnsRemaining > 0;
                if (!waterTrailRenderers.TryGetValue(tile.Index, out var renderer) && active)
                {
                    if (waterTrailMaterial == null)
                        waterTrailMaterial = new Material(Shader.Find("Sprites/Default")) {
                            name = "Water trail (runtime)", mainTexture = Texture2D.whiteTexture };
                    if (waterTrailMesh == null)
                    {
                        waterTrailMesh = new Mesh { name = "Water trail diamond (runtime)" };
                        waterTrailMesh.vertices = new[] { new Vector3(-.43f, 0), new Vector3(0, .215f),
                            new Vector3(.43f, 0), new Vector3(0, -.215f) };
                        waterTrailMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                        waterTrailMesh.uv = new Vector2[4];
                        waterTrailMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                        waterTrailMesh.RecalculateBounds();
                    }
                    var marker = new GameObject($"Tile {tile.Index} Water Trail", typeof(MeshFilter), typeof(MeshRenderer));
                    marker.transform.SetParent(transform, false);
                    marker.GetComponent<MeshFilter>().sharedMesh = waterTrailMesh;
                    renderer = marker.GetComponent<MeshRenderer>(); renderer.sharedMaterial = waterTrailMaterial;
                    renderer.sortingLayerID = GetComponent<TilemapRenderer>().sortingLayerID;
                    waterTrailRenderers.Add(tile.Index, renderer);
                }
                if (renderer == null) continue;
                renderer.gameObject.SetActive(active); PositionWaterTrail(tile.Index, renderer);
            }
            if (waterTrailMaterial != null) waterTrailMaterial.color = waterTrailColor;
        }

        private void PositionWaterTrail(int tileIndex, MeshRenderer renderer)
        {
            if (renderer == null) return;
            var position = GetWorldPosition(tileIndex);
            renderer.transform.position = position + GetTileVisualWorldOffset(tileIndex) + (Vector3)waterTrailOffset;
            var sprite = Tilemap.GetSprite(GetCellPosition(tileIndex));
            renderer.transform.localScale = Vector3.one * (sprite != null ? sprite.bounds.size.x : 1f);
            renderer.sortingOrder = BoardDepthSorting.GetOrder(position, -99);
        }
    }
}
