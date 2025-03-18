using System.Security.Cryptography.X509Certificates;
using Kardashev.PlanetGeneration;
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

		[Range(1f, 10f)]
		public float debugWindScale = 1f;

		[Range(1f, 50f)] 
		public float minWindSpeed = 20f;
		[Range(50f, 100f)] 
		public float maxWindSpeed = 50f;
		
		public UnityEvent<Planet> onClimateSimulated;

		private Planet _planet;
		private NativeArray<float3> _prevalentWinds;
		
		private void OnDestroy()
		{
			_planet?.Dispose();
		}
		
		public override void DrawShapes( Camera cam ){

			using( Draw.Command( cam ) ){

				if (_planet != null)
				{
					for (var i = 0; i < _planet.TilePositions.Length; ++i)
					{
						var tilePosition = _planet.TilePositions[i];
						var wind		 = _prevalentWinds[i];
						Draw.Line(tilePosition, tilePosition + wind * debugWindScale, 0.2f, Color.white);
					}
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
			calculatePrevalentWindsJobHandle.Complete();
			
			cyclonePoints.Dispose();
			
			onClimateSimulated?.Invoke(planet);
		}
	}
}