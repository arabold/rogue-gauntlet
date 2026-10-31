using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// One floor surface (stone, light wood, dark wood, dirt…) as weighted floor-library items: whole-tile slabs,
/// quarter-tile pieces used where materials meet or wear breaks a slab, and wear accents (cracks, weeds).
/// </summary>
[Tool, GlobalClass]
public partial class FloorMaterial : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public MeshLibrary Library { get; set; }
	/// <summary>4×4 items covering a whole tile, by weight.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Large { get; set; } = [];
	/// <summary>2×2 items covering a quarter tile, by weight.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Small { get; set; } = [];
	/// <summary>Quarter-tile accents (broken, weedy, rocky), by weight.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Wear { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float WearChance { get; set; }
	/// <summary>
	/// Pieces, wear included, keep one orientation across the whole room instead of turning per tile: plank runs, and
	/// Asset Studio floors, whose patterns join only between unturned neighbours.
	/// </summary>
	[Export] public bool AlignedPlanks { get; set; }

	public string[] Validate()
	{
		if (string.IsNullOrWhiteSpace(Id) || Library == null) return [$"Floor material {Id}: assign an ID and the floor library."];
		if (Small.Count == 0) return [$"Floor material {Id}: assign quarter-tile items."];
		return CheckItems(Library, $"Floor material {Id}", Large.Keys, Vector2.One * 4)
			.Concat(CheckItems(Library, $"Floor material {Id}", Small.Keys.Concat(Wear.Keys), Vector2.One * 2)).ToArray();
	}

	/// <summary>Whether two references name one material; separately loaded copies (cache-ignoring loads) share an ID.</summary>
	public static bool Same(FloorMaterial a, FloorMaterial b) => a == b || (a != null && b != null && a.Id == b.Id);

	/// <summary>
	/// Errors for items missing from the library or not covering exactly <paramref name="size"/> (X by Z, at no turn).
	/// Floor pieces tile edge to edge, so a mis-sized piece overlaps its neighbours or leaves a gap.
	/// </summary>
	public static IEnumerable<string> CheckItems(MeshLibrary library, string owner, IEnumerable<string> items, Vector2 size)
	{
		foreach (string item in items)
		{
			int id = library.FindItemByName(item);
			if (id < 0) yield return $"{owner}: item '{item}' is not in the floor library.";
			else if (library.GetItemMesh(id) == null) yield return $"{owner}: item '{item}' has no mesh.";
			else if ((Footprint(library, id) - size).Abs() is var error && Mathf.Max(error.X, error.Y) > .05f)
				yield return $"{owner}: '{item}' covers {Footprint(library, id)}, not {size}.";
		}
	}

	/// <summary>An item's X by Z extent at no turn, measured from its mesh.</summary>
	public static Vector2 Footprint(MeshLibrary library, int item)
	{
		var box = library.GetItemMeshTransform(item) * library.GetItemMesh(item).GetAabb();
		return new Vector2(box.Size.X, box.Size.Z);
	}
}
