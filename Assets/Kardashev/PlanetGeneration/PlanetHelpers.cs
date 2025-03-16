using Unity.Collections;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration
{
	public static class PlanetHelpers
	{
		public static int CornerCount(int size)
		{
			var frequency = (int)math.pow(2, size);
			return 20 * frequency * frequency;
		}
		
		public static int SpokeCount(int size)
		{
			var frequency = (int)math.pow(2, size);
			return 30 * frequency * frequency * 2;
		}

		public static int TileCount(int size)
		{
			var frequency = (int)math.pow(2, size);
			return 10 * frequency * frequency + 2;
		}

		public static float Radius(int size) => 
			math.sqrt(CornerCount(size) / 4f * math.PI);

		public static int FirstFreeIndex(NativeArray<int> array)
		{
			for (var i = 0; i < array.Length; i++)
			{
				if (array[i] == -1)
					return i;
			}

			return array.Length;
		}
		
		public static int FirstFreeIndex(NativeArray<float3> array)
		{
			for (var i = 0; i < array.Length; i++)
			{
				if (math.lengthsq(array[i]) == 0f)
					return i;
			}

			return array.Length;
		}
		
		public static int FirstFreeIndex(NativeList<int> list)
		{
			for (var i = 0; i < list.Length; i++)
			{
				if (list[i] == -1)
					return i;
			}

			return list.Length;
		}
		
		public static int PreviousEdgeIndex(int edgeIndex) =>
			edgeIndex % 3 == 0 
				? edgeIndex + 2 
				: edgeIndex - 1;
		
		public static int NextEdgeIndex(int edgeIndex) =>
			edgeIndex % 3 == 2 
				? edgeIndex - 2 
				: edgeIndex + 1;
		
		public static NativeList<int> TileSpokeIndices(
			int tileIndex,
			NativeArray<int> tileSpokes,
			NativeArray<int> tileSpokeOpposites)
		{
			var startEdge    = tileSpokes[tileIndex];
			var currentEdge  = startEdge;
			int oppositeEdge;
        
			var loop = new NativeList<int>(Allocator.Temp);
			do
			{
				loop.Add(currentEdge);
				oppositeEdge = tileSpokeOpposites[currentEdge];
				currentEdge  = NextEdgeIndex(oppositeEdge);
			} while(oppositeEdge >= 0 && currentEdge != startEdge);
        
			return loop;
		}
		
		public static int CornerIndex(int edgeIndex) => 
			edgeIndex / 3;

		public static NativeArray<int> CornerEdgeIndices(int cornerIndex)
		{
			var edges = new NativeArray<int>(3, Allocator.Temp);
			edges[0] = 3 * cornerIndex;
			edges[1] = 3 * cornerIndex + 1;
			edges[2] = 3 * cornerIndex + 2;
			return edges;
		}
		
		public static NativeArray<int> CornerNeighborIndices(
			int cornerIndex,
			NativeArray<int> tileSpokeOpposites)
		{
			var neighbors = new NativeArray<int>(3, Allocator.Temp);
			neighbors[0] = CornerIndex(tileSpokeOpposites[3 * cornerIndex]);
			neighbors[1] = CornerIndex(tileSpokeOpposites[3 * cornerIndex + 1]);
			neighbors[2] = CornerIndex(tileSpokeOpposites[3 * cornerIndex + 2]);
			return neighbors;
		}
		
		public static NativeArray<int> TileNeighborIndices(
			int tileIndex,
			NativeArray<int> spokes,
			NativeArray<int> tileSpokes,
			NativeArray<int> tileSpokeOpposites)
		{
			var edgeLoop  = TileSpokeIndices(tileIndex, tileSpokes, tileSpokeOpposites);
			var neighbors = new NativeArray<int>(edgeLoop.Length, Allocator.Temp);
			for (var i = 0; i < edgeLoop.Length; i++)
			{
				var nextEdge = NextEdgeIndex(edgeLoop[i]);
				neighbors[i] = spokes[nextEdge];
			}
			edgeLoop.Dispose();
			return neighbors;
		}
		
		public static NativeArray<int> TileCornerIndices(
			int tileIndex,
			NativeArray<int> spokes,
			NativeArray<int> tileSpokes,
			NativeArray<int> tileSpokeOpposites)
		{
			var edgeLoop = TileSpokeIndices(tileIndex, tileSpokes, tileSpokeOpposites);
			var corners  = new NativeArray<int>(edgeLoop.Length, Allocator.Temp);
			for (var i = 0; i < edgeLoop.Length; i++)
			{
				corners[i] = CornerIndex(edgeLoop[i]);
			}
			edgeLoop.Dispose();
			return corners;
		}
		
		public static (int corner1, int corner2) SpokeCorners(
			int edgeIndex,
			NativeArray<int> tileSpokeOpposites)
		{
			var corner1 = CornerIndex(edgeIndex);
			var corner2 = CornerIndex(tileSpokeOpposites[edgeIndex]);
			return (corner1, corner2);
		}
		
		public static int AddTileCenter(
			int tileIndex, 
			float3 position,
			NativeArray<float3> tilePositions)
		{
			tilePositions[tileIndex] = position;

			return tileIndex;
		}
		
		public static int AddTileCenter(
			float3 position,
			NativeArray<float3> tilePositions)
		{
			var tileIndex = FirstFreeIndex(tilePositions);
			tilePositions[tileIndex]  = position;

			return tileIndex;
		}
		
		public static void AddTileCornerWithoutOpposites(
			int cornerIndex,
			int tileAIndex, 
			int tileBIndex, 
			int tileCIndex, 
			NativeParallelHashMap<(int, int), int>.ParallelWriter edgeLookupTable,
			NativeArray<int> spokes,
			NativeArray<int> tileSpokes,
			NativeArray<float3> tilePositions,
			NativeArray<float3> tileCorners)
		{
			var baseEdgeIndex = cornerIndex * 3;
        
			// Add new tile spokes.
			spokes[baseEdgeIndex]     = tileAIndex;
			spokes[baseEdgeIndex + 1] = tileBIndex;
			spokes[baseEdgeIndex + 2] = tileCIndex;
        
			// Calculate and store corner position.
			var cornerPosition = (tilePositions[tileAIndex] + tilePositions[tileBIndex] + tilePositions[tileCIndex]) / 3;
			// tileCorners.Add(cornerPosition);
			tileCorners[cornerIndex] = cornerPosition;

			// Add spokes to tile if tile doesn't already have one.
			if (tileSpokes[tileAIndex] == -1) tileSpokes[tileAIndex] = baseEdgeIndex;
			if (tileSpokes[tileBIndex] == -1) tileSpokes[tileBIndex] = baseEdgeIndex + 1;
			if (tileSpokes[tileCIndex] == -1) tileSpokes[tileCIndex] = baseEdgeIndex + 2;
			
			CacheOppositeSpoke(baseEdgeIndex, tileAIndex, tileBIndex, edgeLookupTable);
			CacheOppositeSpoke(baseEdgeIndex + 1, tileBIndex, tileCIndex, edgeLookupTable);
			CacheOppositeSpoke(baseEdgeIndex + 2, tileCIndex, tileAIndex, edgeLookupTable);
		}
		
		private static void CacheOppositeSpoke(
			int newEdgeIndex,
			int fromTile,
			int toTile,
			NativeParallelHashMap<(int, int), int>.ParallelWriter edgeLookupTable)
		{
			edgeLookupTable.TryAdd((fromTile, toTile), newEdgeIndex);
		}
		
		public static void AddTileCorner(
			int cornerIndex,
			int tileAIndex, 
			int tileBIndex, 
			int tileCIndex, 
			NativeHashMap<(int, int), int> edgeLookupTable,
			NativeArray<int> spokes,
			NativeArray<int> tileSpokes,
			NativeArray<int> tileSpokeOpposites,
			NativeArray<float3> tilePositions,
			NativeArray<float3> tileCorners)
		{
			// var baseEdgeIndex = FirstFreeIndex(spokes);
			var baseEdgeIndex = cornerIndex * 3;
        
			// Add new tile spokes.
			// spokes.Add(tileAIndex);
			// spokes.Add(tileBIndex);
			// spokes.Add(tileCIndex);
			spokes[baseEdgeIndex] = tileAIndex;
			spokes[baseEdgeIndex + 1] = tileBIndex;
			spokes[baseEdgeIndex + 2] = tileCIndex;
        
			// Initialize new spoke opposites.
			// tileSpokeOpposites.Add(-1);
			// tileSpokeOpposites.Add(-1);
			// tileSpokeOpposites.Add(-1);
			// tileSpokeOpposites[baseEdgeIndex] = -1;
			// tileSpokeOpposites[baseEdgeIndex + 1] = -1;
			// tileSpokeOpposites[baseEdgeIndex + 2] = -1;
        
			// Calculate and store corner position.
			var cornerPosition = (tilePositions[tileAIndex] + tilePositions[tileBIndex] + tilePositions[tileCIndex]) / 3;
			// tileCorners.Add(cornerPosition);
			tileCorners[cornerIndex] = cornerPosition;

			// Add spokes to tile if tile doesn't already have one.
			if (tileSpokes[tileAIndex] == -1) tileSpokes[tileAIndex] = baseEdgeIndex;
			if (tileSpokes[tileBIndex] == -1) tileSpokes[tileBIndex] = baseEdgeIndex + 1;
			if (tileSpokes[tileCIndex] == -1) tileSpokes[tileCIndex] = baseEdgeIndex + 2;

			// Link opposite spokes.
			TryLinkOppositeSpoke(baseEdgeIndex, tileAIndex, tileBIndex, edgeLookupTable, tileSpokeOpposites);
			TryLinkOppositeSpoke(baseEdgeIndex + 1, tileBIndex, tileCIndex, edgeLookupTable, tileSpokeOpposites);
			TryLinkOppositeSpoke(baseEdgeIndex + 2, tileCIndex, tileAIndex, edgeLookupTable, tileSpokeOpposites);
		}

		private static void TryLinkOppositeSpoke(
			int newEdgeIndex,
			int fromTile, 
			int toTile,
			NativeHashMap<(int, int), int> edgeLookupTable,
			NativeArray<int> tileSpokeOpposites)
		{
			// Look up the reverse edge key.
			if (edgeLookupTable.TryGetValue((toTile, fromTile), out var oppositeSpokeIndex))
			{
				tileSpokeOpposites[oppositeSpokeIndex] = newEdgeIndex;
				tileSpokeOpposites[newEdgeIndex]       = oppositeSpokeIndex;
			}
			// Register this edge so future edges can find it.
			edgeLookupTable[(fromTile, toTile)] = newEdgeIndex;
		}
	}
}