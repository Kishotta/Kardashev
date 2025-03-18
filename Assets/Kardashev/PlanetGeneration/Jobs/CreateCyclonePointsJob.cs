using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct CreateCyclonePointsJob : IJobParallelFor
	{
		[NativeDisableContainerSafetyRestriction]
		public NativeArray<Random> RandomNumberGenerators;

		[ReadOnly] public float PlanetRadius;
		[ReadOnly] public int CyclonePointCount;
		[ReadOnly] public float MinWindSpeed;
		[ReadOnly] public float MaxWindSpeed;
		
		[WriteOnly]
		public NativeArray<CyclonePoint> CyclonePoints;

		private static readonly float GoldenRatio = math.PI * (3f - math.sqrt(5f));
		
		public void Execute(int cyclonePointIndex)
		{
			var z      = 1 - (cyclonePointIndex + 0.5f) * (2f / CyclonePointCount);
			var radial = math.sqrt(1 - z * z);
			var theta  = GoldenRatio * cyclonePointIndex;
			var x      = math.cos(theta) * radial;
			var y      = math.sin(theta) * radial;
			
			var position = new float3(x, y, z) * PlanetRadius;

			var rnd                  = RandomNumberGenerators[cyclonePointIndex];
			var minCycloneRadius     = math.sqrt(4f * math.PI / CyclonePointCount) * PlanetRadius * 0.7f;
			var maxCycloneRadius     = minCycloneRadius * 1.5f;
			var cycloneRadius        = rnd.NextFloat(minCycloneRadius, maxCycloneRadius);
			var cycloneDirection = rnd.NextFloat() > 0.5f ? 1 : -1;
			var cycloneRotationSpeed = rnd.NextFloat(MinWindSpeed * cycloneDirection, MaxWindSpeed * cycloneDirection);
			
			var cyclonePoint = new CyclonePoint
			{
				Position      = position,
				Radius        = cycloneRadius,
				RotationSpeed = cycloneRotationSpeed
			};
			CyclonePoints[cyclonePointIndex] = cyclonePoint;
		}
	}
}