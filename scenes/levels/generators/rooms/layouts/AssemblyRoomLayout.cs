using Godot;
using System.Linq;

/// <summary>Fill wall runs with reusable variable assemblies, trying real wall ends/corners before interior positions.</summary>
[Tool, GlobalClass]
public partial class AssemblyRoomLayout : RoomContentLayout
{
	[Export] public RoomAssemblyDefinition Assembly { get; set; }
	[Export(PropertyHint.Range, "0,8")] public int MinimumCount { get; set; } = 1;
	[Export(PropertyHint.Range, "1,8")] public int MaximumCountPerRoom { get; set; } = 2;
	[Export(PropertyHint.Range, "0,2,0.1")] public float EndClearance { get; set; } = .75f;
	public override RoomArrangementDefinition[] Definitions => Assembly?.Definitions ?? [];
	public override int MaximumCount => MaximumCountPerRoom * (Assembly?.MaximumPieces ?? 0);
	public override string[] Validate() => (Assembly?.Validate() ?? ["Assembly layout: assign an assembly."])
		.Concat(MinimumCount < 0 || MaximumCountPerRoom < MinimumCount || MaximumCountPerRoom < 1 || MaximumCountPerRoom > 8
			|| !float.IsFinite(EndClearance) || EndClearance < 0 || EndClearance > 2 ? ["Assembly layout: assign bounded counts and wall-end clearance."] : System.Array.Empty<string>()).ToArray();

	public override bool Populate(RoomFurnishingContext context, string key)
	{
		int count = 0;
		using var rng = context.Random(key);
		foreach (var wall in context.WallSpans().Select(wall => (Wall: wall, Order: rng.Randi())).OrderBy(entry => entry.Order).Select(entry => entry.Wall))
		{
			// Sample both ends first; furnishings usually accumulate near wall ends and corners.
			float length = wall.LengthTiles * Room.TileSize;
			for (int step = 0; step < length * 2 && count < MaximumCountPerRoom; step++)
			{
				float offset = step % 2 == 0 ? EndClearance + step / 2 * .5f : length - EndClearance - step / 2 * .5f;
				if (offset < EndClearance || offset > length - EndClearance) continue;
				var basis = new Basis(Vector3.Up, RoomFurnishingPatterns.Turn(wall.Outward) * Mathf.Pi / 2);
				var along = new Vector3(wall.Along.X, 0, wall.Along.Y);
				var origin = context.Center(wall.Start) + along * (offset - Room.TileSize / 2f);
				if (RoomFurnishingPatterns.PlaceAssembly(context, Assembly, new(basis, origin), $"{key}:wall:{wall.Start}:{wall.Outward}:{step}")) count++;
			}
			if (count >= MaximumCountPerRoom) break;
		}
		return count >= MinimumCount;
	}
}