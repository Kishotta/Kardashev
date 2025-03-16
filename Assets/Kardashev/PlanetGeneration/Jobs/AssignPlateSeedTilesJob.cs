using Unity.Collections;
using Unity.Jobs;

namespace Kardashev.PlanetGeneration.Jobs
{
	// [BurstCompile]
	struct AssignPlateSeedTilesJob : IJobParallelFor
	{
		[ReadOnly] 
		public NativeArray<Plate> Plates;
			
		[WriteOnly]
		public NativeArray<int> TilePlates;

		[WriteOnly] 
		public NativeArray<float> TileElevations;
		
			
		public void Execute(int tileIndex)
		{
			for (var plateIndex = 0; plateIndex < Plates.Length; plateIndex++)
			{
				if (Plates[plateIndex].seedTileIndex != tileIndex) continue;
					
				TilePlates[tileIndex] = plateIndex;
				TileElevations[tileIndex] = Plates[plateIndex].desiredElevation;
			}
		}
	}
}