using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct AssignBaseIcosahedronVerticesJob : IJobParallelFor
	{
		[ReadOnly] public float Radius;
		[ReadOnly] public NativeArray<float3> Vertices;
		
		[WriteOnly] public NativeArray<float3> TilePositions;
		
		public void Execute(int vertexIndex)
		{
			TilePositions[vertexIndex] = Vertices[vertexIndex] * Radius;
		}
	}
}