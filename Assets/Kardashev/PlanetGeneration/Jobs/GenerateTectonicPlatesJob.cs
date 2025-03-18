using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	struct GenerateTectonicPlatesJob : IJobParallelFor
	{
		[NativeDisableContainerSafetyRestriction]
		public NativeArray<Random> RandomNumberGenerators;
			
		public int TileCount;
		public int PlateCount;
		public float OceanicPlateRatio;
		public float MinOceanicPlateDesiredElevation;
		public float MaxOceanicPlateDesiredElevation;
		public float MinContinentalPlateDesiredElevation;
		public float MaxContinentalPlateDesiredElevation;
		public float MinPlateRotationRate;
		public float MaxPlateRotationRate;
			
		[WriteOnly]
		public NativeArray<Plate> Plates;

		public void Execute(int plateIndex)
		{
			var rnd           = RandomNumberGenerators[plateIndex];
			var seedTileIndex = rnd.NextInt(TileCount);
			var plateType     = plateIndex / (float)PlateCount < OceanicPlateRatio ? PlateType.Oceanic : PlateType.Continental;
			var minDesiredElevation = plateType == PlateType.Oceanic
				? MinOceanicPlateDesiredElevation
				: MinContinentalPlateDesiredElevation;
			var maxDesiredElevation = plateType == PlateType.Oceanic
				? MaxOceanicPlateDesiredElevation
				: MaxContinentalPlateDesiredElevation;
			var desiredElevation = rnd.NextFloat(minDesiredElevation, maxDesiredElevation);
			var rotationalAxis   = math.normalize(rnd.NextFloat3());
			var rotationRate     = rnd.NextFloat(MinPlateRotationRate, MaxPlateRotationRate);
			var plate = new Plate
			{
				id               = plateIndex,
				type             = plateType,
				desiredElevation = desiredElevation,
				rotationAxis     = rotationalAxis,
				rotationRate     = rotationRate,
				seedTileIndex    = seedTileIndex
			};

			Plates[plateIndex] = plate;
		}
	}
}