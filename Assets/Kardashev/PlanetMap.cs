using Kardashev.PlanetGeneration;
using Kardashev.PlanetGeneration.Jobs;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Kardashev
{
	public class PlanetMap : MonoBehaviour
	{
		[Header("Settings")] 
		public uint seed = 1337;
		
		public PlanetSize size = PlanetSize.Medium;

		[Header("Edge Flip Settings")]
		[Range(0f, 0.1f)]
		public float irregularity = 0.05f;
		[Range(4, 5)]
		[Tooltip("Minimum number of neighbors a tile must have")]
		public int minimumNeighborCount = 5;
		[Range(7,9)]
        [Tooltip("Maximum number of neighbors a tile can have")]
        public int maximumNeighborCount = 8;
        [Range(0f, 1f)]
		[Tooltip("Maximum allowed ratio difference between an original edge length and a flipped edge length.")]
		public float maxEdgeLengthDifferenceRatio = 0.2f;
		
		[Header("Relaxation Settings")]
		public int relaxationIterations = 10;

		[Range(0f, 1f)] 
		public float relaxationStrength = 1f;
		
		[Header("Output")]
		public UnityEvent<Planet> onMapCreated;
		
		private void Awake()
		{
			Application.targetFrameRate = 75;
		}

		[Button("Create Planet Map")]
		private void CreateIcosahedronAndSubdividePlanetMap()
		{
			Random.InitState((int)seed);
			
			var planetMap = new Planet(seed, size, Allocator.Persistent);
			
			var vertices = new NativeArray<float3>(Icosahedron.Vertices, Allocator.TempJob);
			var faces = new NativeArray<int3>(Icosahedron.Faces, Allocator.TempJob);
			var edgeLookupTable = new NativeParallelHashMap<(int, int), int>(PlanetHelpers.SpokeCount((int)size), Allocator.TempJob);
			
			var assignBaseIcosahedronVerticesJobHandle = AssignBaseIcosahedronVertices(vertices, planetMap);
			var assignBaseIcosahedronFacesJobHandle = AssignBaseIcosahedronFaces(faces, planetMap, edgeLookupTable, assignBaseIcosahedronVerticesJobHandle);
			var connectOppositeSpokesJobHandle = ConnectOppositeSpokes(planetMap, edgeLookupTable, assignBaseIcosahedronFacesJobHandle);
			connectOppositeSpokesJobHandle.Complete();

			vertices.Dispose();
			faces.Dispose();
			edgeLookupTable.Dispose();
			
			for(var i = 0; i < (int)size; i++)
			{
				Subdivide(planetMap);
			}
			
			var edgeFlipCount = (int)(planetMap.Spokes.Length * irregularity);
			for(var relaxationStep = 0; relaxationStep < relaxationIterations; relaxationStep++)
			{
				for (var i = 0; i < edgeFlipCount / relaxationIterations; i++)
				{
					FlipRandomEdge(planetMap);
				}
				RelaxVertices(planetMap);
			}
			
			onMapCreated?.Invoke(planetMap);
		}
		
		private JobHandle AssignBaseIcosahedronVertices(
			NativeArray<float3> vertices,
			Planet planetMap)
		{
			var assignBaseIcosahedronVerticesJob = new AssignBaseIcosahedronVerticesJob
			{
				Radius        = PlanetHelpers.Radius((int)size),
				Vertices      = vertices,
				TilePositions = planetMap.TilePositions
			};
			return assignBaseIcosahedronVerticesJob.Schedule(vertices.Length, 64);
		}

		private static JobHandle AssignBaseIcosahedronFaces(
			NativeArray<int3> faces,
			Planet planetMap,
			NativeParallelHashMap<(int, int), int> edgeLookupTable,
			JobHandle dependencies)
		{
			var assignBaseIcosahedronFacesJob = new AssignBaseIcosahedronFacesJob
			{
				Faces           = faces,
				Spokes          = planetMap.Spokes,
				TileSpokes      = planetMap.TileSpokes,
				TilePositions   = planetMap.TilePositions,
				TileCorners     = planetMap.TileCorners,
				EdgeLookupTable = edgeLookupTable.AsParallelWriter(),
			};
			return assignBaseIcosahedronFacesJob.Schedule(faces.Length, 64, dependencies);
		}
		
		private static JobHandle ConnectOppositeSpokes(
			Planet planetMap, 
			NativeParallelHashMap<(int, int), int> edgeLookupTable, 
			JobHandle dependencies)
		{
			var connectOppositeSpokesJob = new ConnectOppositeSpokesJob
			{
				Spokes             = planetMap.Spokes,
				TileSpokeOpposites = planetMap.TileSpokeOpposites,
				EdgeLookupTable    = edgeLookupTable
			};
			return connectOppositeSpokesJob.Schedule(planetMap.Spokes.Length, 64, dependencies);
		}

		#region Subdivision

		private static void Subdivide(Planet planet)
        {
            var tempMap         = new Planet(planet.Seed, planet.Size, Allocator.Temp);
            var edgeLookupTable = new NativeHashMap<(int, int), int>(0, Allocator.Temp);
            
            // Add all existing vertices to the temp map
            var tileCount = PlanetHelpers.FirstFreeIndex(planet.TilePositions);
            for (var i = 0; i < tileCount; i++)
            {
                PlanetHelpers.AddTileCenter(i, planet.TilePositions[i], tempMap.TilePositions);
            }

            // Cache midpoints
            var midpointLookup = new NativeHashMap<(int, int), int>(planet.Spokes.Length, Allocator.Temp);

            var cornerCount = PlanetHelpers.FirstFreeIndex(planet.TileCorners);
            for (var corner = 0; corner < cornerCount; corner++)
            {
                var baseEdge = corner * 3;
                
                // Get the three tile indices of the current corner.
                var tileA = planet.Spokes[baseEdge];
                var tileB = planet.Spokes[baseEdge + 1];
                var tileC = planet.Spokes[baseEdge + 2];
                
                // Compute (or reuse) midpoints for each edge.
                var abMidpoint = GetMidpointCornerIndex(tempMap, midpointLookup, tileA, tileB);
                var bcMidpoint = GetMidpointCornerIndex(tempMap, midpointLookup, tileB, tileC);
                var caMidpoint = GetMidpointCornerIndex(tempMap, midpointLookup, tileC, tileA);
                
                // Create four new corners to replace the original corner.
                PlanetHelpers.AddTileCorner(
	                corner * 4,
	                tileA, 
	                abMidpoint,
	                caMidpoint,
	                edgeLookupTable,
	                tempMap.Spokes,
	                tempMap.TileSpokes,
	                tempMap.TileSpokeOpposites,
	                tempMap.TilePositions,
	                tempMap.TileCorners);
                PlanetHelpers.AddTileCorner(
					corner * 4 + 1,
					tileB, 
					bcMidpoint, 
					abMidpoint, 
					edgeLookupTable,
	                tempMap.Spokes,
	                tempMap.TileSpokes,
	                tempMap.TileSpokeOpposites,
	                tempMap.TilePositions,
	                tempMap.TileCorners);
                PlanetHelpers.AddTileCorner(
					corner * 4 + 2,
					tileC, 
					caMidpoint, 
					bcMidpoint, 
					edgeLookupTable,
	                tempMap.Spokes,
	                tempMap.TileSpokes,
	                tempMap.TileSpokeOpposites,
	                tempMap.TilePositions,
	                tempMap.TileCorners);
                PlanetHelpers.AddTileCorner(
					corner * 4 + 3,
					abMidpoint, 
					bcMidpoint, 
					caMidpoint, 
					edgeLookupTable,
	                tempMap.Spokes,
	                tempMap.TileSpokes,
	                tempMap.TileSpokeOpposites,
	                tempMap.TilePositions,
	                tempMap.TileCorners);
            }
            midpointLookup.Dispose();
            edgeLookupTable.Dispose();
            
            planet.Spokes.CopyFrom(tempMap.Spokes);
            planet.TileSpokeOpposites.CopyFrom(tempMap.TileSpokeOpposites);
            planet.TileSpokes.CopyFrom(tempMap.TileSpokes);
            planet.TilePositions.CopyFrom(tempMap.TilePositions);
            planet.TileCorners.CopyFrom(tempMap.TileCorners);
            planet.TileElevations.CopyFrom(tempMap.TileElevations);
            
            tempMap.Dispose();
        }

        private static int GetMidpointCornerIndex(Planet temp, NativeHashMap<(int, int), int> midpointLookup, int tileAIndex, int tileBIndex)
        {
            // Order the indices to ensure the key is consistent.
            var key = (math.min(tileAIndex, tileBIndex), math.max(tileAIndex, tileBIndex));
            if (midpointLookup.TryGetValue(key, out var midpointIndex))
            {
                return midpointIndex;
            }
            
            // Compute the midpoint position.
            var midpointPosition = (
	            temp.TilePositions[tileAIndex] +
	            temp.TilePositions[tileBIndex]) / 2;
            
            // Optionally, project the midpoint back onto the sphere.
            midpointPosition = math.normalize(midpointPosition) * math.length(temp.TilePositions[tileAIndex]);
            
            // Add the midpoint as a new corner in the subdivided map.
            midpointIndex = PlanetHelpers.AddTileCenter(
	            midpointPosition,
	            temp.TilePositions);
            midpointLookup[key] = midpointIndex;
            return midpointIndex;
        }
        
        #endregion
        
        #region Edge Flip

        private void FlipRandomEdge(Planet planet)
        {
	        var edgeIndex = Random.Range(0, planet.Spokes.Length);
	        if (CanFlipEdge(planet, edgeIndex))
	        {
		        FlipEdge(planet, edgeIndex);
	        }
        }

        private bool CanFlipEdge(Planet planet, int edgeIndex)
        {
	        var edgeOppositeIndex = planet.TileSpokeOpposites[edgeIndex];
	        if(edgeOppositeIndex == -1)
	        {
		        // Debug.Log("Edge Opposite not set");
		        return false;
	        }
	        
	        var tileAIndex = planet.Spokes[edgeIndex];
	        var tileBIndex = planet.Spokes[edgeOppositeIndex];
	        var tileCIndex = planet.Spokes[Planet.GetPreviousEdgeIndex(edgeIndex)];
	        var tileDIndex = planet.Spokes[Planet.GetPreviousEdgeIndex(edgeOppositeIndex)];
	        
	        var tileASpokeIndices = PlanetHelpers.TileSpokeIndices(tileAIndex, planet.TileSpokes, planet.TileSpokeOpposites);
	        if (tileASpokeIndices.Length <= minimumNeighborCount)
	        {
		        // Debug.Log("Tile A has too few neighbors: " + tileASpokeIndices.Length);
		        tileASpokeIndices.Dispose();
		        return false;
	        }
	        
	        var tileBSpokeIndices = PlanetHelpers.TileSpokeIndices(tileBIndex, planet.TileSpokes, planet.TileSpokeOpposites);
	        if (tileBSpokeIndices.Length <= minimumNeighborCount)
	        {
		        // Debug.Log("Tile B has too few neighbors: " + tileBIndex);
		        tileBSpokeIndices.Dispose();
		        return false;
	        }
	        
	        var tileCSpokeIndices = PlanetHelpers.TileSpokeIndices(tileCIndex, planet.TileSpokes, planet.TileSpokeOpposites);
	        if (tileCSpokeIndices.Length >= maximumNeighborCount)
	        {
		        // Debug.Log("Tile C has too many neighbors: " + tileCIndex);
		        tileCSpokeIndices.Dispose();
		        return false;
	        }
	        
	        var tileDSpokeIndices = PlanetHelpers.TileSpokeIndices(tileDIndex, planet.TileSpokes, planet.TileSpokeOpposites);
	        if (tileDSpokeIndices.Length >= maximumNeighborCount)
	        {
		        // Debug.Log("Tile D has too many neighbors: " + tileDIndex);
		        tileDSpokeIndices.Dispose();
		        return false;
	        }
	        
	        var originalLength = math.distance(planet.TilePositions[tileAIndex], planet.TilePositions[tileBIndex]);
	        var flippedLength = math.distance(planet.TilePositions[tileCIndex], planet.TilePositions[tileDIndex]);
	        var lengthDifferenceRatio = (flippedLength - originalLength) / ((originalLength + flippedLength) / 2);
	        if (lengthDifferenceRatio > maxEdgeLengthDifferenceRatio)
	        {
		        // Debug.Log("Edge length difference ratio is too high");
		        return false;
	        }
	        
	        if (HasObtuseAngle(planet, edgeIndex))
	        {
		        // Debug.Log("Has obtuse angle");
		        return false;
	        }

	        return true;
        }
        
        private bool HasObtuseAngle(Planet map, int edgeIndex)
        {
	        // For both triangles sharing the edge, compute the angle at the vertices along the shared edge.
	        var angle1 = GetAngleAtSharedEdge(map, edgeIndex, side: 0);
	        var angle2 = GetAngleAtSharedEdge(map, edgeIndex, side: 1);
	        // If either angle is greater than 90 degrees, return true.
	        return (angle1 > math.PI / 2f || angle2 > math.PI / 2f);
        }
        
        /// <summary>
        /// For the triangle on the given side of the half-edge, compute the angle at the vertex that is opposite the shared edge.
        /// The shared edge is defined by the half-edge passed in; the "other" triangle is accessed via its opposite pointer if side==1.
        /// </summary>
        private float GetAngleAtSharedEdge(Planet map, int edgeIndex, int side)
        {
	        // If we are checking side 1 (the triangle opposite to the given half-edge),
	        // then use the opposite half-edge.
	        if (side == 1)
	        {
		        var oppositeEdge = map.TileSpokeOpposites[edgeIndex];
		        if (oppositeEdge == -1)
		        {
			        // If there is no opposite (i.e. boundary edge), return 0 or some default.
			        return 0f;
		        }
		        edgeIndex = oppositeEdge;
	        }
    
	        // For the triangle associated with the half-edge at edgeIndex,
	        // we assume the triangle is stored as three consecutive half-edges:
	        // A = map.Tiles[edgeIndex] (start of the shared edge),
	        // B = map.Tiles[GetNextEdgeIndex(edgeIndex)] (end of the shared edge),
	        // C = map.Tiles[GetPreviousEdgeIndex(edgeIndex)] (the vertex opposite the shared edge).
	        var tileAIndex = map.Spokes[edgeIndex];
	        var tileBIndex = map.Spokes[PlanetHelpers.NextEdgeIndex(edgeIndex)];
	        var tileCIndex = map.Spokes[PlanetHelpers.PreviousEdgeIndex(edgeIndex)];
    
	        var posA = map.TilePositions[tileAIndex];
	        var posB = map.TilePositions[tileBIndex];
	        var posC = map.TilePositions[tileCIndex];
    
	        // Compute vectors from vertex C (the opposite vertex) to A and to B.
	        var v1 = posA - posC;
	        var v2 = posB - posC;
    
	        // Calculate the cosine of the angle at C.
	        var dot      = math.dot(v1, v2);
	        var mag1     = math.length(v1);
	        var mag2     = math.length(v2);
	        var cosTheta = dot / (mag1 * mag2);
	        // Clamp to avoid domain errors due to floating point inaccuracies.
	        cosTheta = math.clamp(cosTheta, -1f, 1f);
	        var angle = math.acos(cosTheta);
	        return angle;
        }

        private void FlipEdge(Planet planet, int edgeIndex)
        {
	        // Get all pre-flipped indices.
	        var abIndex = edgeIndex;
	        var bcIndex = PlanetHelpers.NextEdgeIndex(abIndex);
	        var cbIndex = planet.TileSpokeOpposites[bcIndex];
	        var caIndex = PlanetHelpers.PreviousEdgeIndex(abIndex);
	        var acIndex = planet.TileSpokeOpposites[caIndex];
	        
	        var baIndex = planet.TileSpokeOpposites[abIndex];
	        var adIndex = PlanetHelpers.NextEdgeIndex(baIndex);
	        var daIndex = planet.TileSpokeOpposites[adIndex];
	        var dbIndex = PlanetHelpers.PreviousEdgeIndex(baIndex);
	        var bdIndex = planet.TileSpokeOpposites[dbIndex];
	        
	        var aIndex = planet.Spokes[abIndex];
	        var bIndex = planet.Spokes[bcIndex];
	        var cIndex = planet.Spokes[caIndex];
	        var dIndex = planet.Spokes[daIndex];
	        
	        var abcCornerIndex = PlanetHelpers.CornerIndex(abIndex);
	        var abdCornerIndex = PlanetHelpers.CornerIndex(baIndex);

	        // Reassign Triangles
	        planet.Spokes[abIndex] = dIndex;
	        planet.Spokes[bcIndex] = cIndex;
	        planet.Spokes[caIndex] = aIndex;
	        
	        planet.Spokes[baIndex] = cIndex;
	        planet.Spokes[adIndex] = dIndex;
	        planet.Spokes[dbIndex] = bIndex;
	        
	        planet.TileCorners[abcCornerIndex] = (planet.TilePositions[aIndex] + planet.TilePositions[cIndex] + planet.TilePositions[dIndex]) / 3;
	        planet.TileCorners[abdCornerIndex] = (planet.TilePositions[bIndex] + planet.TilePositions[cIndex] + planet.TilePositions[dIndex]) / 3;

	        planet.TileSpokes[aIndex] = caIndex;
	        planet.TileSpokes[bIndex] = dbIndex;
	        planet.TileSpokes[cIndex] = bcIndex;
	        planet.TileSpokes[dIndex] = adIndex;
	        
	        // Reassign Edge Opposites.
	        planet.TileSpokeOpposites[caIndex] = daIndex;
	        planet.TileSpokeOpposites[daIndex] = caIndex;
	        
	        planet.TileSpokeOpposites[adIndex] = bdIndex;
	        planet.TileSpokeOpposites[bdIndex]   = adIndex;
	        
	        planet.TileSpokeOpposites[dbIndex] = cbIndex;
	        planet.TileSpokeOpposites[cbIndex]   = dbIndex;
	        
	        planet.TileSpokeOpposites[bcIndex] = acIndex;
	        planet.TileSpokeOpposites[acIndex]   = bcIndex;
        }
        
        #endregion
        
        #region Vertex Relaxation
        
        private void RelaxVertices(Planet planet)
        {
	        // Create a temporary copy of the vertex positions.
	        var newTilePositions = new NativeArray<float3>(planet.TilePositions.Length, Allocator.Temp);
	        for (var tileIndex = 0; tileIndex < planet.TilePositions.Length; tileIndex++)
	        {
		        // Get the neighboring vertices (using your connectivity method).
		        var neighbors = PlanetHelpers.TileNeighborIndices(tileIndex, planet.Spokes, planet.TileSpokes, planet.TileSpokeOpposites);
		        var centroid  = float3.zero;
		        for (var neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
		        {
			        centroid += planet.TilePositions[neighbors[neighborIndex]];
		        }
		        centroid /= neighbors.Length;
		        neighbors.Dispose();

		        // Reproject onto the sphere.
		        newTilePositions[tileIndex] = math.normalize(centroid) * PlanetHelpers.Radius((int)size);
	        }
	        // Update the map with the new positions.
	        for (var i = 0; i < planet.TilePositions.Length; i++)
	        {
		        planet.TilePositions[i] = math.lerp(planet.TilePositions[i], newTilePositions[i], relaxationStrength);
	        }
	        newTilePositions.Dispose();
	        
	        UpdateTileCorners(planet);
        }
        
        private void UpdateTileCorners(Planet planet)
        {
	        // Each triangle (or face) is stored in 3 consecutive entries in Tiles.
	        var cornerCount = planet.TileCorners.Length;
	        for (var cornerIndex = 0; cornerIndex < cornerCount; cornerIndex++)
	        {
		        // var baseIndex  = i * 3;
		        var centroid = float3.zero;
		        var tiles    = planet.GetCornerTileIndices(cornerIndex);
		        for(var tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
		        {
			        centroid += planet.TilePositions[tiles[tileIndex]];
		        }
		        centroid /= 3;
		        tiles.Dispose();
        
		        // The corner is computed as the centroid of the triangle.
		        planet.TileCorners[cornerIndex] = math.lerp(planet.TileCorners[cornerIndex], centroid, relaxationStrength);
	        }
        }
		
        #endregion
	}
}