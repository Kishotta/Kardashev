using Kardashev.PlanetGeneration;
using Kardashev.PlanetGeneration.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace Kardashev
{
	public class PlateTectonicsSimulation : MonoBehaviour
	{
		private NativeArray<Unity.Mathematics.Random> _randomNumberGenerators;
		
		[Header("Settings")] [Range(4, 100)] public int plateCount = 12;

		[Range(0f, 1f)] public float oceanicPlateRatio = 0.7f;

		[Range(-3f, -1f)] public float maxOceanicPlateDesiredElevation = -1f;
		[Range(-7f, -4f)] public float minOceanicPlateDesiredElevation = -4f;

		[Range(4f, 7f)] public float maxContinentalPlateDesiredElevation = 4;
		[Range(0f, 3f)] public float minContinentalPlateDesiredElevation = 1f;

		public float minPlateRotationRate = -1f;
		public float maxPlateRotationRate = 1f;

		private Planet _map;
		public AnimationCurve elevationCurve;
		public AnimationCurve oceanicSubductionCurve;
		public AnimationCurve continentalSubductionCurve;
		
		public UnityEvent<Planet> onPlateTectonicsSimulated;

		private void OnDestroy()
		{
			_map?.Dispose();
		}

		public void Simulate(Planet planet)
		{
			_map?.Dispose();
			_map = planet;
			var plates = new NativeArray<Plate>(plateCount, Allocator.TempJob);
			var tilePlates = new NativeArray<int>(planet.TilePositions.Length, Allocator.TempJob);
			InitializeNativeArray(tilePlates, -1);
			var tileVelocities = new NativeArray<float3>(planet.TilePositions.Length, Allocator.Temp);
			InitializeNativeArray(tileVelocities, float3.zero);
			var spokePressures = new NativeArray<float>(planet.Spokes.Length, Allocator.Temp);
			InitializeNativeArray(spokePressures, 0f);
			var spokeShears	= new NativeArray<float>(planet.Spokes.Length, Allocator.Temp);
			InitializeNativeArray(spokeShears, 0f);

			_randomNumberGenerators = new NativeArray<Unity.Mathematics.Random>(JobsUtility.MaxJobThreadCount, Allocator.Persistent);
			Random.InitState((int)planet.Seed);
			for (var i = 0; i < _randomNumberGenerators.Length; i++)
			{
				_randomNumberGenerators[i] = new Unity.Mathematics.Random((uint)Random.Range(0, int.MaxValue));
			}

			var generateTectonicPlatesJob = new GenerateTectonicPlatesJob
			{
				RandomNumberGenerators              = _randomNumberGenerators,
				TileCount                           = planet.TilePositions.Length,
				OceanicPlateRatio                   = oceanicPlateRatio,
				MinOceanicPlateDesiredElevation     = minOceanicPlateDesiredElevation,
				MaxOceanicPlateDesiredElevation     = maxOceanicPlateDesiredElevation,
				MinContinentalPlateDesiredElevation = minContinentalPlateDesiredElevation,
				MaxContinentalPlateDesiredElevation = maxContinentalPlateDesiredElevation,
				MinPlateRotationRate                = minPlateRotationRate,
				MaxPlateRotationRate                = maxPlateRotationRate,
				Plates                              = plates,
			};
			var generateTectonicPlatesJobHandle = generateTectonicPlatesJob.Schedule(plates.Length, 64);

			var assignPlateSeedTilesJob = new AssignPlateSeedTilesJob
			{
				Plates     = plates,
				TilePlates = tilePlates,
				TileElevations = planet.TileElevations,
			};
			var assignPlateSeedTilesJobHandle = assignPlateSeedTilesJob.Schedule(tilePlates.Length, 64, generateTectonicPlatesJobHandle);

			var spreadTectonicPlatesJob = new SpreadTectonicPlatesJob
			{
				Seed               = planet.Seed,
				Spokes             = planet.Spokes,
				TileSpokes         = planet.TileSpokes,
				TileSpokeOpposites = planet.TileSpokeOpposites,
				Plates             = plates,
				TileElevations     = planet.TileElevations,
				TilePlates         = tilePlates
			};
			var spreadTectonicPlatesJobHandle = spreadTectonicPlatesJob.Schedule(assignPlateSeedTilesJobHandle);
			spreadTectonicPlatesJobHandle.Complete();

			// SpreadTectonicPlates(_map, plates, tilePlates);
			
			CreateTrench(plates, tilePlates, -10f, minOceanicPlateDesiredElevation, minContinentalPlateDesiredElevation, maxContinentalPlateDesiredElevation);

			CalculateGeologicalStresses(_map, plates, tilePlates, tileVelocities, spokePressures, spokeShears);

			ApplyGeologicalStresses(_map, plates, tilePlates, spokePressures);

			ApplyInteriorElevations(_map, plates, tilePlates);

			onPlateTectonicsSimulated.Invoke(_map);
			
			plates.Dispose();
			tilePlates.Dispose();
			tileVelocities.Dispose();
			spokePressures.Dispose();
			spokeShears.Dispose();
			_randomNumberGenerators.Dispose();
		}

		private void InitializeNativeArray<T>(NativeArray<T> array, T initialValue) where T : struct
		{
			for(var i = 0; i < array.Length; i++)
			{
				array[i] = initialValue;
			}
		}

		
		
		private void SpreadTectonicPlates(Planet planet, NativeArray<Plate> plates, NativeArray<int> tilePlates)
		{
			var frontier = new NativeList<TileNode>(Allocator.Temp);

			for (var i = 0; i < plates.Length; i++)
			{
				var plate         = plates[i];
				var seedTileIndex = plate.seedTileIndex;

				frontier.Add(new TileNode(seedTileIndex, plate.id));
			}

			while (frontier.Length > 0)
			{
				var randomIndex = Random.Range(0, frontier.Length);
				var current     = frontier[randomIndex];
				var plate       = plates[current.PlateId];

				var foundUnassignedNeighbor = false;

				// Get neighbor tile indices.
				var neighbors = planet.GetTileNeighborIndices(current.TileIndex);
				for (var i = 0; i < neighbors.Length; i++)
				{
					var neighborIndex = neighbors[i];

					if (tilePlates[neighborIndex] == -1)
					{
						tilePlates[neighborIndex]     = current.PlateId;
						planet.TileElevations[neighborIndex] = plate.desiredElevation;
						frontier.Add(new TileNode(neighborIndex, current.PlateId));

						foundUnassignedNeighbor = true;
						break;
					}
				}

				neighbors.Dispose();

				if (!foundUnassignedNeighbor)
				{
					frontier.RemoveAtSwapBack(randomIndex);
				}
			}

			frontier.Dispose();
		}

		private void CreateTrench(
			NativeArray<Plate> plates, 
			NativeArray<int> tilePlates, 
			float trenchDepth, 
			float deepOceanDepth,
			float planeHeight,
			float PeakHeight)
		{
			var plateSizes = new NativeArray<int>(plateCount, Allocator.Temp);
			for (var i = 0; i < tilePlates.Length; i++)
			{
				plateSizes[tilePlates[i]]++;
			}

			var smallestOceanPlateId = -1;
			var largestOceanPlateId = -1;
			var smallestContinentalPlateId = -1;
			var largestContinentalPlateId = -1;
			for (var i = 0; i < plateSizes.Length; i++)
			{
				if (plates[i].type == PlateType.Oceanic)
				{
					if (smallestOceanPlateId == -1 || plateSizes[i] < plateSizes[smallestOceanPlateId])
					{
						smallestOceanPlateId = i;
					}
					if (largestOceanPlateId == -1 || plateSizes[i] > plateSizes[largestOceanPlateId])
					{
						largestOceanPlateId = i;
					}
				}

				if (plates[i].type == PlateType.Continental)
				{
					if (largestContinentalPlateId == -1 || plateSizes[i] > plateSizes[largestContinentalPlateId])
					{
						largestContinentalPlateId = i;
					}
					if (smallestContinentalPlateId == -1 || plateSizes[i] < plateSizes[smallestContinentalPlateId])
					{
						smallestContinentalPlateId = i;
					}
				}
			}

			var plate = plates[smallestOceanPlateId];
			plate.desiredElevation       = trenchDepth;
			plates[smallestOceanPlateId] = plate;
			
			plate                       = plates[largestOceanPlateId];
			plate.desiredElevation      = deepOceanDepth;
			plates[largestOceanPlateId] = plate;
			
			plate 					 = plates[smallestContinentalPlateId];
			plate.desiredElevation     = PeakHeight;
			plates[smallestContinentalPlateId] = plate;
			
			plate 					 = plates[largestContinentalPlateId];
			plate.desiredElevation     = 1f;
			plates[largestContinentalPlateId] = plate;
			
			plateSizes.Dispose();
		}

		private void CalculateGeologicalStresses(Planet planet, NativeArray<Plate> plates, NativeArray<int> tilePlates, NativeArray<float3> tileVelocities, NativeArray<float> spokePressures, NativeArray<float> spokeShears)
		{
			// For each tile, compute its velocity from plate rotation.
			for (var tileIndex = 0; tileIndex < planet.TilePositions.Length; tileIndex++)
			{
				var plateId = tilePlates[tileIndex];
				var plate   = plates[plateId];

				var tileVelocity = AngularTileVelocity(planet, plate.rotationAxis, plate.rotationRate, tileIndex);

				tileVelocities[tileIndex] = tileVelocity;
			}

			// For each spoke, compute the stress between the neighboring tiles.
			for (var spokeIndex = 0; spokeIndex < planet.Spokes.Length; spokeIndex++)
			{
				var oppositeSpokeIndex = planet.TileSpokeOpposites[spokeIndex];

				if (spokeIndex > oppositeSpokeIndex) continue;

				var tileAIndex = planet.Spokes[spokeIndex];
				var tileBIndex = planet.Spokes[oppositeSpokeIndex];

				var tileAPosition = planet.TilePositions[tileAIndex];
				var tileBPosition = planet.TilePositions[tileBIndex];

				var boundaryNormal = math.normalize(tileBPosition - tileAPosition);
				var (c1, c2) = planet.GetSpokeCorners(spokeIndex);
				var boundaryTangent = math.normalize(planet.TileCorners[c2] - planet.TileCorners[c1]);

				var tileAVelocity         = tileVelocities[tileAIndex];
				var tileBVelocity         = tileVelocities[tileBIndex];
				var tileBRelativeVelocity = tileBVelocity - tileAVelocity;

				var pressure = math.dot(tileBRelativeVelocity, boundaryNormal);
				var shear    = math.abs(math.dot(tileBRelativeVelocity, boundaryTangent));

				spokePressures[spokeIndex]         = -pressure;
				spokePressures[oppositeSpokeIndex] = -pressure;

				spokeShears[spokeIndex]         = shear;
				spokeShears[oppositeSpokeIndex] = shear;
			}
		}

		private bool IsBoundaryTile(Planet planet, int tileIndex, NativeArray<int> tilePlates)
		{
			var neighbors = planet.GetTileNeighborIndices(tileIndex);
			for (var i = 0; i < neighbors.Length; i++)
			{
				if (tilePlates[neighbors[i]] != tilePlates[tileIndex])
				{
					neighbors.Dispose();
					return true;
				}
			}

			return false;
		}

		private static float3 AngularTileVelocity(Planet planet, float3 rotationalAxis, float rotationRate, int i)
		{
			// Compute angular velocity vector
			var omega        = rotationalAxis * rotationRate;
			var tilePosition = planet.TilePositions[i];

			// Compute tile's velocity
			var tileVelocity = math.cross(omega, tilePosition);
			return tileVelocity;
		}

		private void ApplyGeologicalStresses(Planet planet, NativeArray<Plate> plates, NativeArray<int> tilePlates, NativeArray<float> spokePressures)
		{
			// Thresholds and factors (tweak as needed)
			var lowStressThreshold  = 1f;  // below this, stress is considered negligible
			var highStressThreshold = 3f;  // above this, stress is considered high
			var upliftAmount        = 3f;  // additional elevation for direct collisions
			var subductionOffset    = -1f; // offset for subduction (oceanic plate gets lower)
			var divergentFactor     = 1f;  // scale factor for divergent/shearing stress


			var tileCount     = planet.TilePositions.Length;
			var newElevations = new NativeArray<float>(tileCount, Allocator.Temp);

			for (var i = 0; i < tileCount; i++)
			{
				var plateId = tilePlates[i];
				newElevations[i] = plates[plateId].desiredElevation;
			}

			var spokeCount = planet.Spokes.Length;
			for (var spokeIndex = 0; spokeIndex < spokeCount; spokeIndex++)
			{
				var oppositeSpokeIndex = planet.TileSpokeOpposites[spokeIndex];
				if (spokeIndex > oppositeSpokeIndex) continue;

				var tileA = planet.Spokes[spokeIndex];
				var tileB = planet.Spokes[oppositeSpokeIndex];

				// Only process boundaries between different plates.
				var plateA = tilePlates[tileA];
				var plateB = tilePlates[tileB];
				if (plateA == plateB) continue;

				var pressure = spokePressures[spokeIndex];

				// Get each plate's desired elevation
				var desiredA   = plates[plateA].desiredElevation;
				var plateTypeA = plates[plateA].type;
				var desiredB   = plates[plateB].desiredElevation;
				var plateTypeB = plates[plateB].type;

				var boundaryElevation = 0f;

				if (math.abs(pressure) < lowStressThreshold)
				{
					boundaryElevation = (desiredA + desiredB) / 2f;
				}
				else if (pressure > highStressThreshold)
				{
					if (plateTypeA == plateTypeB)
					{
						boundaryElevation = math.max(desiredA, desiredB) + upliftAmount;
					}
					else
					{
						boundaryElevation = (plateTypeA == PlateType.Continental ? desiredA : desiredB) + upliftAmount;
					}
				}
				else if (pressure < -highStressThreshold)
				{
					boundaryElevation = math.max(desiredA, desiredB) * divergentFactor;
				}
				else
				{
					// Intermediate stress: interpolate between the average and the maximum.
					var t = (math.abs(pressure) - lowStressThreshold) / (highStressThreshold - lowStressThreshold);
					boundaryElevation = math.lerp((desiredA + desiredB) / 2f, math.max(desiredA, desiredB), t);
				}

				newElevations[tileA] = (newElevations[tileA] + boundaryElevation) * 0.5f;
				newElevations[tileB] = (newElevations[tileB] + boundaryElevation) * 0.5f;
			}

			for (var i = 0; i < tileCount; i++)
			{
				planet.TileElevations[i] = newElevations[i];
			}

			newElevations.Dispose();
		}
		
		public enum SubductionType
		{
			None,
			Oceanic,     // This tile's plate (oceanic) is subducting under another plate (continental)
			Continental, // This tile's plate (continental) is overriding another plate (oceanic)
		}

		private SubductionType GetSubductionType(Planet planet, int tileIndex, NativeArray<Plate> plates, NativeArray<int> tilePlates)
		{
			var plateId   = tilePlates[tileIndex];
			var tilePlate = plates[plateId];
			var tileType  = tilePlate.type; // e.g. PlateType.Oceanic or PlateType.Continental

			// Get the neighboring tiles.
			var neighbors = planet.GetTileNeighborIndices(tileIndex);
			var result    = SubductionType.None;

			for (var i = 0; i < neighbors.Length; i++)
			{
				var neighborIndex     = neighbors[i];
				var neighborPlateId   = tilePlates[neighborIndex];
				var neighborPlateType = plates[neighborPlateId].type;

				// If the plate types differ, we have a candidate subduction boundary.
				if (neighborPlateType != tileType)
				{
					// If this tile is oceanic and the neighbor is continental,
					// then the oceanic plate is subducting.
					if (tileType == PlateType.Oceanic && neighborPlateType == PlateType.Continental)
					{
						result = SubductionType.Oceanic;
						break;
					}
					// If this tile is continental and the neighbor is oceanic,
					// then the continental plate is overriding.

					if (tileType == PlateType.Continental && neighborPlateType == PlateType.Oceanic)
					{
						result = SubductionType.Continental;
						break;
					}
				}
			}

			neighbors.Dispose();
			return result;
		}

		private void ApplyInteriorElevations(Planet planet, NativeArray<Plate> plates, NativeArray<int> tilePlates)
		{
			var tileCount      = planet.TilePositions.Length;
			var steps          = new NativeArray<int>(tileCount, Allocator.Temp);
			var boundarySource = new NativeArray<int>(tileCount, Allocator.Temp);
			var queue          = new NativeQueue<int>(Allocator.Temp);
			for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
			{
				steps[tileIndex]          = int.MaxValue;
				boundarySource[tileIndex] = -1;

				if (IsBoundaryTile(planet, tileIndex, tilePlates))
				{
					steps[tileIndex]          = 0;
					boundarySource[tileIndex] = tileIndex;
					queue.Enqueue(tileIndex);
				}
			}

			while (!queue.IsEmpty())
			{
				var currentTileIndex = queue.Dequeue();
				var currentStep      = steps[currentTileIndex];

				var neighbors = planet.GetTileNeighborIndices(currentTileIndex);
				for (var j = 0; j < neighbors.Length; j++)
				{
					var neighborIndex = neighbors[j];
					var newSteps      = currentStep + 1;

					if (newSteps < steps[neighborIndex])
					{
						steps[neighborIndex]          = newSteps;
						boundarySource[neighborIndex] = boundarySource[currentTileIndex];
						queue.Enqueue(neighborIndex);
					}
				}

				neighbors.Dispose();
			}

			var perPlateMaxDistance = new NativeArray<int>(plateCount, Allocator.Temp);
			for (var i = 0; i < plates.Length; i++)
			{
				perPlateMaxDistance[i] = 0;
			}

			var propagatedBoundaryElevations = new NativeArray<float>(tileCount, Allocator.Temp);
			for (var i = 0; i < tileCount; i++)
			{
				if (boundarySource[i] != -1)
				{
					propagatedBoundaryElevations[i] = planet.TileElevations[boundarySource[i]];
				}
				else
				{
					propagatedBoundaryElevations[i] = planet.TileElevations[i];
				}

				var plateId = tilePlates[i];
				if (steps[i] != int.MaxValue && steps[i] > perPlateMaxDistance[plateId])
					perPlateMaxDistance[plateId] = steps[i];
			}

			for (var i = 0; i < tileCount; i++)
			{
				var plateId     = tilePlates[i];
				var maxDistance = math.max(perPlateMaxDistance[plateId], 1);
				var t           = math.clamp((float)steps[i] / maxDistance, 0f, 1f);

				var subductionType = GetSubductionType(planet, i, plates, tilePlates);
				var interpFactor = 0f;

				if (subductionType == SubductionType.Oceanic)
				{
					interpFactor = oceanicSubductionCurve.Evaluate(t);
				}
				else if (subductionType == SubductionType.Continental)
				{
					interpFactor = continentalSubductionCurve.Evaluate(t);
				}
				else
				{
					interpFactor = elevationCurve.Evaluate(t);
				}
				
				var boundaryElevation = propagatedBoundaryElevations[i];
				var desiredElevation  = plates[plateId].desiredElevation;

				var newElevation = math.lerp(boundaryElevation, desiredElevation, interpFactor);

				planet.TileElevations[i] = newElevation;
			}

			queue.Dispose();
			steps.Dispose();
			perPlateMaxDistance.Dispose();
		}

		private struct TileNode
		{
			public int TileIndex;
			public int PlateId;

			public TileNode(int tileIndex, int plateId)
			{
				TileIndex = tileIndex;
				PlateId   = plateId;
			}
		}
	}
}