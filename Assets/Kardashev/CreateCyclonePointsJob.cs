using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev
{
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

		private static float _goldenRatio = math.PI * (3f - math.sqrt(5f));
		
		public void Execute(int cyclonePointIndex)
		{
			var z      = 1 - (cyclonePointIndex + 0.5f) * (2f / CyclonePointCount);
			var radial = math.sqrt(1 - z * z);
			var theta  = _goldenRatio * cyclonePointIndex;
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

	public struct CalculatePrevalentWindsJob : IJobParallelFor
	{
		[ReadOnly] public NativeArray<float3> TilePositions;
		[ReadOnly] public NativeArray<CyclonePoint> CyclonePoints;
		[ReadOnly] public float PlanetRadius;
		
		[WriteOnly] public NativeArray<float3> PrevalentWinds;
		
		public void Execute(int tileIndex)
		{
			var prevalentWind = float3.zero;
			
			var tilePosition = TilePositions[tileIndex];
			var n1           = math.normalize(tilePosition);
			var totalWeight  = 0f;
			for (var cycloneIndex = 0; cycloneIndex < CyclonePoints.Length; cycloneIndex++)
			{
				var cyclone  = CyclonePoints[cycloneIndex];
				var n2       = math.normalize(cyclone.Position);
				var distance = math.acos(math.clamp(math.dot(n1, n2), -1f, 1f)) * PlanetRadius;
				if (distance > cyclone.Radius) continue;
				
				var weight   = 1f - distance / cyclone.Radius;
				var radial   = math.normalize(tilePosition - cyclone.Position);
				var up       = math.normalize(cyclone.Position);
				var tangent  = math.normalize(math.cross(up, radial));
				var binormal = math.cross(radial, tangent);

				var angle     = cyclone.RotationSpeed;
				var cos       = math.cos(angle);
				var sin       = math.sin(angle);
				var localWind = tangent * cos + binormal * sin;

				var tileNormal = math.normalize(tilePosition);
				var projectedLocalWind = localWind - tileNormal * math.dot(localWind, tileNormal);
				prevalentWind += projectedLocalWind * weight;
				totalWeight += weight;
			}
			
			if (totalWeight > 0) 
				PrevalentWinds[tileIndex] = prevalentWind / totalWeight;
			else 
				PrevalentWinds[tileIndex] = float3.zero;
		}
	}
}