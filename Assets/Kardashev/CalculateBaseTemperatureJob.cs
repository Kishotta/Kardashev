using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev
{
	[BurstCompile]
	public struct CalculateBaseTemperatureJob : IJobParallelFor
	{
		[ReadOnly] public NativeArray<float3> TilePositions;
		[ReadOnly] public NativeArray<float> TileElevations;
		
		public NativeArray<float> BaseTemperatures;
		
		public void Execute(int tileIndex)
		{
			var normal = math.normalize(TilePositions[tileIndex]);
			var tileElevation = TileElevations[tileIndex];
			var lattitude = math.sin(math.abs(normal.y));

			var equatorialTemperature = 40f;
			var polarTemperature = -20f;
			
			var baseTemperature = math.lerp(equatorialTemperature, polarTemperature, lattitude);
			var isUnderwater = tileElevation < 0;
			var lapseRate       = isUnderwater ? -1.5f : -3f;
			var elevationAdjustment = TileElevations[tileIndex] * lapseRate;
			
			BaseTemperatures[tileIndex] = baseTemperature + elevationAdjustment;
		}
	}
}