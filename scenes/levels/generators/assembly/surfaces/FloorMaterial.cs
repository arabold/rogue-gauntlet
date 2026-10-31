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
	/// <summary>Planks run one way across the whole room instead of turning per tile.</summary>
	[Export] public bool AlignedPlanks { get; set; }

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || Library == null) errors.Add($"Floor material {Id}: assign an ID and the floor library.");
		else if (Small.Count == 0) errors.Add($"Floor material {Id}: assign quarter-tile items.");
		else foreach (var item in Large.Keys.Concat(Small.Keys).Concat(Wear.Keys).Where(item => Library.FindItemByName(item) < 0))
			errors.Add($"Floor material {Id}: item '{item}' is not in the floor library.");
		return errors.ToArray();
	}
}
