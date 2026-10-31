using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A feature set into the floor tiling, such as drain grates and gutter channels. Its size is measured from its
/// items, which must share one footprint of whole quarter tiles. The floor places each piece whole, aligned to the
/// quarter-tile grid, in place of the floor pieces it covers.
/// </summary>
[Tool, GlobalClass]
public partial class FloorInlay : Resource
{
	/// <summary>Room maps floor tiles by the item anchors inside each tile, so an inlay may cover at most one.</summary>
	private const int MaxQuads = 2;

	[Export] public string Id { get; set; } = "";
	/// <summary>Items sharing one footprint, by weight; a run uses one item throughout.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Items { get; set; } = [];
	/// <summary>Only on this material, e.g. grates in stone; empty allows any.</summary>
	[Export] public FloorMaterial Host { get; set; }
	[Export(PropertyHint.Range, "0,1,0.05")] public float Chance { get; set; } = 1;
	[Export(PropertyHint.Range, "0,8")] public int MinimumCount { get; set; } = 1;
	[Export(PropertyHint.Range, "0,8")] public int MaximumCount { get; set; } = 1;
	/// <summary>Pieces laid end to end along their long side, for gutters.</summary>
	[Export(PropertyHint.Range, "1,8")] public int MinimumRun { get; set; } = 1;
	[Export(PropertyHint.Range, "1,8")] public int MaximumRun { get; set; } = 1;

	/// <summary>Footprint in quarter tiles at no turn.</summary>
	public Vector2I Size(MeshLibrary library) => (Vector2I)(FloorMaterial.Footprint(library, library.FindItemByName(Items.Keys.First())) / 2).Round();

	public string[] Validate(MeshLibrary library)
	{
		if (string.IsNullOrWhiteSpace(Id) || Items.Count == 0 || library == null) return [$"Floor inlay {Id}: assign an ID and its items."];
		var errors = new List<string>();
		if (MinimumCount > MaximumCount || MinimumRun > MaximumRun) errors.Add($"Floor inlay {Id}: minimums must not exceed maximums.");
		var missing = Items.Keys.Where(item => library.FindItemByName(item) < 0).ToArray();
		if (missing.Length > 0) return [.. errors, .. missing.Select(item => $"Floor inlay {Id}: item '{item}' is not in the floor library.")];
		var size = Size(library);
		errors.AddRange(FloorMaterial.CheckItems(library, $"Floor inlay {Id}", Items.Keys, size * 2));
		if (size.X < 1 || size.Y < 1 || size.X > MaxQuads || size.Y > MaxQuads) errors.Add($"Floor inlay {Id}: items must cover one to four quarter tiles within a tile.");
		return errors.ToArray();
	}
}
