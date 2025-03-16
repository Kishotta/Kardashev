using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct ConnectOppositeSpokesJob : IJobParallelFor
	{
		[ReadOnly] public NativeArray<int> Spokes;
		[ReadOnly] public NativeParallelHashMap<(int, int), int> EdgeLookupTable;
		
		[WriteOnly] public NativeArray<int> TileSpokeOpposites;
		
		public void Execute(int spokeIndex)
		{
			var from = Spokes[spokeIndex];
			var to   = Spokes[PlanetHelpers.NextEdgeIndex(spokeIndex)];
			
			var foundOppositeSpokeIndex = EdgeLookupTable.TryGetValue((to, from), out var oppositeSpokeIndex);
			if (foundOppositeSpokeIndex)
			{
				TileSpokeOpposites[spokeIndex] = oppositeSpokeIndex;
			}
		}
	}
}