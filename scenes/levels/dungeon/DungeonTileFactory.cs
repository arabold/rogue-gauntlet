using System.Linq;
using Godot;

/// <summary>
/// Corridor floors and the walls the generator builds around corridors, sealed doorways and secret pockets, drawn from
/// a floor material's whole-tile slabs and a wall style, so each realm's corridors match its rooms. Corridor floors are
/// laid unturned, so seam-matched studio floors join; corridor walls never carry pillars. Optional <see cref="Steps"/>
/// decorate corridor walls and straight spans after corridor and secret-branch walls are placed.
/// </summary>
[Tool]
[GlobalClass]
public partial class DungeonTileFactory : TileFactory
{
	[Export] public FloorMaterial Floor { get; set; }
	[Export] public WallStyle Walls { get; set; }
	[Export] public CorridorStep[] Steps { get; set; } = [];

	public override int GetCorridorTileIndex(RandomNumberGenerator rng) => Floor.Library.FindItemByName(Weighted.Pick(Floor.Large, rng));
	public override int GetWallTileIndex(RandomNumberGenerator rng) => Walls.Library.FindItemByName(Walls.DrawEdge(rng).Item);
	public override int GetWallHalfTileIndex(RandomNumberGenerator rng) => Walls.Library.FindItemByName(Walls.Half);
	public override int GetWallCornerTileIndex(RandomNumberGenerator rng) => Walls.Library.FindItemByName(Walls.Corner);

	public string[] Validate()
	{
		if (Floor == null || Walls == null) return [$"{ResourcePath}: assign the corridor floor material and wall style."];
		string[] slabs = Floor.Large.Count == 0 ? [$"{ResourcePath}: the corridor floor needs whole-tile slabs."] : [];
		string[] steps = (Steps ?? []).SelectMany(step => step == null ? [$"{ResourcePath}: corridor step is null."] : step.Validate()).ToArray();
		return [.. slabs, .. Floor.Validate(), .. Walls.Validate(), .. steps];
	}
}
