using System;
using Godot;

/// <summary>
/// Corridor floors and the walls the generator builds around corridors, sealed doorways and secret pockets, drawn from
/// a floor material's whole-tile slabs and a wall style, so each realm's corridors match its rooms. Corridor floors are
/// laid unturned, so seam-matched studio floors join; corridor walls never carry pillars.
/// </summary>
[Tool]
[GlobalClass]
public partial class DungeonTileFactory : TileFactory
{
	[Export] public FloorMaterial Floor { get; set; }
	[Export] public WallStyle Walls { get; set; }

	/// <summary>Rejects broken corridor authoring before generation starts, through the level's normal failure path.</summary>
	public override void Reset()
	{
		var errors = Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
	}

	public override int GetCorridorTileIndex(RandomNumberGenerator rng) => Floor.Library.FindItemByName(Weighted.Pick(Floor.Large, rng));
	public override int GetWallTileIndex(RandomNumberGenerator rng) => Walls.Library.FindItemByName(Walls.DrawEdge(rng).Item);
	public override int GetWallHalfTileIndex(RandomNumberGenerator rng) => Walls.Library.FindItemByName(Walls.Half);
	public override int GetWallCornerTileIndex(RandomNumberGenerator rng) => Walls.Library.FindItemByName(Walls.Corner);

	public string[] Validate()
	{
		if (Floor == null || Walls == null) return [$"{ResourcePath}: assign the corridor floor material and wall style."];
		string[] slabs = Floor.Large.Count == 0 ? [$"{ResourcePath}: the corridor floor needs whole-tile slabs."] : [];
		return [.. slabs, .. Floor.Validate(), .. Walls.Validate()];
	}
}