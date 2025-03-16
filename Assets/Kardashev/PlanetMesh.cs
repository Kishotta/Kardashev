using System.Collections.Generic;
using Kardashev.PlanetGeneration;
using Shapes;
using Unity.Mathematics;
using UnityEngine;

namespace Kardashev
{
    public class PlanetMesh : ImmediateModeShapeDrawer
    {
        private Planet _map;
        
        public PlanetMeshChunk chunkPrefab;

        public int chunkResolution = 2;
        
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
            }
        }
        
        public void Render(Planet planet)
        {
            _map?.Dispose();
            _map  = planet;
            
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }
            
            Triangulate(_map);
        }
        
        private void Triangulate(Planet planet)
        {
            var chunkCount = 6 * chunkResolution * chunkResolution;
            var chunks = new List<PlanetMeshChunk>();
            
            for (var i = 0; i < chunkCount; i++)
            {
                chunks.Add(Instantiate(chunkPrefab, transform));
            }
            
            for (var i = 0; i < planet.TilePositions.Length; i++)
            {
                var tile = planet.TilePositions[i];
                var chunkId = GetCubemapTileChunkId(tile);
                chunks[chunkId].Triangulate(planet, i);
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
