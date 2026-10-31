using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Floor strategy: assigns a <see cref="FloorMaterial"/> to every quarter tile; shared tiling then lays whole-tile
/// slabs where a tile is uniform and quarter pieces where materials meet or wear breaks through. Purely cosmetic,
/// so it runs after furnishing on its own stream and never moves furniture.
/// </summary>
[Tool, GlobalClass]
public abstract partial class FloorStyle : Resource
{
	private static readonly int[] UprightOrientations = [0, 16, 10, 22];

	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;

	/// <summary>Every material this style may use; all must share one floor library.</summary>
	public abstract IEnumerable<FloorMaterial> Materials { get; }

	/// <summary>Fills <paramref name="quads"/> (two per tile on each axis) for every floor tile.</summary>
	protected abstract void Assign(RoomBlueprint room, FloorMaterial[,] quads, RandomNumberGenerator rng);

	public MeshLibrary Library => Materials.FirstOrDefault()?.Library;

	public virtual string[] Validate()
	{
		var materials = Materials.ToArray();
		if (materials.Length == 0 || materials.Any(material => material == null)) return [$"Floor style {Id}: assign its materials."];
		var errors = materials.SelectMany(material => material.Validate()).ToList();
		if (materials.Select(material => material.Library).Distinct().Count() > 1) errors.Add($"Floor style {Id}: materials must share one floor library.");
		return errors.ToArray();
	}

	public void Paint(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var footprint = room.Footprint;
		var quads = new FloorMaterial[footprint.Width * 2, footprint.Length * 2];
		Assign(room, quads, rng);
		var planks = Materials.Distinct().ToDictionary(material => material, _ => rng.Randf() < .5f ? 0 : 16);
		var grid = room.Room.FloorGridMap;
		foreach (var tile in footprint.Tiles)
		{
			var material = quads[tile.X * 2, tile.Y * 2];
			var wear = new string[2, 2];
			bool uniform = material.Large.Count > 0;
			for (int qx = 0; qx < 2; qx++)
			for (int qz = 0; qz < 2; qz++)
			{
				var quad = quads[tile.X * 2 + qx, tile.Y * 2 + qz];
				if (quad != material) uniform = false;
				if (quad.Wear.Count > 0 && rng.Randf() < quad.WearChance) wear[qx, qz] = Weighted.Pick(quad.Wear, rng);
				if (wear[qx, qz] != null) uniform = false;
			}
			var center = (Vector3I)RoomFootprint.TileCenter(tile);
			if (uniform)
			{
				grid.SetCellItem(center, grid.MeshLibrary.FindItemByName(Weighted.Pick(material.Large, rng)), Orientation(material));
				continue;
			}
			for (int qx = 0; qx < 2; qx++)
			for (int qz = 0; qz < 2; qz++)
			{
				var quad = quads[tile.X * 2 + qx, tile.Y * 2 + qz];
				string item = wear[qx, qz] ?? Weighted.Pick(quad.Small, rng);
				var cell = center + new Vector3I(qx == 0 ? -1 : 1, 0, qz == 0 ? -1 : 1);
				grid.SetCellItem(cell, grid.MeshLibrary.FindItemByName(item), wear[qx, qz] == null ? Orientation(quad) : UprightOrientations[rng.RandiRange(0, 3)]);
			}
		}

		int Orientation(FloorMaterial material) => material.AlignedPlanks ? planks[material] : UprightOrientations[rng.RandiRange(0, 3)];
	}
}
