using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Two materials on either side of one straight boundary across the room: dirt reaching into a stone room, a worn
/// half. The boundary is either a seam along the room's axes, on a tile edge so slabs stay whole, or a 45° diagonal
/// laid entirely from <see cref="Transition"/> pieces. Materials never mix in scattered patches, which read as
/// stray squares and triangles.
/// </summary>
[Tool, GlobalClass]
public partial class SplitFloor : FloorStyle
{
	private static readonly Vector2I[] Axes = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
	private static readonly Vector2I[] Diagonals = [new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];

	[Export] public FloorMaterial Base { get; set; }
	[Export] public FloorMaterial Other { get; set; }
	/// <summary>Diagonal pieces between the two materials; without one the boundary always follows the room's axes.</summary>
	[Export] public FloorTransition Transition { get; set; }
	[Export(PropertyHint.Range, "0,1,0.05")] public float DiagonalChance { get; set; } = .5f;
	/// <summary>Share of the floor on the <see cref="Other"/> side, rolled between these.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float MinimumShare { get; set; } = .25f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float MaximumShare { get; set; } = .5f;

	public override IEnumerable<FloorMaterial> Materials => [Base, Other];

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		if (MinimumShare > MaximumShare) errors.Add($"Floor style {Id}: the minimum share must not exceed the maximum.");
		if (Transition != null && !Transition.Joins(Base, Other)) errors.Add($"Floor style {Id}: the transition must join {Base?.Id} and {Other?.Id}.");
		if (Transition != null) errors.AddRange(Transition.Validate());
		return errors.ToArray();
	}

	protected override void Assign(RoomBlueprint room, FloorMaterial[,] quads, FloorSplit[,] splits, RandomNumberGenerator rng)
	{
		bool diagonal = Transition != null && rng.Randf() < DiagonalChance;
		var toward = diagonal ? Diagonals[rng.RandiRange(0, 3)] : Axes[rng.RandiRange(0, 3)];
		float share = (float)rng.RandfRange(MinimumShare, MaximumShare);
		var floor = new List<int>();
		for (int x = 0; x < quads.GetLength(0); x++)
		for (int z = 0; z < quads.GetLength(1); z++)
			if (IsFloorQuad(room, x, z)) floor.Add(Along(x, z));
		if (floor.Count == 0) return;
		floor.Sort();
		// Quarter tiles at or past the boundary take the other material, which leaves about `share` of the floor to it.
		int boundary = floor[Mathf.Clamp((int)((1 - share) * floor.Count), 0, floor.Count - 1)];
		// On an axis, shift the seam onto a tile edge: between quarter tiles 2k - 1 and 2k.
		if (!diagonal && Mathf.PosMod(boundary, 2) != (toward.X + toward.Y > 0 ? 0 : 1)) boundary++;
		for (int x = 0; x < quads.GetLength(0); x++)
		for (int z = 0; z < quads.GetLength(1); z++)
		{
			int along = Along(x, z);
			quads[x, z] = along >= boundary ? Other : Base;
			// The diagonal runs through the boundary row's corners: base in the corner facing away from `toward`.
			if (diagonal && along == boundary) splits[x, z] = new FloorSplit(Transition, Transition.Turn(Base, -toward));
		}

		int Along(int x, int z) => toward.X * x + toward.Y * z;
	}
}
