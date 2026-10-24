using GaeBullBing.Core.Board;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GaeBullBing.Presentation.Board
{
    public sealed partial class BoardTilemapView
    {
        [Header("Overgrown Tiles")]
        [SerializeField] private Color overgrowthColor = new(.28f, .65f, .12f, 1f);
        [SerializeField] private Vector2 overgrowthOffset = new(0f, .015f);
        [SerializeField, Min(.1f)] private float overgrowthScale = 1f;
        private readonly System.Collections.Generic.Dictionary<int, MeshRenderer> overgrowthRenderers = new();
        private Mesh overgrowthMesh;
        private Material overgrowthMaterial;

        private void RefreshOvergrowth(BoardState board)
        {
            if (!Application.isPlaying) return;
            foreach (var tile in board.Tiles)
            {
                if (!overgrowthRenderers.TryGetValue(tile.Index, out var renderer) && tile.HasOvergrowth)
                {
                    EnsureOvergrowthResources();
                    var marker = new GameObject($"Tile {tile.Index} Overgrowth", typeof(MeshFilter), typeof(MeshRenderer));
                    marker.transform.SetParent(transform, false);
                    marker.GetComponent<MeshFilter>().sharedMesh = overgrowthMesh;
                    renderer = marker.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = overgrowthMaterial;
                    renderer.sortingLayerID = GetComponent<TilemapRenderer>().sortingLayerID;
                    overgrowthRenderers.Add(tile.Index, renderer);
                }
                if (renderer == null) continue;
                renderer.gameObject.SetActive(tile.HasOvergrowth);
                PositionOvergrowth(tile.Index, renderer);
            }
            if (overgrowthMaterial != null) overgrowthMaterial.color = overgrowthColor;
        }

        private void EnsureOvergrowthResources()
        {
            if (overgrowthMaterial == null)
            {
                overgrowthMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Overgrowth (runtime)", color = overgrowthColor };
                overgrowthMaterial.mainTexture = Texture2D.whiteTexture;
            }
            if (overgrowthMesh != null) return;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            // A translucent ground diamond and two tufts leave the tower/units visible.
            vertices.AddRange(new[] { new Vector3(-.43f, 0), new Vector3(0, .215f), new Vector3(.43f, 0), new Vector3(0, -.215f) });
            for (var i = 0; i < 4; i++) colors.Add(new Color(1, 1, 1, .25f));
            triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            foreach (var x in new[] { -.27f, .27f })
                for (var blade = -1; blade <= 1; blade++)
                {
                    var baseX = x + blade * .035f;
                    var index = vertices.Count;
                    vertices.Add(new Vector3(baseX - .023f, -.05f));
                    vertices.Add(new Vector3(baseX + blade * .07f, blade == 0 ? .23f : .15f));
                    vertices.Add(new Vector3(baseX + .023f, -.05f));
                    colors.Add(new Color(.45f, .65f, .35f)); colors.Add(Color.white); colors.Add(new Color(.45f, .65f, .35f));
                    triangles.AddRange(new[] { index, index + 1, index + 2 });
                }
            overgrowthMesh = new Mesh { name = "Overgrowth marker (runtime)" };
            overgrowthMesh.SetVertices(vertices); overgrowthMesh.SetColors(colors);
            overgrowthMesh.SetTriangles(triangles, 0);
            overgrowthMesh.uv = new Vector2[vertices.Count];
            overgrowthMesh.RecalculateBounds();
        }

        private void PositionOvergrowth(int tileIndex, MeshRenderer renderer)
        {
            if (renderer == null) return;
            var position = GetWorldPosition(tileIndex);
            renderer.transform.position = position + GetTileVisualWorldOffset(tileIndex) + (Vector3)overgrowthOffset;
            var sprite = Tilemap.GetSprite(GetCellPosition(tileIndex));
            renderer.transform.localScale = Vector3.one * (sprite != null ? sprite.bounds.size.x : 1f) * overgrowthScale;
            renderer.sortingOrder = BoardDepthSorting.GetOrder(position, -98);
        }

        private void OnDestroy()
        {
            if (overgrowthMesh != null) Destroy(overgrowthMesh);
            if (overgrowthMaterial != null) Destroy(overgrowthMaterial);
        }
    }
}
