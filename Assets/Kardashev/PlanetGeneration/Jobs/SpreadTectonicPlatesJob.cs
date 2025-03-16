using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Kardashev.PlanetGeneration.Jobs
{
	[BurstCompile]
	struct SpreadTectonicPlatesJob : IJob
	{
		public uint Seed;
		[ReadOnly] public NativeArray<int> Spokes;
		[ReadOnly] public NativeArray<int> TileSpokes;
		[ReadOnly] public NativeArray<int> TileSpokeOpposites;
		[ReadOnly] public NativeArray<Plate> Plates;
			
		public NativeArray<int> TilePlates;
		[WriteOnly] public NativeArray<float> TileElevations;
			
		public void Execute()
		{
			var rnd = new Unity.Mathematics.Random(Seed);
			var frontier = new NativeList<TileNode>(Allocator.TempJob);

			AddPlateSeedsToFrontier(frontier);
				
			while (frontier.Length > 0)
			{
				var randomIndex = rnd.NextInt(0, frontier.Length);
				var current     = frontier[randomIndex];
				var plate       = Plates[current.PlateId];

				var foundUnassignedNeighbor = false;

				// Get neighbor tile indices.
				var neighbors = GetTileNeighborIndices(current.TileIndex);
				for (var i = 0; i < neighbors.Length; i++)
				{
					var neighborIndex = neighbors[i];

					if (TilePlates[neighborIndex] == -1)
					{
						TilePlates[neighborIndex]     = current.PlateId;
						TileElevations[neighborIndex] = plate.desiredElevation;
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

		private void AddPlateSeedsToFrontier(NativeList<TileNode> frontier)
		{
			for (var i = 0; i < Plates.Length; i++)
			{
				var plate         = Plates[i];
				var seedTileIndex = plate.seedTileIndex;
				frontier.Add(new TileNode(seedTileIndex, plate.id));
			}
		}
			
		private NativeArray<int> GetTileNeighborIndices(int tileIndex)
		{
			var edgeLoop  = GetTileSpokeIndices(tileIndex);
			var neighbors = new NativeArray<int>(edgeLoop.Length, Allocator.Temp);
			for (var i = 0; i < edgeLoop.Length; i++)
			{
				var nextEdge = GetNextEdgeIndex(edgeLoop[i]);
				neighbors[i] = Spokes[nextEdge];
			}
			edgeLoop.Dispose();
			return neighbors;
		}
			
		private NativeList<int> GetTileSpokeIndices(int tileIndex)
		{
			var startEdge    = TileSpokes[tileIndex];
			var currentEdge  = startEdge;
			var oppositeEdge = -1;
        
			var loop = new NativeList<int>(Allocator.Temp);
			do
			{
				loop.Add(currentEdge);
				oppositeEdge = TileSpokeOpposites[currentEdge];
				currentEdge  = GetNextEdgeIndex(oppositeEdge);
			} while(oppositeEdge >= 0 && currentEdge != startEdge);
        
			return loop;
		}
			
		private static int GetNextEdgeIndex(int edgeIndex)
		{
			return edgeIndex % 3 == 2 
				? edgeIndex - 2 
				: edgeIndex + 1;
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