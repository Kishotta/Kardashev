using Kardashev.PlanetGeneration;
using Kardashev.PlanetGeneration.Jobs;
using Shapes;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace Kardashev
{
	public class ClimateSimulation : ImmediateModeShapeDrawer
	{
		private NativeArray<Unity.Mathematics.Random> _randomNumberGenerators;

		[Header("Settings")] 
		[Range(3, 20)] 
		public int cycloneCount = 10;

		public bool showDebugWind = true;
		[Range(1f, 10f)]
		public float debugWindScale = 1f;

		[Range(1f, 50f)] 
		public float minWindSpeed = 20f;
		[Range(50f, 100f)] 
		public float maxWindSpeed = 50f;
		
		[Header("Heat")]
		[Range(0f, 1f)]
		public float heatAdvectionFactor = 0.2f;
		[Range(0f, 1f)]
		public float heatForcingFactor   = 0.05f;
		
		public UnityEvent<Planet> onClimateSimulated;

		private Planet _planet;
		private NativeArray<float3> _prevalentWinds;
		
		private void OnDestroy()
		{
			_planet?.Dispose();
		}
		
		public override void DrawShapes( Camera cam ){

			using( Draw.Command( cam ) )
			{
				if (_planet == null) return;
				if (!showDebugWind) return;
				for (var i = 0; i < _planet.TilePositions.Length; ++i)
				{
					var tilePosition = _planet.TilePositions[i] + math.normalize(_planet.TilePositions[i]) *
						(_planet.TileElevations[i] * 0.5f + 0.2f);
					var wind         = _prevalentWinds[i];
					Draw.Line(tilePosition, tilePosition + wind * debugWindScale, 0.2f, Color.white);
				}
			}

		}

		public void Simulate(Planet planet)
		{
			_planet?.Dispose();
			_prevalentWinds.Dispose();
			_planet = planet;
			
			_randomNumberGenerators = new NativeArray<Unity.Mathematics.Random>(JobsUtility.MaxJobThreadCount, Allocator.Persistent);
			Random.InitState((int)planet.Seed);
			for (var i = 0; i < _randomNumberGenerators.Length; i++)
			{
				_randomNumberGenerators[i] = new Unity.Mathematics.Random((uint)Random.Range(0, int.MaxValue));
			}
			
			var cyclonePoints = new NativeArray<CyclonePoint>(cycloneCount, Allocator.TempJob);
			var createCyclonePointsJob = new CreateCyclonePointsJob
			{
				RandomNumberGenerators = _randomNumberGenerators,
				PlanetRadius           = PlanetHelpers.Radius((int)planet.Size),
				CyclonePointCount      = cycloneCount,
				MinWindSpeed           = minWindSpeed,
				MaxWindSpeed           = maxWindSpeed,
				CyclonePoints          = cyclonePoints
			};
			var createCyclonePointsJobHandle = createCyclonePointsJob.Schedule(cycloneCount, 64);
			
			_prevalentWinds = new NativeArray<float3>(planet.TilePositions.Length, Allocator.Persistent);
			var calculatePrevalentWindsJob = new CalculatePrevalentWindsJob
			{
				TilePositions     = planet.TilePositions,
				CyclonePoints     = cyclonePoints,
				PlanetRadius      = PlanetHelpers.Radius((int)planet.Size),
				PrevalentWinds    = _prevalentWinds
			};
			var calculatePrevalentWindsJobHandle = calculatePrevalentWindsJob.Schedule(planet.TilePositions.Length, 64, createCyclonePointsJobHandle);
			
			var calculateBaseTemperatureJob = new CalculateBaseTemperatureJob
			{
				TilePositions     = planet.TilePositions,
				TileElevations    = planet.TileElevations,
				BaseTemperatures  = planet.TileTemperatures,
			};
			var calculateBaseTemperatureJobHandle = calculateBaseTemperatureJob.Schedule(planet.TilePositions.Length, 64, calculatePrevalentWindsJobHandle);

			var cachedBasedTemperatures = new NativeArray<float>(planet.TileTemperatures.Length, Allocator.TempJob);
			var copyBaseTemperaturesJob = new CopyValuesJob<float>
			{
				Source      = planet.TileTemperatures,
				Destination = cachedBasedTemperatures,
			};
			var cacheBaseTemperaturesJobHandle = copyBaseTemperaturesJob.Schedule(planet.TileTemperatures.Length, 64, calculateBaseTemperatureJobHandle);

			var simulationSteps         = (int)math.round(2 * math.PI * PlanetHelpers.Radius((int)planet.Size));
			var heatAdvectionDependency = cacheBaseTemperaturesJobHandle;
			var temperatureBuffer       = new NativeArray<float>(planet.TileTemperatures.Length, Allocator.TempJob);
			for (var step = 0; step < simulationSteps; step++)
			{
				var heatAdvectionJob = new HeatAdvectionJob
				{
					Spokes             = planet.Spokes,
					TileSpokes         = planet.TileSpokes,
					TileSpokeOpposites = planet.TileSpokeOpposites,
					TilePositions      = planet.TilePositions,
					TileElevations     = planet.TileElevations,
					TileWinds          = _prevalentWinds,
					BaseTemperatures   = cachedBasedTemperatures,
					TileTemperatures   = planet.TileTemperatures,
					NewTemperatures    = temperatureBuffer,
					AdvectionFactor    = heatAdvectionFactor,
					ForcingFactor      = heatForcingFactor,
				};
				var heatAdvectionJobHandle = heatAdvectionJob.Schedule(planet.TilePositions.Length, 64, heatAdvectionDependency);

				var swapTemperatureBufferJob = new CopyValuesJob<float>
				{
					Source      = temperatureBuffer,
					Destination = planet.TileTemperatures,
				};
				heatAdvectionDependency = swapTemperatureBufferJob.Schedule(planet.TilePositions.Length, 64, heatAdvectionJobHandle);
			}
			heatAdvectionDependency.Complete();
			
			cyclonePoints.Dispose();
			temperatureBuffer.Dispose();
			
			onClimateSimulated?.Invoke(planet);
		}
	}
}