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
        /// Returns the indices of the three edges forming a corner (triangle).
        /// </summary>
        public static NativeArray<int> GetCornerEdgeIndices(int cornerIndex)
        {
            var edges = new NativeArray<int>(3, Allocator.Temp);
            edges[0] = 3 * cornerIndex;
            edges[1] = 3 * cornerIndex + 1;
            edges[2] = 3 * cornerIndex + 2;
            return edges;
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
        /// Returns the indices of the corners adjacent to the given corner.
        /// </summary>
        public NativeArray<int> GetCornerNeighborIndices(int cornerIndex)
        {
            var neighbors = new NativeArray<int>(3, Allocator.Temp);
            neighbors[0] = GetCornerIndex(TileSpokeOpposites[3 * cornerIndex]);
            neighbors[1] = GetCornerIndex(TileSpokeOpposites[3 * cornerIndex + 1]);
            neighbors[2] = GetCornerIndex(TileSpokeOpposites[3 * cornerIndex + 2]);
            return neighbors;
        }

    
        /// <summary>
        /// Returns all incoming tile edges for the specified tile center.
        /// </summary>
        public NativeArray<int> GetIncomingTileSpokeIndices(int tileIndex)
        {
            var edgeLoop      = GetTileSpokeIndices(tileIndex);
            var incomingEdges = new NativeArray<int>(edgeLoop.Length, Allocator.Temp);
            for (var i = 0; i < edgeLoop.Length; i++)
            {
                incomingEdges[i] = GetPreviousEdgeIndex(edgeLoop[i]);
            }
            edgeLoop.Dispose();
            return incomingEdges;
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
        /// Returns the two tile centers (seeds) connected by the specified tile edge.
        /// </summary>
        public (int fromTile, int toTile) GetSpokeTiles(int edgeIndex)
        {
            var fromTile = Spokes[edgeIndex];
            var toTile   = Spokes[GetNextEdgeIndex(edgeIndex)];
            return (fromTile, toTile);
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
    
        /// <summary>
        /// Adds a new tile center (seed) to the planet map.
        /// </summary>
        public int AddTileCenter(float3 position)
        {
            // TilePositions.Add(position);
            // TileSpokes.Add(-1);
            // TileElevations.Add(0);
            return TilePositions.Length - 1;
        }
    
        /// <summary>
        /// Adds a new corner (computed from three tile centers) to the planet map.
        /// </summary>
        public int AddTileCorner(int tileAIndex, int tileBIndex, int tileCIndex, NativeHashMap<(int, int), int> edgeLookupTable)
        {
            var cornerIndex   = TileCorners.Length;
            var baseEdgeIndex = Spokes.Length;
        
            // Add new tile spokes.
            // Spokes.Add(tileAIndex);
            // Spokes.Add(tileBIndex);
            // Spokes.Add(tileCIndex);
        
            // Initialize new spoke opposites.
            // TileSpokeOpposites.Add(-1);
            // TileSpokeOpposites.Add(-1);
            // TileSpokeOpposites.Add(-1);
        
            // Calculate and store corner position.
            var cornerPosition = (TilePositions[tileAIndex] + TilePositions[tileBIndex] + TilePositions[tileCIndex]) / 3;
            // TileCorners.Add(cornerPosition);

            // Add spokes to tile if tile doesn't already have one.
            if (TileSpokes[tileAIndex] == -1) TileSpokes[tileAIndex] = baseEdgeIndex;
            if (TileSpokes[tileBIndex] == -1) TileSpokes[tileBIndex] = baseEdgeIndex + 1;
            if (TileSpokes[tileCIndex] == -1) TileSpokes[tileCIndex] = baseEdgeIndex + 2;

            // Link opposite spokes.
            LinkOppositeSpoke(baseEdgeIndex, tileAIndex, tileBIndex, edgeLookupTable);
            LinkOppositeSpoke(baseEdgeIndex + 1, tileBIndex, tileCIndex, edgeLookupTable);
            LinkOppositeSpoke(baseEdgeIndex + 2, tileCIndex, tileAIndex, edgeLookupTable);
        
            return cornerIndex;
        }

        /// <summary>
        /// Searches for and links the opposite tile edge for a newly added edge.
        /// </summary>
        private void LinkOppositeSpoke(int newEdgeIndex, int fromTile, int toTile, NativeHashMap<(int, int), int> edgeLookupTable)
        {
            // Look up the reverse edge key.
            if (edgeLookupTable.TryGetValue((toTile, fromTile), out var oppositeSpokeIndex))
            {
                TileSpokeOpposites[oppositeSpokeIndex] = newEdgeIndex;
                TileSpokeOpposites[newEdgeIndex]       = oppositeSpokeIndex;
            }
            // Register this edge so future edges can find it.
            edgeLookupTable[(fromTile, toTile)] = newEdgeIndex;
        }
    }
}
