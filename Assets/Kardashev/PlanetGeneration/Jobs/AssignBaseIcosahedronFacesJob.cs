using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct AssignBaseIcosahedronFacesJob : IJobParallelFor
	{
		[ReadOnly] public NativeArray<int3> Faces;
		[ReadOnly] public NativeArray<float3> TilePositions;

		[WriteOnly] public NativeArray<float3> TileCorners;
		[WriteOnly] public NativeParallelHashMap<int2, int>.ParallelWriter EdgeLookupTable;
		
		[NativeDisableParallelForRestriction]
		[WriteOnly] public NativeArray<int> Spokes;
		
		[NativeDisableParallelForRestriction] 
		public NativeArray<int> TileSpokes;
		
		public void Execute(int cornerIndex)
		{
			var baseEdgeIndex = cornerIndex * 3;

			var vertices   = Faces[cornerIndex];
			var tileAIndex = vertices[0];
			var tileBIndex = vertices[1];
			var tileCIndex = vertices[2];
        
			// Add new tile spokes.
			Spokes[baseEdgeIndex]     = tileAIndex;
			Spokes[baseEdgeIndex + 1] = tileBIndex;
			Spokes[baseEdgeIndex + 2] = tileCIndex;
        
			// Calculate and store corner position.
			var cornerPosition = (TilePositions[tileAIndex] + TilePositions[tileBIndex] + TilePositions[tileCIndex]) / 3;
			TileCorners[cornerIndex] = cornerPosition;

			// Add spokes to tile if tile doesn't already have one.
			if (TileSpokes[tileAIndex] == -1) TileSpokes[tileAIndex] = baseEdgeIndex;
			if (TileSpokes[tileBIndex] == -1) TileSpokes[tileBIndex] = baseEdgeIndex + 1;
			if (TileSpokes[tileCIndex] == -1) TileSpokes[tileCIndex] = baseEdgeIndex + 2;
			
			EdgeLookupTable.TryAdd(new int2(tileAIndex, tileBIndex), baseEdgeIndex);
			EdgeLookupTable.TryAdd(new int2(tileBIndex, tileCIndex), baseEdgeIndex + 1);
			EdgeLookupTable.TryAdd(new int2(tileCIndex, tileAIndex), baseEdgeIndex + 2);
		}
	}
}