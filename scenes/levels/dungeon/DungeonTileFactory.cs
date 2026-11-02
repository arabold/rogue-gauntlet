using System;
using System.Linq;
using Godot;

/// <summary>
/// Corridor floors and their wall family, drawn from
/// a floor material's whole-tile slabs and a wall style, so each realm's corridors match its rooms. Corridor floors are
/// laid unturned, so seam-matched studio floors join; corridor walls never carry pillars. Optional <see cref="Steps"/>
/// decorate corridor walls and straight spans after boundaries are placed.
/// </summary>
[Tool]
[GlobalClass]
public partial class DungeonTileFactory : Resource
{
	[Export] public FloorMaterial Floor { get; set; }
	[Export] public WallStyle Walls { get; set; }
	[Export] public CorridorStep[] Steps { get; set; } = [];

	/// <summary>Rejects broken corridor authoring before generation starts, through the level's normal failure path.</summary>
	public void ValidateOrThrow()
	{
		var errors = Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
	}

	public int GetCorridorTileIndex(RandomNumberGenerator rng) => Floor.Library.FindItemByName(Weighted.Pick(Floor.Large, rng));

	public string[] Validate()
	{
		if (Floor == null || Walls == null) return [$"{ResourcePath}: assign the corridor floor material and wall style."];
		string[] slabs = Floor.Large.Count == 0 ? [$"{ResourcePath}: the corridor floor needs whole-tile slabs."] : [];
		string[] steps = (Steps ?? []).SelectMany(step => step == null ? [$"{ResourcePath}: corridor step is null."] : step.Validate()).ToArray();
		return [.. slabs, .. Floor.Validate(), .. Walls.Validate(), .. steps];
	}
}