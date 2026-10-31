using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Corridor floors and the walls the generator builds around corridors, sealed doorways and secret pockets, as
/// weighted items of the dungeon floor and wall libraries, so each realm gives its corridors its own look.
/// Corridor floors are laid unturned, so seam-matched studio floors join.
/// </summary>
[Tool]
[GlobalClass]
public partial class DungeonTileFactory : TileFactory
{
	[Export] public MeshLibrary FloorLibrary { get; set; }
	/// <summary>Whole-tile (4×4) corridor floors by weight.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Floors { get; set; } = new() { ["floor_dirt_large"] = 1, ["floor_dirt_large_rocky"] = 1 };
	[Export] public MeshLibrary WallLibrary { get; set; }
	/// <summary>Straight wall pieces by weight; both faces show, so use pieces dressed on both sides.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Walls { get; set; } = new() { ["wall"] = 4, ["wall_cracked"] = 1, ["wall_pillar"] = 1 };
	[Export] public string WallHalf { get; set; } = "wall_half";
	[Export] public string WallCorner { get; set; } = "wall_corner";

	public override int GetCorridorTileIndex(RandomNumberGenerator rng) => FloorLibrary.FindItemByName(Weighted.Pick(Floors, rng));
	public override int GetWallTileIndex(RandomNumberGenerator rng) => WallLibrary.FindItemByName(Weighted.Pick(Walls, rng));
	public override int GetWallHalfTileIndex(RandomNumberGenerator rng) => WallLibrary.FindItemByName(WallHalf);
	public override int GetWallCornerTileIndex(RandomNumberGenerator rng) => WallLibrary.FindItemByName(WallCorner);

	public string[] Validate()
	{
		if (FloorLibrary == null || WallLibrary == null || Floors.Count == 0 || Walls.Count == 0)
			return [$"{ResourcePath}: assign the floor and wall libraries and their items."];
		var errors = new List<string>();
		errors.AddRange(FloorMaterial.CheckItems(FloorLibrary, ResourcePath, Floors.Keys, Vector2.One * 4));
		errors.AddRange(Walls.Keys.Append(WallHalf).Append(WallCorner).Where(name => WallLibrary.FindItemByName(name) < 0)
			.Select(name => $"{ResourcePath}: '{name}' is not in the wall library."));
		return errors.ToArray();
	}
}
