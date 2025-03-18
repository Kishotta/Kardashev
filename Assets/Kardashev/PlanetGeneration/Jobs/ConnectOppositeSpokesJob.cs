using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct ConnectOppositeSpokesJob : IJobParallelFor
	{
		[ReadOnly] public NativeArray<int> Spokes;
		[ReadOnly] public NativeParallelHashMap<int2, int> EdgeLookupTable;
		
		[WriteOnly] public NativeArray<int> TileSpokeOpposites;
		
		public void Execute(int spokeIndex)
		{
			var from = Spokes[spokeIndex];
			var to   = Spokes[PlanetHelpers.NextEdgeIndex(spokeIndex)];
			
			var foundOppositeSpokeIndex = EdgeLookupTable.TryGetValue(new int2(to, from), out var oppositeSpokeIndex);
			if (foundOppositeSpokeIndex)
			{
				TileSpokeOpposites[spokeIndex] = oppositeSpokeIndex;
			}
		}
	}
}