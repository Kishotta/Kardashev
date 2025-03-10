using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;

namespace Kardashev
{
	public enum PlanetSize
	{
		Miniscule = 1,
		Tiny = 2,
		Small = 3,
		Medium = 4,
		Large = 5,
		Huge = 6,
		Gargantuan = 7,
		Colossal = 8,
	}
	
	public class IcosahedronPlanet : MonoBehaviour
	{
		[Header("Settings")] 
		public PlanetSize size = PlanetSize.Medium;
		private float _radius = 1f;

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
		public UnityEvent<PlanetMap> onMapCreated;
		
		private void Awake()
		{
			CreateIcosahedronAndSubdividePlanetMap();
		}

		private void CreateIcosahedronAndSubdividePlanetMap()
		{
			_radius = CalculatePlanetRadius();
			var planetMap = CreateIcosahedronPlanetMap();
			
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

		private float CalculatePlanetRadius()
		{
			var frequency     = math.pow(2, (int)size);
			var totalVertices = 10 * frequency * frequency + 2;
			var radius        = math.sqrt(totalVertices / 4 * math.PI);

			return radius;
		}

		private PlanetMap CreateIcosahedronPlanetMap()
		{
			var planetMap = new PlanetMap(Allocator.Persistent);
            
			// Golden ratio.
			var phi = (1f + math.sqrt(5f)) / 2f;

			// Define 12 vertices for an icosahedron (not yet normalized).
			var vertices = new[] {
				new float3(-1,  phi,  0),
				new float3( 1,  phi,  0),
				new float3(-1, -phi,  0),
				new float3( 1, -phi,  0),
				new float3( 0, -1,  phi),
				new float3( 0,  1,  phi),
				new float3( 0, -1, -phi),
				new float3( 0,  1, -phi),
				new float3( phi,  0, -1),
				new float3( phi,  0,  1),
				new float3(-phi,  0, -1),
				new float3(-phi,  0,  1)
			};
			
			// Normalize each vertex and scale it to the sphereRadius.
			foreach (var vertex in vertices)
			{
				planetMap.AddTileCenter(math.normalize(vertex) * _radius);
			}
			
			// The 20 triangular faces of an icosahedron, defined by vertex indices.
			var faces = new [,] {
				{ 0, 11, 5 },
				{ 0, 5, 1 },
				{ 0, 1, 7 },
				{ 0, 7, 10 },
				{ 0, 10, 11 },
				{ 1, 5, 9 },
				{ 5, 11, 4 },
				{ 11, 10, 2 },
				{ 10, 7, 6 },
				{ 7, 1, 8 },
				{ 3, 9, 4 },
				{ 3, 4, 2 },
				{ 3, 2, 6 },
				{ 3, 6, 8 },
				{ 3, 8, 9 },
				{ 4, 9, 5 },
				{ 2, 4, 11 },
				{ 6, 2, 10 },
				{ 8, 6, 7 },
				{ 9, 8, 1 }
			};
			
			// Add each face to the half-edge mesh.
			var edgeLookupTable = new NativeHashMap<(int, int), int>(0, Allocator.Temp);
			for (var i = 0; i < 20; i++)
			{
				planetMap.AddTileCorner(faces[i, 0], faces[i, 1], faces[i, 2], edgeLookupTable);
			}

			edgeLookupTable.Dispose();
			
			return planetMap;
		}
		
		#region Subdivision

		private static void Subdivide(PlanetMap planetMap)
        {
            var tempMap         = new PlanetMap(Allocator.Temp);
            var edgeLookupTable = new NativeHashMap<(int, int), int>(0, Allocator.Temp);
            
            // Add all existing vertices to the temp map
            for (var i = 0; i < planetMap.TilePositions.Length; i++)
            {
                tempMap.AddTileCenter(planetMap.TilePositions[i]);
            }

            // Cache midpoints
            var midpointLookup = new NativeHashMap<(int, int), int>(planetMap.Spokes.Length, Allocator.Temp);

            var cornerCount = planetMap.TileCorners.Length;
            for (var corner = 0; corner < cornerCount; corner++)
            {
                var baseEdge = corner * 3;
                
                // Get the three tile indices of the current corner.
                var tileA = planetMap.Spokes[baseEdge];
                var tileB = planetMap.Spokes[baseEdge + 1];
                var tileC = planetMap.Spokes[baseEdge + 2];
                
                // Compute (or reuse) midpoints for each edge.
                var abMidpoint = GetMidpointCornerIndex(tempMap, midpointLookup, tileA, tileB);
                var bcMidpoint = GetMidpointCornerIndex(tempMap, midpointLookup, tileB, tileC);
                var caMidpoint = GetMidpointCornerIndex(tempMap, midpointLookup, tileC, tileA);
                
                // Create four new corners to replace the original corner.
                tempMap.AddTileCorner(tileA, abMidpoint, caMidpoint, edgeLookupTable);
                tempMap.AddTileCorner(tileB, bcMidpoint, abMidpoint, edgeLookupTable);
                tempMap.AddTileCorner(tileC, caMidpoint, bcMidpoint, edgeLookupTable);
                tempMap.AddTileCorner(abMidpoint, bcMidpoint, caMidpoint, edgeLookupTable);
            }
            midpointLookup.Dispose();
            edgeLookupTable.Dispose();
            
            planetMap.Spokes.CopyFrom(tempMap.Spokes);
            planetMap.TileSpokeOpposites.CopyFrom(tempMap.TileSpokeOpposites);
            planetMap.TileSpokes.CopyFrom(tempMap.TileSpokes);
            planetMap.TilePositions.CopyFrom(tempMap.TilePositions);
            planetMap.TileCorners.CopyFrom(tempMap.TileCorners);
            planetMap.TilePlates.CopyFrom(tempMap.TilePlates);
            planetMap.TileVelocities.CopyFrom(tempMap.TileVelocities);
            planetMap.SpokePressures.CopyFrom(tempMap.SpokePressures);
            planetMap.SpokeShears.CopyFrom(tempMap.SpokeShears);
            planetMap.TileElevations.CopyFrom(tempMap.TileElevations);
            
            tempMap.Dispose();
        }

        private static int GetMidpointCornerIndex(PlanetMap tempMap, NativeHashMap<(int, int), int> midpointLookup, int tileAIndex, int tileBIndex)
        {
            // Order the indices to ensure the key is consistent.
            var key = (math.min(tileAIndex, tileBIndex), math.max(tileAIndex, tileBIndex));
            if (midpointLookup.TryGetValue(key, out var midpointIndex))
            {
                return midpointIndex;
            }
            
            // Compute the midpoint position.
            var midpointPosition = (tempMap.TilePositions[tileAIndex] + tempMap.TilePositions[tileBIndex]) / 2;
            
            // Optionally, project the midpoint back onto the sphere.
            midpointPosition = math.normalize(midpointPosition) * math.length(tempMap.TilePositions[tileAIndex]);
            
            // Add the midpoint as a new corner in the subdivided map.
            midpointIndex = tempMap.AddTileCenter(midpointPosition);
            midpointLookup[key] = midpointIndex;
            return midpointIndex;
        }
        
        #endregion
        
        #region Edge Flip

        private void FlipRandomEdge(PlanetMap planetMap)
        {
	        var edgeIndex = UnityEngine.Random.Range(0, planetMap.Spokes.Length);
	        if (CanFlipEdge(planetMap, edgeIndex))
	        {
		        FlipEdge(planetMap, edgeIndex);
	        }
        }

        private bool CanFlipEdge(PlanetMap planetMap, int edgeIndex)
        {
	        var edgeOppositeIndex = planetMap.TileSpokeOpposites[edgeIndex];
	        if(edgeOppositeIndex == -1)
	        {
		        return false;
	        }
	        
	        var tileAIndex = planetMap.Spokes[edgeIndex];
	        var tileBIndex = planetMap.Spokes[edgeOppositeIndex];
	        var tileCIndex = planetMap.Spokes[PlanetMap.GetPreviousEdgeIndex(edgeIndex)];
	        var tileDIndex = planetMap.Spokes[PlanetMap.GetPreviousEdgeIndex(edgeOppositeIndex)];
	        
	        var tileASpokeIndices = planetMap.GetTileSpokeIndices(tileAIndex);
	        if (tileASpokeIndices.Length <= minimumNeighborCount)
	        {
		        tileASpokeIndices.Dispose();
		        return false;
	        }
	        
	        var tileBSpokeIndices = planetMap.GetTileSpokeIndices(tileBIndex);
	        if (tileBSpokeIndices.Length <= minimumNeighborCount)
	        {
		        tileBSpokeIndices.Dispose();
		        return false;
	        }
	        
	        var tileCSpokeIndices = planetMap.GetTileSpokeIndices(tileCIndex);
	        if (tileCSpokeIndices.Length >= maximumNeighborCount)
	        {
		        tileCSpokeIndices.Dispose();
		        return false;
	        }
	        
	        var tileDSpokeIndices = planetMap.GetTileSpokeIndices(tileDIndex);
	        if (tileDSpokeIndices.Length >= maximumNeighborCount)
	        {
		        tileDSpokeIndices.Dispose();
		        return false;
	        }
	        
	        var originalLength = math.distance(planetMap.TilePositions[tileAIndex], planetMap.TilePositions[tileBIndex]);
	        var flippedLength = math.distance(planetMap.TilePositions[tileCIndex], planetMap.TilePositions[tileDIndex]);
	        var lengthDifferenceRatio = (flippedLength - originalLength) / ((originalLength + flippedLength) / 2);
	        if (lengthDifferenceRatio > maxEdgeLengthDifferenceRatio)
	        {
		        return false;
	        }
	        
	        if (HasObtuseAngle(planetMap, edgeIndex))
	        {
		        return false;
	        }

	        return true;
        }
        
        private bool HasObtuseAngle(PlanetMap map, int edgeIndex)
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
        private float GetAngleAtSharedEdge(PlanetMap map, int edgeIndex, int side)
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
	        var tileBIndex = map.Spokes[PlanetMap.GetNextEdgeIndex(edgeIndex)];
	        var tileCIndex = map.Spokes[PlanetMap.GetPreviousEdgeIndex(edgeIndex)];
    
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

        private void FlipEdge(PlanetMap planetMap, int edgeIndex)
        {
	        // Get all pre-flipped indices.
	        var abIndex = edgeIndex;
	        var bcIndex = PlanetMap.GetNextEdgeIndex(abIndex);
	        var cbIndex = planetMap.TileSpokeOpposites[bcIndex];
	        var caIndex = PlanetMap.GetPreviousEdgeIndex(abIndex);
	        var acIndex = planetMap.TileSpokeOpposites[caIndex];
	        
	        var baIndex = planetMap.TileSpokeOpposites[abIndex];
	        var adIndex = PlanetMap.GetNextEdgeIndex(baIndex);
	        var daIndex = planetMap.TileSpokeOpposites[adIndex];
	        var dbIndex = PlanetMap.GetPreviousEdgeIndex(baIndex);
	        var bdIndex = planetMap.TileSpokeOpposites[dbIndex];
	        
	        var aIndex = planetMap.Spokes[abIndex];
	        var bIndex = planetMap.Spokes[bcIndex];
	        var cIndex = planetMap.Spokes[caIndex];
	        var dIndex = planetMap.Spokes[daIndex];
	        
	        var abcCornerIndex = PlanetMap.GetCornerIndex(abIndex);
	        var abdCornerIndex = PlanetMap.GetCornerIndex(baIndex);

	        // Reassign Triangles
	        planetMap.Spokes[abIndex] = dIndex;
	        planetMap.Spokes[bcIndex] = cIndex;
	        planetMap.Spokes[caIndex] = aIndex;
	        
	        planetMap.Spokes[baIndex] = cIndex;
	        planetMap.Spokes[adIndex] = dIndex;
	        planetMap.Spokes[dbIndex] = bIndex;
	        
	        planetMap.TileCorners[abcCornerIndex] = (planetMap.TilePositions[aIndex] + planetMap.TilePositions[cIndex] + planetMap.TilePositions[dIndex]) / 3;
	        planetMap.TileCorners[abdCornerIndex] = (planetMap.TilePositions[bIndex] + planetMap.TilePositions[cIndex] + planetMap.TilePositions[dIndex]) / 3;

	        planetMap.TileSpokes[aIndex] = caIndex;
	        planetMap.TileSpokes[bIndex] = dbIndex;
	        planetMap.TileSpokes[cIndex] = bcIndex;
	        planetMap.TileSpokes[dIndex] = adIndex;
	        
	        // Reassign Edge Opposites.
	        planetMap.TileSpokeOpposites[caIndex] = daIndex;
	        planetMap.TileSpokeOpposites[daIndex] = caIndex;
	        
	        planetMap.TileSpokeOpposites[adIndex] = bdIndex;
	        planetMap.TileSpokeOpposites[bdIndex]   = adIndex;
	        
	        planetMap.TileSpokeOpposites[dbIndex] = cbIndex;
	        planetMap.TileSpokeOpposites[cbIndex]   = dbIndex;
	        
	        planetMap.TileSpokeOpposites[bcIndex] = acIndex;
	        planetMap.TileSpokeOpposites[acIndex]   = bcIndex;

        }
        
        #endregion
        
        #region Vertex Relaxation
        
        private void RelaxVertices(PlanetMap planetMap)
        {
	        // Create a temporary copy of the vertex positions.
	        var newTilePositions = new NativeArray<float3>(planetMap.TilePositions.Length, Allocator.Temp);
	        for (var tileIndex = 0; tileIndex < planetMap.TilePositions.Length; tileIndex++)
	        {
		        // Get the neighboring vertices (using your connectivity method).
		        var neighbors = planetMap.GetTileNeighborIndices(tileIndex);
		        var centroid  = float3.zero;
		        for (var neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
		        {
			        centroid += planetMap.TilePositions[neighbors[neighborIndex]];
		        }
		        centroid /= neighbors.Length;
		        neighbors.Dispose();

		        // Reproject onto the sphere.
		        newTilePositions[tileIndex] = math.normalize(centroid) * _radius;
	        }
	        // Update the map with the new positions.
	        for (var i = 0; i < planetMap.TilePositions.Length; i++)
	        {
		        planetMap.TilePositions[i] = math.lerp(planetMap.TilePositions[i], newTilePositions[i], relaxationStrength);
	        }
	        newTilePositions.Dispose();
	        
	        
	        UpdateTileCorners(planetMap);
        }
        
        private void UpdateTileCorners(PlanetMap planetMap)
        {
	        // Each triangle (or face) is stored in 3 consecutive entries in Tiles.
	        var cornerCount = planetMap.TileCorners.Length;
	        for (var cornerIndex = 0; cornerIndex < cornerCount; cornerIndex++)
	        {
		        // var baseIndex  = i * 3;
		        var centroid = float3.zero;
		        var tiles    = planetMap.GetCornerTileIndices(cornerIndex);
		        for(var tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
		        {
			        centroid += planetMap.TilePositions[tiles[tileIndex]];
		        }
		        centroid /= 3;
		        tiles.Dispose();
        
		        // The corner is computed as the centroid of the triangle.
		        planetMap.TileCorners[cornerIndex] = math.lerp(planetMap.TileCorners[cornerIndex], centroid, relaxationStrength);
	        }
        }
		
        #endregion
	}
}