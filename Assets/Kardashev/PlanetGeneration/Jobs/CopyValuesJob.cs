using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct CopyValuesJob<T> : IJobParallelFor where T : unmanaged
	{
		[ReadOnly] public NativeArray<T> Source;
		
		[WriteOnly] public NativeArray<T> Destination;

		public void Execute(int index)
		{
			Destination[index] = Source[index];
		}
	}
}