using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Kardashev
{
	public struct TileNode
	{
		public int TileIndex;
		public int PlateId;

		public TileNode(int tileIndex, int plateId)
		{
			TileIndex = tileIndex;
			PlateId   = plateId;
		}
	}

	public struct InteriorNode : IComparable<InteriorNode>
	{
		public int TileIndex;
		public float DistanceToBoundary; // lower values have higher priority

		public int CompareTo(InteriorNode other)
		{
			return DistanceToBoundary.CompareTo(other.DistanceToBoundary);
		}
	}

	public class PlateTectonicsSimulation : MonoBehaviour
	{
		[Header("Settings")] [Range(4, 100)] public int plateCount = 12;

		[Range(0f, 1f)] public float oceanicPlateRatio = 0.7f;

		[Range(-3f, -1f)] public float maxOceanicPlateDesiredElevation = -1f;
		[Range(-7f, -4f)] public float minOceanicPlateDesiredElevation = -4f;

		[Range(4f, 7f)] public float maxContinentalPlateDesiredElevation = 4;
		[Range(0f, 3f)] public float minContinentalPlateDesiredElevation = 1f;

		public float minPlateRotationRate = -1f;
		public float maxPlateRotationRate = 1f;

		private PlanetMap _map;


		public AnimationCurve elevationCurve;
		public AnimationCurve oceanicSubductionCurve;
		public AnimationCurve continentalSubductionCurve;

		public UnityEvent<PlanetMap> onPlateTectonicsSimulated;

		private void OnDestroy()
		{
			_map?.Dispose();
		}

		public void Simulate(PlanetMap planetMap)
		{
			_map = planetMap;

			for (var i = 0; i < plateCount; i++)
			{
				var seedTileIndex = Random.Range(0, _map.TilePositions.Length);
				var plateType     = Random.value < oceanicPlateRatio ? PlateType.Oceanic : PlateType.Continental;
				var plate = new Plate
				{
					id   = i,
					type = plateType,
					desiredElevation = plateType == 0
						? Random.Range(minOceanicPlateDesiredElevation, maxOceanicPlateDesiredElevation)
						: Random.Range(minContinentalPlateDesiredElevation, maxContinentalPlateDesiredElevation),
					rotationAxis  = math.normalize(Random.insideUnitSphere),
					rotationRate  = Random.Range(minPlateRotationRate, maxPlateRotationRate),
					seedTileIndex = seedTileIndex,
					color         = Random.ColorHSV()
				};

				_map.Plates.Add(plate);
				_map.TilePlates[seedTileIndex] = plate.id;
			}

			var tileCosts = new NativeArray<float>(_map.TilePositions.Length, Allocator.Temp);
			for (var i = 0; i < planetMap.TilePositions.Length; i++)
			{
				tileCosts[i] = float.MaxValue;
			}

			GenerateTectonicPlates(_map);

			tileCosts.Dispose();

			CalculateGeologicalStresses(_map);
			// AddRandomTileElevations(_map);

			ApplyGeologicalStresses(_map);

			ApplyInteriorElevations(_map);

			onPlateTectonicsSimulated.Invoke(_map);
		}

		private void GenerateTectonicPlates(PlanetMap planetMap)
		{
			var frontier = new NativeList<TileNode>(Allocator.Temp);

			for (var i = 0; i < planetMap.Plates.Length; i++)
			{
				var plate         = planetMap.Plates[i];
				var seedTileIndex = plate.seedTileIndex;

				planetMap.TilePlates[seedTileIndex]     = plate.id;
				planetMap.TileElevations[seedTileIndex] = plate.desiredElevation;

				frontier.Add(new TileNode(seedTileIndex, plate.id));
			}

			while (frontier.Length > 0)
			{
				var randomIndex = Random.Range(0, frontier.Length);
				var current     = frontier[randomIndex];
				var plate       = planetMap.Plates[current.PlateId];

				var foundUnassignedNeighbor = false;

				// Get neighbor tile indices.
				var neighbors = planetMap.GetTileNeighborIndices(current.TileIndex);
				for (var i = 0; i < neighbors.Length; i++)
				{
					var neighborIndex = neighbors[i];

					if (planetMap.TilePlates[neighborIndex] == -1)
					{
						planetMap.TilePlates[neighborIndex]     = current.PlateId;
						planetMap.TileElevations[neighborIndex] = plate.desiredElevation;
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

		private void CalculateGeologicalStresses(PlanetMap planetMap)
		{
			// For each tile, compute its velocity from plate rotation.
			for (var tileIndex = 0; tileIndex < planetMap.TilePositions.Length; tileIndex++)
			{
				var plateId = planetMap.TilePlates[tileIndex];
				var plate   = planetMap.Plates[plateId];

				var tileVelocity = AngularTileVelocity(planetMap, plate.rotationAxis, plate.rotationRate, tileIndex);

				planetMap.TileVelocities[tileIndex] = tileVelocity;
			}

			// For each spoke, compute the stress between the neighboring tiles.
			for (var spokeIndex = 0; spokeIndex < planetMap.Spokes.Length; spokeIndex++)
			{
				var oppositeSpokeIndex = planetMap.TileSpokeOpposites[spokeIndex];

				if (spokeIndex > oppositeSpokeIndex) continue;

				var tileAIndex = planetMap.Spokes[spokeIndex];
				var tileBIndex = planetMap.Spokes[oppositeSpokeIndex];

				var tileAPosition = planetMap.TilePositions[tileAIndex];
				var tileBPosition = planetMap.TilePositions[tileBIndex];

				var boundaryNormal = math.normalize(tileBPosition - tileAPosition);
				var (c1, c2) = planetMap.GetSpokeCorners(spokeIndex);
				var boundaryTangent = math.normalize(planetMap.TileCorners[c2] - planetMap.TileCorners[c1]);

				var tileAVelocity         = planetMap.TileVelocities[tileAIndex];
				var tileBVelocity         = planetMap.TileVelocities[tileBIndex];
				var tileBRelativeVelocity = tileBVelocity - tileAVelocity;

				var pressure = math.dot(tileBRelativeVelocity, boundaryNormal);
				var shear    = math.abs(math.dot(tileBRelativeVelocity, boundaryTangent));

				planetMap.SpokePressures[spokeIndex]         = -pressure;
				planetMap.SpokePressures[oppositeSpokeIndex] = -pressure;

				planetMap.SpokeShears[spokeIndex]         = shear;
				planetMap.SpokeShears[oppositeSpokeIndex] = shear;
			}
		}

		private bool IsBoundaryTile(PlanetMap planetMap, int tileIndex)
		{
			var neighbors = planetMap.GetTileNeighborIndices(tileIndex);
			for (var i = 0; i < neighbors.Length; i++)
			{
				if (planetMap.TilePlates[neighbors[i]] != planetMap.TilePlates[tileIndex])
				{
					neighbors.Dispose();
					return true;
				}
			}

			return false;
		}

		private static float3 AngularTileVelocity(PlanetMap planetMap, float3 rotationalAxis, float rotationRate, int i)
		{
			// Compute angular velocity vector
			var omega        = rotationalAxis * rotationRate;
			var tilePosition = planetMap.TilePositions[i];

			// Compute tile's velocity
			var tileVelocity = math.cross(omega, tilePosition);
			return tileVelocity;
		}

		private void ApplyGeologicalStresses(PlanetMap planetMap)
		{
			// Thresholds and factors (tweak as needed)
			var lowStressThreshold  = 1f;  // below this, stress is considered negligible
			var highStressThreshold = 3f;  // above this, stress is considered high
			var upliftAmount        = 3f;  // additional elevation for direct collisions
			var subductionOffset    = -1f; // offset for subduction (oceanic plate gets lower)
			var divergentFactor     = 1f;  // scale factor for divergent/shearing stress


			var tileCount     = planetMap.TilePositions.Length;
			var newElevations = new NativeArray<float>(tileCount, Allocator.Temp);

			for (var i = 0; i < tileCount; i++)
			{
				var plateId = planetMap.TilePlates[i];
				newElevations[i] = planetMap.Plates[plateId].desiredElevation;
			}

			var spokeCount = planetMap.Spokes.Length;
			for (var spokeIndex = 0; spokeIndex < spokeCount; spokeIndex++)
			{
				var oppositeSpokeIndex = planetMap.TileSpokeOpposites[spokeIndex];
				if (spokeIndex > oppositeSpokeIndex) continue;

				var tileA = planetMap.Spokes[spokeIndex];
				var tileB = planetMap.Spokes[oppositeSpokeIndex];

				// Only process boundaries between different plates.
				var plateA = planetMap.TilePlates[tileA];
				var plateB = planetMap.TilePlates[tileB];
				if (plateA == plateB) continue;

				var pressure = planetMap.SpokePressures[spokeIndex];

				// Get each plate's desired elevation
				var desiredA   = planetMap.Plates[plateA].desiredElevation;
				var desiredB   = planetMap.Plates[plateB].desiredElevation;
				var plateTypeA = planetMap.Plates[plateA].type;
				var plateTypeB = planetMap.Plates[plateB].type;

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
				planetMap.TileElevations[i] = newElevations[i];
			}

			newElevations.Dispose();
		}
		
		public enum SubductionType
		{
			None,
			Oceanic,     // This tile's plate (oceanic) is subducting under another plate (continental)
			Continental, // This tile's plate (continental) is overriding another plate (oceanic)
		}

		private SubductionType GetSubductionType(PlanetMap planetMap, int tileIndex)
		{
			var plateId   = planetMap.TilePlates[tileIndex];
			var tilePlate = planetMap.Plates[plateId];
			var tileType  = tilePlate.type; // e.g. PlateType.Oceanic or PlateType.Continental

			// Get the neighboring tiles.
			var neighbors = planetMap.GetTileNeighborIndices(tileIndex);
			var result    = SubductionType.None;

			for (var i = 0; i < neighbors.Length; i++)
			{
				var neighborIndex     = neighbors[i];
				var neighborPlateId   = planetMap.TilePlates[neighborIndex];
				var neighborPlateType = planetMap.Plates[neighborPlateId].type;

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

		private void ApplyInteriorElevations(PlanetMap planetMap)
		{
			var tileCount      = planetMap.TilePositions.Length;
			var steps          = new NativeArray<int>(tileCount, Allocator.Temp);
			var boundarySource = new NativeArray<int>(tileCount, Allocator.Temp);
			var queue          = new NativeQueue<int>(Allocator.Temp);
			for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
			{
				steps[tileIndex]          = int.MaxValue;
				boundarySource[tileIndex] = -1;

				if (IsBoundaryTile(planetMap, tileIndex))
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

				var neighbors = planetMap.GetTileNeighborIndices(currentTileIndex);
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
			for (var i = 0; i < planetMap.Plates.Length; i++)
			{
				perPlateMaxDistance[i] = 0;
			}

			var propagatedBoundaryElevations = new NativeArray<float>(tileCount, Allocator.Temp);
			for (var i = 0; i < tileCount; i++)
			{
				if (boundarySource[i] != -1)
				{
					propagatedBoundaryElevations[i] = planetMap.TileElevations[boundarySource[i]];
				}
				else
				{
					propagatedBoundaryElevations[i] = planetMap.TileElevations[i];
				}

				var plateId = planetMap.TilePlates[i];
				if (steps[i] != int.MaxValue && steps[i] > perPlateMaxDistance[plateId])
					perPlateMaxDistance[plateId] = steps[i];
			}

			for (var i = 0; i < tileCount; i++)
			{
				var plateId     = planetMap.TilePlates[i];
				var maxDistance = math.max(perPlateMaxDistance[plateId], 1);
				var t           = math.clamp((float)steps[i] / maxDistance, 0f, 1f);

				var subductionType = GetSubductionType(planetMap, i);
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
				var desiredElevation  = planetMap.Plates[plateId].desiredElevation;

				var newElevation = math.lerp(boundaryElevation, desiredElevation, interpFactor);

				planetMap.TileElevations[i] = newElevation;
			}

			Debug.Log(perPlateMaxDistance[0]);

			queue.Dispose();
			steps.Dispose();
			perPlateMaxDistance.Dispose();
		}
	}
}