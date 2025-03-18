using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration
{
    public class Planet : IDisposable
    {
        /// <summary>
        /// The random seed used to generate the planet.
        /// </summary>
        public uint Seed;

        /// <summary>
        /// The size of the planet.
        /// </summary>
        public PlanetSize Size;
        
        /// <summary>
        /// Each entry represents a tile spoke by storing the index of the tile (seed) from which the spoke originates.
        /// </summary>
        public NativeArray<int> Spokes;

        /// <summary>
        /// For each tile spoke, stores the index of the opposite spoke (linking to the adjacent tile).
        /// </summary>
        public NativeArray<int> TileSpokeOpposites;

        /// <summary>
        /// For each tile, stores the index of one outgoing tile spoke.
        /// </summary>
        public NativeArray<int> TileSpokes;

        /// <summary>
        /// Coordinates of tiles (seed points).
        /// </summary>
        public NativeArray<float3> TilePositions;

        /// <summary>
        /// Coordinates of tile corners (computed from the centers of triangles in the Delaunay triangulation).
        /// </summary>
        public NativeArray<float3> TileCorners;

        /// <summary>
        /// For each tile, stores the elevation of the tile.
        /// </summary>
        public NativeArray<float> TileElevations;

        /// <summary>
        /// For each tile, stores the temperature of the tile.
        /// </summary>
        public NativeArray<float> TileTemperatures;

        public Planet(uint seed, PlanetSize size, Allocator allocator)
        {
            var spokeCount = PlanetHelpers.SpokeCount((int)size);
            var tileCount = PlanetHelpers.TileCount((int)size);
            var cornerCount = PlanetHelpers.CornerCount((int)size);
            
            Seed               = seed;
            Size               = size;
            
            Spokes             = new NativeArray<int>(spokeCount, allocator);
            Initialize(Spokes, spokeCount, -1);
            
            TileSpokeOpposites = new NativeArray<int>(spokeCount, allocator);
            Initialize(TileSpokeOpposites, spokeCount, -1);
            
            TileSpokes         = new NativeArray<int>(tileCount, allocator);
            Initialize(TileSpokes, tileCount, -1);
            
            TilePositions      = new NativeArray<float3>(tileCount, allocator);
            Initialize(TilePositions, tileCount, float3.zero);
            
            TileCorners        = new NativeArray<float3>(cornerCount, allocator);
            Initialize(TileCorners, cornerCount, float3.zero);
            
            TileElevations     = new NativeArray<float>(tileCount, allocator);
            Initialize(TileElevations, tileCount, 0);
            
            TileTemperatures   = new NativeArray<float>(tileCount, allocator);
            Initialize(TileTemperatures, tileCount, 0);
        }
        
        private static void Initialize<T>(NativeArray<T> array, int count, T value) where T : unmanaged
        {
            for (var i = 0; i < count; i++)
                array[i] = value;
        }
    
        public void Dispose()
        {
            if (Spokes.IsCreated) Spokes.Dispose();
            if (TileSpokeOpposites.IsCreated) TileSpokeOpposites.Dispose();
            if (TileSpokes.IsCreated) TileSpokes.Dispose();
            if (TilePositions.IsCreated) TilePositions.Dispose();
            if (TileCorners.IsCreated) TileCorners.Dispose();
            if (TileElevations.IsCreated) TileElevations.Dispose();
            if (TileTemperatures.IsCreated) TileTemperatures.Dispose();
        }

        /// <summary>
        /// Returns the index of the previous edge in a triangle.
        /// </summary>
        public static int GetPreviousEdgeIndex(int edgeIndex)
        {
            return edgeIndex % 3 == 0 
                ? edgeIndex + 2 
                : edgeIndex - 1;
        }

        /// <summary>
        /// Returns the index of the next edge in a triangle.
        /// </summary>
        public static int GetNextEdgeIndex(int edgeIndex)
        {
            return edgeIndex % 3 == 2 
                ? edgeIndex - 2 
                : edgeIndex + 1;
        }

        /// <summary>
        /// Returns all tile edges surrounding a given tile center (seed).
        /// </summary>
        public NativeList<int> GetTileSpokeIndices(int tileIndex)
        {
            var startEdge    = TileSpokes[tileIndex];
            var currentEdge  = startEdge;
            var oppositeEdge = -1;
        
            var loop = new NativeList<int>(Allocator.Temp);
            do
            {
                loop.Add(currentEdge);
                oppositeEdge = TileSpokeOpposites[currentEdge];
                currentEdge  = GetNextEdgeIndex(oppositeEdge);
            } while(oppositeEdge >= 0 && currentEdge != startEdge);
        
            return loop;
        }
    
        /// <summary>
        /// Returns the corner index corresponding to a given tile edge.
        /// </summary>
        public static int GetCornerIndex(int edgeIndex)
        {
            return edgeIndex / 3;
        }
    
        /// <summary>
        /// Returns the indices of the tile centers (seeds) that form the given corner.
        /// </summary>
        public NativeArray<int> GetCornerTileIndices(int cornerIndex)
        {
            var tileIndices = new NativeArray<int>(3, Allocator.Temp);
            tileIndices[0] = Spokes[3 * cornerIndex];
            tileIndices[1] = Spokes[3 * cornerIndex + 1];
            tileIndices[2] = Spokes[3 * cornerIndex + 2];
            return tileIndices;
        }

    
        /// <summary>
        /// Returns the neighboring tile centers (seeds) adjacent to the given tile center.
        /// </summary>
        public NativeArray<int> GetTileNeighborIndices(int tileIndex)
        {
            var edgeLoop  = GetTileSpokeIndices(tileIndex);
            var neighbors = new NativeArray<int>(edgeLoop.Length, Allocator.Temp);
            for (var i = 0; i < edgeLoop.Length; i++)
            {
                var nextEdge = GetNextEdgeIndex(edgeLoop[i]);
                neighbors[i] = Spokes[nextEdge];
            }
            edgeLoop.Dispose();
            return neighbors;
        }
    
        /// <summary>
        /// Returns the indices of the corners (tile vertices) that touch the specified tile center.
        /// </summary>
        public NativeArray<int> GetTileCornerIndices(int tileIndex)
        {
            var edgeLoop = GetTileSpokeIndices(tileIndex);
            var corners  = new NativeArray<int>(edgeLoop.Length, Allocator.Temp);
            for (var i = 0; i < edgeLoop.Length; i++)
            {
                corners[i] = GetCornerIndex(edgeLoop[i]);
            }
            edgeLoop.Dispose();
            return corners;
        }

        /// <summary>
        /// Returns the two corners adjacent to the specified tile edge.
        /// </summary>
        public (int corner1, int corner2) GetSpokeCorners(int edgeIndex)
        {
            var corner1 = GetCornerIndex(edgeIndex);
            var corner2 = GetCornerIndex(TileSpokeOpposites[edgeIndex]);
            return (corner1, corner2);
        }
    }
}
