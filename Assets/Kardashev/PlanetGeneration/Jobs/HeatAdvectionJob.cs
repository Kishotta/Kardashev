using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	public struct HeatAdvectionJob : IJobParallelFor
	{
		[ReadOnly] public NativeArray<int> Spokes;
		[ReadOnly] public NativeArray<int> TileSpokes;
		[ReadOnly] public NativeArray<int> TileSpokeOpposites;
		[ReadOnly] public NativeArray<float3> TilePositions;
		[ReadOnly] public NativeArray<float> TileElevations;
		[ReadOnly] public NativeArray<float3> TileWinds;
		[ReadOnly] public NativeArray<float> BaseTemperatures;
		[ReadOnly] public NativeArray<float> TileTemperatures;

		[WriteOnly] public NativeArray<float> NewTemperatures;

		public float AdvectionFactor;
		public float ForcingFactor;
		
		public void Execute(int tileIndex)
		{
			var currentTemperature = TileTemperatures[tileIndex];

			var weightedSum = currentTemperature;
			var totalWeight = 1f;

			var neighborTiles = PlanetHelpers.TileNeighborIndices(
				tileIndex,
				Spokes,
				TileSpokes,
				TileSpokeOpposites);
			for (var i = 0; i < neighborTiles.Length; ++i)
			{
				var neighborTileIndex   = neighborTiles[i];
				var neighborPosition    = TilePositions[neighborTileIndex];
				var neighborTemperature = TileTemperatures[neighborTileIndex];
				
				var neighborDirection = math.normalize(neighborPosition - TilePositions[tileIndex]);
				
				var windAlignment = (math.dot(neighborDirection, TileWinds[tileIndex]) + 1f) * 0.5f;
				
				var elevationDiff   = math.abs(TileElevations[tileIndex] - TileElevations[neighborTileIndex]);
				var elevationFactor = 1f - math.saturate(elevationDiff / 3f);
				
				var oceanFactor = TileElevations[neighborTileIndex] < 0 ? 1.2f : 1f;
				
				var weight = 1f * windAlignment * elevationFactor * oceanFactor;
				weightedSum += neighborTemperature * weight;
				totalWeight += weight;
			}
			
			var advectedTemperature = weightedSum / totalWeight;
			var baseTemperature = BaseTemperatures[tileIndex];
			var forcedTemperature = math.lerp(advectedTemperature, baseTemperature, ForcingFactor);
			NewTemperatures[tileIndex] = math.lerp(currentTemperature, forcedTemperature, AdvectionFactor);
		}
	}
}