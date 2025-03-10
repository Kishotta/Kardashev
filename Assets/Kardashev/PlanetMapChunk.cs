using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Kardashev
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlanetMapChunk : MonoBehaviour
    {
        private Mesh _mesh;
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();
        private readonly List<Color> _colors = new();
        
        [Header("Tile Settings")]
        [Range(0.6f, 0.9f)]
        public float solidFactor = 0.75f;
        
        public Gradient elevationGradient;
        
        private void Awake()
        {
            GetComponent<MeshFilter>().mesh = _mesh = new Mesh();
            _mesh.name                      = "Planet Mesh";
        }

        public void SaveMesh()
        {
            _mesh.vertices = _vertices.ToArray();
            _mesh.colors = _colors.ToArray();
            _mesh.triangles = _triangles.ToArray();
            _mesh.RecalculateNormals();
            
            _vertices.Clear();
            _triangles.Clear();
            _colors.Clear();
        }
        
        public void Triangulate(PlanetMap planetMap, int tileIndex)
        {
            var spokeIndices = planetMap.GetTileSpokeIndices(tileIndex);
            for (var i = 0; i < spokeIndices.Length; i++)
            {
                Triangulate(planetMap, tileIndex, spokeIndices[i]);
            }
            spokeIndices.Dispose();
        }
        
        private void Triangulate(PlanetMap planetMap, int tileIndex, int spokeIndex)
        {
            var center = GetTileCenter(planetMap, tileIndex);
            var v1     = center + GetFirstSolidCorner(planetMap, center, spokeIndex);
            var v2     = center + GetSecondSolidCorner(planetMap, center, spokeIndex);
            
            var color = GetTileColor(planetMap, tileIndex);

            AddTriangle(center, v1, v2);
            AddTriangleColor(color);

            TriangulateConnection(planetMap, tileIndex, spokeIndex, v1, v2);
        }

        private Color GetTileColor(PlanetMap planetMap, int tileIndex)
        {
            var elevation           = math.round(planetMap.TileElevations[tileIndex]);
            var normalizedElevation = math.remap(-8, 8, 0, 1, elevation);
            var color               = elevationGradient.Evaluate(normalizedElevation);
            return color;
        }
        
        private static float GetTileElevation(PlanetMap planetMap, int tileIndex)
        {
            // return 0;
            return math.round(planetMap.TileElevations[tileIndex]) * 0.5f;
        }

        private void TriangulateConnection(PlanetMap planetMap, int tileIndex, int spokeIndex, float3 v1, float3 v2)
        {
            var oppositeSpokeIndex = planetMap.TileSpokeOpposites[spokeIndex];
            if (spokeIndex < oppositeSpokeIndex)
            {
                var bridge = GetBridge(planetMap, spokeIndex);
                var v3     = v1 + bridge;
                var v4     = v2 + bridge;
                
                AddQuad(v1, v2, v3, v4);
                AddQuadColor(
                    GetTileColor(planetMap, planetMap.Spokes[spokeIndex]), 
                    GetTileColor(planetMap, planetMap.Spokes[oppositeSpokeIndex]));
                
            }
            
            if (spokeIndex < PlanetMap.GetPreviousEdgeIndex(spokeIndex) &&
                spokeIndex < PlanetMap.GetNextEdgeIndex(spokeIndex))
            {
                var bridge = GetBridge(planetMap, spokeIndex);
                var v4     = v2 + bridge;
                
                AddTriangle(
                    v2, 
                    v4,
                    v2 + GetBridge(planetMap, planetMap.TileSpokeOpposites[PlanetMap.GetPreviousEdgeIndex(spokeIndex)]));
                AddTriangleColor(
                    GetTileColor(planetMap, planetMap.Spokes[spokeIndex]), 
                    GetTileColor(planetMap, planetMap.Spokes[oppositeSpokeIndex]),
                    GetTileColor(planetMap, planetMap.Spokes[PlanetMap.GetPreviousEdgeIndex(spokeIndex)]));
            }
        }

        private static float3 GetTileCenter(PlanetMap planetMap, int tileIndex)
        {
            var tileCorners = planetMap.GetTileCornerIndices(tileIndex);
            var center      = float3.zero;
            for (var i = 0; i < tileCorners.Length; i++)
            {
                center += planetMap.TileCorners[tileCorners[i]];
            }
            center /= tileCorners.Length;
            tileCorners.Dispose();
            var elevation = GetTileElevation(planetMap, tileIndex);
            center += math.normalize(center) * elevation;
            return center;
        }

        private float3 GetFirstSolidCorner(PlanetMap planetMap, float3 center, int spokeIndex)
        {
           return GetFirstCorner(planetMap, center, spokeIndex) * solidFactor;
        }
        
        private float3 GetSecondSolidCorner(PlanetMap planetMap, float3 center, int spokeIndex)
        {
            return GetSecondCorner(planetMap, center, spokeIndex)  * solidFactor;
        }
        
        private float3 GetFirstCorner(PlanetMap planetMap, float3 center, int spokeIndex)
        {
            var elevation = GetTileElevation(planetMap, planetMap.Spokes[spokeIndex]);
            return planetMap.TileCorners[planetMap.GetSpokeCorners(spokeIndex).corner2] + math.normalize(center) * elevation - center;
        }
        
        private float3 GetSecondCorner(PlanetMap planetMap, float3 center, int spokeIndex)
        {
            var elevation = GetTileElevation(planetMap, planetMap.Spokes[spokeIndex]);

            return planetMap.TileCorners[planetMap.GetSpokeCorners(spokeIndex).corner1] + math.normalize(center) * elevation - center;
        }

        private float3 GetBridge(PlanetMap planetMap, int spokeIndex)
        {
            var oppositeSpoke = planetMap.TileSpokeOpposites[spokeIndex];
            var center = GetTileCenter(planetMap, planetMap.Spokes[spokeIndex]);
            var otherCenter = GetTileCenter(planetMap, planetMap.Spokes[oppositeSpoke]);
            var c1 = center + GetFirstSolidCorner(planetMap, center, spokeIndex);
            var c2 = otherCenter + GetSecondSolidCorner(planetMap, otherCenter, oppositeSpoke);
            return c2 - c1;
        }
        
        private void AddTriangle (float3 v1, float3 v2, float3 v3) {
            var vertexIndex = _vertices.Count;
            _vertices.Add(v1);
            _vertices.Add(v2);
            _vertices.Add(v3);
            _triangles.Add(vertexIndex);
            _triangles.Add(vertexIndex + 1);
            _triangles.Add(vertexIndex + 2);
        }
        
        private void AddTriangleColor (Color color) {
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
        }
        
        private void AddTriangleColor (Color c1, Color c2, Color c3) {
            _colors.Add(c1);
            _colors.Add(c2);
            _colors.Add(c3);
        }

        private void AddQuad(float3 v1, float3 v2, float3 v3, float3 v4)
        {
            var vertexIndex = _vertices.Count;
            _vertices.Add(v1);
            _vertices.Add(v2);
            _vertices.Add(v3);
            _vertices.Add(v4);
            _triangles.Add(vertexIndex);
            _triangles.Add(vertexIndex + 2);
            _triangles.Add(vertexIndex + 1);
            _triangles.Add(vertexIndex + 1);
            _triangles.Add(vertexIndex + 2);
            _triangles.Add(vertexIndex + 3);
        }

        private void AddQuadColor(Color c1, Color c2, Color c3, Color c4)
        {
            _colors.Add(c1);
            _colors.Add(c2);
            _colors.Add(c3);
            _colors.Add(c4);
        }
        
        private void AddQuadColor(Color c1, Color c2)
        {
            _colors.Add(c1);
            _colors.Add(c1);
            _colors.Add(c2);
            _colors.Add(c2);
        }
    }
}
