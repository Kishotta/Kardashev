using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
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

				var tileNormal         = math.normalize(tilePosition);
				var projectedLocalWind = localWind - tileNormal * math.dot(localWind, tileNormal);
				prevalentWind += projectedLocalWind * weight;
				totalWeight   += weight;
			}
			
			if (totalWeight > 0) 
				PrevalentWinds[tileIndex] = prevalentWind / totalWeight;
			else 
				PrevalentWinds[tileIndex] = float3.zero;
		}
	}
}