using System.Collections.Generic;
using Kardashev.PlanetGeneration;
using Unity.Mathematics;
using UnityEngine;

namespace Kardashev
{
    public enum RenderLayer
    {
        Elevation,
        Temperature,
        ElevationTemperature,
    }
    
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlanetMeshChunk : MonoBehaviour
    {
        private Mesh _mesh;
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();
        private readonly List<Color> _colors = new();
        
        [Header("Tile Settings")]
        [Range(0.6f, 0.9f)]
        public float solidFactor = 0.75f;
        
        [Header("Render Settings")]
        public RenderLayer renderLayer = RenderLayer.Elevation;
        
        public Gradient elevationGradient;
        public Gradient temperatureGradient;
        
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
        
        public void Triangulate(Planet planet, int tileIndex)
        {
            var spokeIndices = planet.GetTileSpokeIndices(tileIndex);
            for (var i = 0; i < spokeIndices.Length; i++)
            {
                Triangulate(planet, tileIndex, spokeIndices[i]);
            }
            spokeIndices.Dispose();
        }
        
        private void Triangulate(Planet planet, int tileIndex, int spokeIndex)
        {
            var center = GetTileCenter(planet, tileIndex);
            var v1     = center + GetFirstSolidCorner(planet, center, spokeIndex);
            var v2     = center + GetSecondSolidCorner(planet, center, spokeIndex);
            
            var color = GetTileColor(planet, tileIndex);

            AddTriangle(center, v1, v2);
            AddTriangleColor(color);

            TriangulateConnection(planet, tileIndex, spokeIndex, v1, v2);
        }

        private Color GetTileColor(Planet planet, int tileIndex)
        {
            var elevation           = math.round(planet.TileElevations[tileIndex]);
            var normalizedElevation = math.remap(-10, 10, 0, 1, elevation);
            var elevationColor      = elevationGradient.Evaluate(normalizedElevation);
            
            var temperature         = planet.TileTemperatures[tileIndex];
            var normalizedTemperature = math.remap(-20, 40, 0, 1, temperature);
            var temperatureColor               = temperatureGradient.Evaluate(normalizedTemperature);
            return renderLayer switch
            {
                RenderLayer.Temperature => temperatureColor,
                RenderLayer.Elevation    => elevationColor,
                RenderLayer.ElevationTemperature => temperatureColor * elevationColor,
                _ => Color.white
            };
        }
        
        private static float GetTileElevation(Planet planet, int tileIndex)
        {
            return 0;
            // return math.round(planet.TileElevations[tileIndex]) * 0.5f;
        }

        private void TriangulateConnection(Planet planet, int tileIndex, int spokeIndex, float3 v1, float3 v2)
        {
            var oppositeSpokeIndex = planet.TileSpokeOpposites[spokeIndex];
            if (spokeIndex < oppositeSpokeIndex)
            {
                var bridge = GetBridge(planet, spokeIndex);
                var v3     = v1 + bridge;
                var v4     = v2 + bridge;
                
                AddQuad(v1, v2, v3, v4);
                AddQuadColor(
                    GetTileColor(planet, planet.Spokes[spokeIndex]), 
                    GetTileColor(planet, planet.Spokes[oppositeSpokeIndex]));
                
            }
            
            if (spokeIndex < Planet.GetPreviousEdgeIndex(spokeIndex) &&
                spokeIndex < Planet.GetNextEdgeIndex(spokeIndex))
            {
                var bridge = GetBridge(planet, spokeIndex);
                var v4     = v2 + bridge;
                
                AddTriangle(
                    v2, 
                    v4,
                    v2 + GetBridge(planet, planet.TileSpokeOpposites[Planet.GetPreviousEdgeIndex(spokeIndex)]));
                AddTriangleColor(
                    GetTileColor(planet, planet.Spokes[spokeIndex]), 
                    GetTileColor(planet, planet.Spokes[oppositeSpokeIndex]),
                    GetTileColor(planet, planet.Spokes[Planet.GetPreviousEdgeIndex(spokeIndex)]));
            }
        }

        private static float3 GetTileCenter(Planet planet, int tileIndex)
        {
            var tileCorners = planet.GetTileCornerIndices(tileIndex);
            var center      = float3.zero;
            for (var i = 0; i < tileCorners.Length; i++)
            {
                center += planet.TileCorners[tileCorners[i]];
            }
            center /= tileCorners.Length;
            tileCorners.Dispose();
            var elevation = GetTileElevation(planet, tileIndex);
            center += math.normalize(center) * elevation;
            return center;
        }

        private float3 GetFirstSolidCorner(Planet planet, float3 center, int spokeIndex)
        {
           return GetFirstCorner(planet, center, spokeIndex) * solidFactor;
        }
        
        private float3 GetSecondSolidCorner(Planet planet, float3 center, int spokeIndex)
        {
            return GetSecondCorner(planet, center, spokeIndex)  * solidFactor;
        }
        
        private float3 GetFirstCorner(Planet planet, float3 center, int spokeIndex)
        {
            var elevation = GetTileElevation(planet, planet.Spokes[spokeIndex]);
            return planet.TileCorners[planet.GetSpokeCorners(spokeIndex).corner2] + math.normalize(center) * elevation - center;
        }
        
        private float3 GetSecondCorner(Planet planet, float3 center, int spokeIndex)
        {
            var elevation = GetTileElevation(planet, planet.Spokes[spokeIndex]);

            return planet.TileCorners[planet.GetSpokeCorners(spokeIndex).corner1] + math.normalize(center) * elevation - center;
        }

        private float3 GetBridge(Planet planet, int spokeIndex)
        {
            var oppositeSpoke = planet.TileSpokeOpposites[spokeIndex];
            var center = GetTileCenter(planet, planet.Spokes[spokeIndex]);
            var otherCenter = GetTileCenter(planet, planet.Spokes[oppositeSpoke]);
            var c1 = center + GetFirstSolidCorner(planet, center, spokeIndex);
            var c2 = otherCenter + GetSecondSolidCorner(planet, otherCenter, oppositeSpoke);
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
