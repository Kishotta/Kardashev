using Unity.Mathematics;

namespace Kardashev.PlanetGeneration
{
	public static class Icosahedron
	{
		private static readonly float GoldenRatio = (1f + math.sqrt(5f)) / 2f;

		public static float3[] Vertices =>
			new[]
			{
				new float3(-1, GoldenRatio, 0),
				new float3(1, GoldenRatio, 0),
				new float3(-1, -GoldenRatio, 0),
				new float3(1, -GoldenRatio, 0),
				new float3(0, -1, GoldenRatio),
				new float3(0, 1, GoldenRatio),
				new float3(0, -1, -GoldenRatio),
				new float3(0, 1, -GoldenRatio),
				new float3(GoldenRatio, 0, -1),
				new float3(GoldenRatio, 0, 1),
				new float3(-GoldenRatio, 0, -1),
				new float3(-GoldenRatio, 0, 1)
			};

		public static int3[] Faces =>
			new[]
			{
				new int3(0, 11, 5),
				new int3(0, 5, 1),
				new int3(0, 1, 7),
				new int3(0, 7, 10),
				new int3(0, 10, 11),
				new int3(1, 5, 9),
				new int3(5, 11, 4),
				new int3(11, 10, 2),
				new int3(10, 7, 6),
				new int3(7, 1, 8),
				new int3(3, 9, 4),
				new int3(3, 4, 2),
				new int3(3, 2, 6),
				new int3(3, 6, 8),
				new int3(3, 8, 9),
				new int3(4, 9, 5),
				new int3(2, 4, 11),
				new int3(6, 2, 10),
				new int3(8, 6, 7),
				new int3(9, 8, 1)
			};
	}
}