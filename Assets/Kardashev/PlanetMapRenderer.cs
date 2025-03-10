using System.Collections.Generic;
using Shapes;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Kardashev
{
    public class PlanetMapRenderer : ImmediateModeShapeDrawer
    {
        private PlanetMap _map;
        
        public PlanetMapChunk chunkPrefab;
        
        [Header("Tile Settings")]
        [Range(0.6f, 0.9f)]
        public float solidFactor = 0.75f;

        public int chunkResolution = 2;
        
        public Gradient pressureGradient;
        
        private void OnValidate()
        {
            if (_map == null) return;
            Render(_map);
        }

        private void OnDestroy()
        {
            _map?.Dispose();
        }
        
        public override void DrawShapes( Camera cam ){
            if (_map == null) return;
            
            using( Draw.Command( cam ) )
            {
                // for(var tileIndex = 0; tileIndex < _map.TileVelocities.Length; tileIndex++)
                // {
                //     Draw.Cone(_map.TilePositions[tileIndex], _map.TileVelocities[tileIndex], 0.1f, math.length(_map.TileVelocities[tileIndex]));
                // }
                
                // for (var spokeIndex = 0; spokeIndex < _map.Spokes.Length; spokeIndex++)
                // {
                //     var oppositeSpokeIndex = _map.TileSpokeOpposites[spokeIndex];
                //     if (oppositeSpokeIndex > spokeIndex) continue;
                //
                //     if (_map.TilePlates[_map.Spokes[spokeIndex]] == _map.TilePlates[_map.Spokes[oppositeSpokeIndex]]) continue;
                //
                //     var (c1, c2) = _map.GetSpokeCorners(spokeIndex);
                //     var pressure         = _map.SpokePressures[spokeIndex];
                //     var relativePressure = math.remap(-3f, 3f, 0f, 1f, pressure);
                //     Draw.Line(_map.TileCorners[c1], _map.TileCorners[c2], 0.1f, pressureGradient.Evaluate(relativePressure));
                // }
            }
        }
        
        public void Render(PlanetMap planetMap)
        {
            _map  = planetMap;
            
            Triangulate(_map);
        }
        
        private void Triangulate(PlanetMap planetMap)
        {
            var chunkCount = 6 * chunkResolution * chunkResolution;
            var chunks = new List<PlanetMapChunk>();
            
            for (var i = 0; i < chunkCount; i++)
            {
                chunks.Add(Instantiate(chunkPrefab, transform));
            }
            
            for (var i = 0; i < planetMap.TilePositions.Length; i++)
            {
                var tile = planetMap.TilePositions[i];
                var chunkId = GetCubemapTileChunkId(tile);
                chunks[chunkId].Triangulate(planetMap, i);
            }
            
            for (var i = 0; i < chunkCount; i++)
            {
                chunks[i].SaveMesh();
            }
        }
        
        public int GetCubemapTileChunkId(float3 tilePosition)
        {
            var   absoluteTilePosition = math.abs(tilePosition);
            int   face;
            float u, v;

            if (absoluteTilePosition.x >= absoluteTilePosition.y && absoluteTilePosition.x >= absoluteTilePosition.z)
            {
                // X face
                face = tilePosition.x > 0 ? 0 : 1;
                u = tilePosition.z / absoluteTilePosition.x;
                v = tilePosition.y / absoluteTilePosition.x;
            }
            else if (absoluteTilePosition.y >= absoluteTilePosition.x && absoluteTilePosition.y >= absoluteTilePosition.z)
            {
                // Y face
                face = tilePosition.y > 0 ? 2 : 3;
                u = tilePosition.x / absoluteTilePosition.y;
                v = tilePosition.z / absoluteTilePosition.y;
            }
            else
            {
                // Z face
                face = tilePosition.z > 0 ? 4 : 5;
                u = tilePosition.x / absoluteTilePosition.z;
                v = tilePosition.y / absoluteTilePosition.z;
            }

            u = (u + 1f) * 0.5f;
            v = (v + 1f) * 0.5f;
            
            var xIndex = (int)math.floor(u * chunkResolution);
            var yIndex = (int)math.floor(v * chunkResolution);
            
            xIndex = math.clamp(xIndex, 0, chunkResolution - 1);
            yIndex = math.clamp(yIndex, 0, chunkResolution - 1);
            
            var chunkId = face * chunkResolution * chunkResolution + yIndex * chunkResolution + xIndex;
            return chunkId;
        }
    }
}
