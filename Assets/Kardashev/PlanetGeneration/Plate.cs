using Unity.Mathematics;

namespace Kardashev.PlanetGeneration
{
	public struct Plate
	{
		public int id;
		public PlateType type;                // 0 = oceanic, 1 = continental
		public float desiredElevation;
		public float3 rotationAxis; // The axis around which the plate rotates.
		public float rotationRate;  // Angular velocity
		public int seedTileIndex;   // The index of the tile from which the plate originates.
	}
}