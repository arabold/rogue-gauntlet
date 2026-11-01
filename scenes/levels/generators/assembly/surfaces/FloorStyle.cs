using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Floor strategy: assigns a <see cref="FloorMaterial"/> (or a transition piece) to every quarter tile; shared tiling
/// then sets <see cref="Inlays"/> into the floor and lays whole-tile slabs where a tile is uniform and quarter pieces
/// elsewhere. Every piece covers exactly its own cells, so nothing overlaps.
/// Purely cosmetic, so it runs after furnishing on its own stream and never moves furniture.
/// </summary>
[Tool, GlobalClass]
public abstract partial class FloorStyle : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	/// <summary>Realms (by ID) this appears in; none means every realm.</summary>
	[Export] public string[] Realms { get; set; } = [];
	/// <summary>Features set into the floor (drains, gutters), placed in order after the materials.</summary>
	[Export] public FloorInlay[] Inlays { get; set; } = [];
	/// <summary>Optional rug laid over the finished floor, clear of inlays.</summary>
	[Export] public FloorRug Rug { get; set; }

	/// <summary>Every material this style may use; all must share one floor library.</summary>
	public abstract IEnumerable<FloorMaterial> Materials { get; }

	/// <summary>
	/// Fills <paramref name="quads"/> (two per tile on each axis) for every floor tile, and <paramref name="splits"/>
	/// where a quarter tile is a transition piece instead.
	/// </summary>
	protected abstract void Assign(RoomBlueprint room, FloorMaterial[,] quads, FloorSplit[,] splits, RandomNumberGenerator rng);

	public MeshLibrary Library => Materials.FirstOrDefault()?.Library;

	public virtual string[] Validate()
	{
		var materials = Materials.ToArray();
		if (materials.Length == 0 || materials.Any(material => material == null)) return [$"Floor style {Id}: assign its materials."];
		var errors = materials.SelectMany(material => material.Validate()).ToList();
		// Compare by path: separately loaded copies of one library are still the same library.
		if (materials.Select(material => material.Library?.ResourcePath ?? "").Distinct().Count() > 1) errors.Add($"Floor style {Id}: materials must share one floor library.");
		if (Inlays.Any(inlay => inlay == null)) errors.Add($"Floor style {Id}: remove empty inlay slots.");
		errors.AddRange(Inlays.Where(inlay => inlay != null).SelectMany(inlay => inlay.Validate(Library)));
		if (Rug != null) errors.AddRange(Rug.Validate().Select(error => $"Floor style {Id}: {error}"));
		return errors.ToArray();
	}

	/// <summary>Whether quarter tile (<paramref name="x"/>, <paramref name="z"/>) lies on a floor tile (not a pit).</summary>
	protected static bool IsFloorQuad(RoomBlueprint room, int x, int z) => x >= 0 && z >= 0 && room.IsFloor(new(x / 2, z / 2));

	public void Paint(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var footprint = room.Footprint;
		var quads = new FloorMaterial[footprint.Width * 2, footprint.Length * 2];
		var splits = new FloorSplit[quads.GetLength(0), quads.GetLength(1)];
		Assign(room, quads, splits, rng);
		foreach (var patch in room.FloorPatches)
		for (int x = 0; x < quads.GetLength(0); x++)
		for (int z = 0; z < quads.GetLength(1); z++)
		{
			// Quarter tile q spans 2q - 2 to 2q; sample its center so all styles retain exact, non-overlapping tiling.
			if (!IsFloorQuad(room, x, z) || !patch.Area.HasPoint(new Vector2(2 * x - 1, 2 * z - 1))) continue;
			quads[x, z] = patch.Material;
			splits[x, z] = default;
		}
		var covered = new bool[quads.GetLength(0), quads.GetLength(1)];
		foreach (var inlay in Inlays) PlaceInlay(room, inlay, quads, splits, covered, rng);
		var roomTurns = Materials.Concat(room.FloorPatches.Select(patch => patch.Material)).Distinct()
			.ToDictionary(material => material, _ => rng.Randf() < .5f ? GridTurns.Turn0 : GridTurns.Turn90);
		var grid = room.Room.FloorGridMap;
		foreach (var tile in footprint.Tiles.Where(room.IsFloor))
		{
			var material = quads[tile.X * 2, tile.Y * 2];
			var wear = new string[2, 2];
			bool uniform = material.Large.Count > 0;
			for (int qx = 0; qx < 2; qx++)
			for (int qz = 0; qz < 2; qz++)
			{
				int x = tile.X * 2 + qx, z = tile.Y * 2 + qz;
				var quad = quads[x, z];
				if (quad != material || covered[x, z] || splits[x, z].Transition != null) uniform = false;
				else if (quad.Wear.Count > 0 && rng.Randf() < quad.WearChance) wear[qx, qz] = Weighted.Pick(quad.Wear, rng);
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
				int x = tile.X * 2 + qx, z = tile.Y * 2 + qz;
				if (covered[x, z]) continue;
				var quad = quads[x, z];
				var split = splits[x, z];
				var (item, orientation) = split.Transition != null ? (Weighted.Pick(split.Transition.Items, rng), split.Orientation)
					: (wear[qx, qz] ?? Weighted.Pick(quad.Small, rng), Orientation(quad));
				var cell = center + new Vector3I(qx == 0 ? -1 : 1, 0, qz == 0 ? -1 : 1);
				grid.SetCellItem(cell, grid.MeshLibrary.FindItemByName(item), orientation);
			}
		}
		Rug?.Lay(room, covered, rng);

		int Orientation(FloorMaterial material) => material.RoomWideOrientation ? roomTurns[material] : GridTurns.Upright[rng.RandiRange(0, 3)];
	}

	/// <summary>
	/// Sets an inlay into the floor up to its count: whole pieces aligned to their size (tile-aligned along even
	/// sides), on its host material, clear of the wall ring, doorway tiles, floor props, transitions and other inlays.
	/// </summary>
	private static void PlaceInlay(RoomBlueprint room, FloorInlay inlay, FloorMaterial[,] quads, FloorSplit[,] splits, bool[,] covered,
		RandomNumberGenerator rng)
	{
		if (rng.Randf() >= inlay.Chance) return;
		var grid = room.Room.FloorGridMap;
		var size = inlay.Size(grid.MeshLibrary);
		int count = rng.RandiRange(inlay.MinimumCount, inlay.MaximumCount);
		for (int index = 0; index < count; index++)
		{
			string item = Weighted.Pick(inlay.Items, rng);
			bool rolledTurn = rng.Randf() < .5f;
			int rolledRun = rng.RandiRange(inlay.MinimumRun, inlay.MaximumRun);
			// The rolled turn and run first, then shorter runs and the other turn where furniture leaves less room.
			(bool Turned, int Run, Vector2I Piece, Vector2I Step, List<Vector2I> Origins) layout = default;
			foreach (bool turned in new[] { rolledTurn, !rolledTurn })
			for (int run = rolledRun; run >= inlay.MinimumRun && layout.Origins == null; run--)
			{
				var piece = turned ? new Vector2I(size.Y, size.X) : size;
				// Runs continue along the piece's long side (its local X when square).
				var step = piece.X > piece.Y || (piece.X == piece.Y && !turned) ? new Vector2I(piece.X, 0) : new Vector2I(0, piece.Y);
				var strip = piece + step * (run - 1);
				var origins = new List<Vector2I>();
				for (int x = 0; x + strip.X <= quads.GetLength(0); x += piece.X % 2 == 0 ? 2 : 1)
				for (int z = 0; z + strip.Y <= quads.GetLength(1); z += piece.Y % 2 == 0 ? 2 : 1)
					if (Fits(new Rect2I(x, z, strip))) origins.Add(new(x, z));
				if (origins.Count > 0) layout = (turned, run, piece, step, origins);
			}
			if (layout.Origins == null) return;
			var origin = layout.Origins[rng.RandiRange(0, layout.Origins.Count - 1)];
			for (int k = 0; k < layout.Run; k++)
			{
				// Quarter tile q spans cells 2q - 2 to 2q, so a piece starting at quarter tile a is centered on 2a - 2 + size.
				var at = origin + layout.Step * k;
				int orientation = layout.Turned ? (rng.Randf() < .5f ? GridTurns.Turn90 : GridTurns.Turn270) : (rng.Randf() < .5f ? GridTurns.Turn0 : GridTurns.Turn180);
				grid.SetCellItem(new Vector3I(2 * at.X - 2 + layout.Piece.X, 0, 2 * at.Y - 2 + layout.Piece.Y), grid.MeshLibrary.FindItemByName(item), orientation);
				for (int x = at.X; x < at.X + layout.Piece.X; x++)
				for (int z = at.Y; z < at.Y + layout.Piece.Y; z++)
					covered[x, z] = true;
			}
		}

		bool Fits(Rect2I area)
		{
			for (int x = area.Position.X; x < area.End.X; x++)
			for (int z = area.Position.Y; z < area.End.Y; z++)
			{
				if (!IsFloorQuad(room, x, z) || covered[x, z] || splits[x, z].Transition != null) return false;
				if (inlay.Host != null && !FloorMaterial.Same(quads[x, z], inlay.Host)) return false;
				var tile = new Vector2I(x / 2, z / 2);
				if (room.Doorways.Any(doorway => doorway.Tile == tile)) return false;
				// Quarter tiles touching void lie under the wall's trim; inlays side by side read as one botched piece.
				for (int dx = -1; dx <= 1; dx++)
				for (int dz = -1; dz <= 1; dz++)
					if (!IsFloorQuad(room, x + dx, z + dz) || covered[x + dx, z + dz]) return false;
			}
			var rect = new Rect2(2 * area.Position.X - 2, 2 * area.Position.Y - 2, 2 * area.Size.X, 2 * area.Size.Y).Grow(-.05f);
			return !room.Pieces.Any(piece => piece.Prop.Mount == PropMount.Floor && RoomBlueprint.XZ(piece.Envelope).Intersects(rect));
		}
	}
}