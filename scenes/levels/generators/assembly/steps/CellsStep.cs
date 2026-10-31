using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A row of barred cells along one long wall, optionally mirrored on the opposite wall: shared partitions, a front
/// with an operable gate per cell, and <see cref="FurnishingStep.Content"/> (bedding) against each cell's back wall.
/// Gates are doors, so cells count as reachable through them; their swing stays clear of furniture.
/// Counts are cells; the row is centered and keeps the run's ends free for the room's entrances.
/// </summary>
[Tool, GlobalClass]
public partial class CellsStep : FurnishingStep
{
	private readonly Dictionary<string, Prop> _structure = [];

	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public string FrontWall { get; set; } = "wall_gated";
	/// <summary>Partition pieces, alternating along the row; short sections use the front piece.</summary>
	[Export] public string[] PartitionWalls { get; set; } = ["wall_gated", "wall_archedwindow_gated"];
	/// <summary>Operable gate: a door scene with an interior-door capability.</summary>
	[Export] public Prop Gate { get; set; }
	/// <summary>Cell width along the wall (X) and depth into the room (Y), in world units.</summary>
	[Export] public Vector2 CellSize { get; set; } = new(6, 7);
	[Export(PropertyHint.Range, "2.1,4,0.1")] public float OpeningWidth { get; set; } = 2.4f;
	/// <summary>Free floor kept at both ends of a row.</summary>
	[Export(PropertyHint.Range, "0,8,0.1")] public float EndClearance { get; set; } = 4;
	/// <summary>Chance of a second row on the opposite wall, when the aisle stays wide enough.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float OpposingChance { get; set; } = .5f;
	[Export(PropertyHint.Range, "2,8,0.1")] public float MinimumAisle { get; set; } = 4;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var runs = room.WallRuns.Where(run => run.Outward.X != 0).OrderByDescending(run => run.Length).ThenBy(run => run.Outward.X).ToList();
		if (runs.Count == 0) return 0;
		var first = runs[rng.RandiRange(0, runs.Count - 1)];
		var rows = new List<WallRun> { first };
		bool opposing = rng.Randf() < OpposingChance;
		var opposite = runs.FirstOrDefault(run => run.Outward == -first.Outward);
		// Free width between the walls' faces and the cell fronts (their bars are half a unit thick).
		float Aisle(int rowCount) => room.Interior.Size.X - 1 - rowCount * (CellSize.Y + .5f);
		if (Aisle(1) < MinimumAisle) return 0;
		if (opposing && opposite != default && Aisle(2) >= MinimumAisle) rows.Add(opposite);
		int count = 0;
		foreach (var run in rows)
		{
			int cells = Mathf.Min(MaximumCount - count, Mathf.FloorToInt((run.LengthUnits - 2 * EndClearance) / CellSize.X));
			if (cells < 1) continue;
			if (!TryPlaceRow(room, run, cells, rng)) continue;
			count += cells;
		}
		return count;
	}

	private bool TryPlaceRow(RoomBlueprint room, WallRun run, int cells, RandomNumberGenerator rng)
	{
		var frame = room.RunFrame(run);
		float width = CellSize.X, depth = CellSize.Y, jamb = (width - OpeningWidth) / 2;
		float left = -cells * width / 2;
		var plan = new PlacementPlan();
		int phase = rng.RandiRange(0, PartitionWalls.Length - 1);
		for (int divider = 0; divider <= cells; divider++)
		{
			// Partitions stop at the front wall's band so the two meet without overlapping.
			float x = left + divider * width;
			AddRun(plan, new Vector3(x, 0, 0), new Vector3(x, 0, depth - .5f), PartitionWalls[(phase + divider) % PartitionWalls.Length]);
		}
		var gates = new List<Transform3D>();
		for (int cell = 0; cell < cells; cell++)
		{
			float x = left + cell * width;
			AddRun(plan, new Vector3(x, 0, depth), new Vector3(x + jamb, 0, depth), FrontWall);
			AddRun(plan, new Vector3(x + width - jamb, 0, depth), new Vector3(x + width, 0, depth), FrontWall);
			var gate = new Transform3D(Basis.Identity, new Vector3(x + width / 2, 0, depth));
			plan.Add(Gate, 0, gate, optional: false);
			gates.Add(frame * gate);
		}
		if (!room.TryPlace(plan, frame)) return false;
		foreach (var gate in gates)
		{
			// The gate swings over both approaches; keep it and the doorway in front clear.
			var clearance = new Aabb(-OpeningWidth / 2 - .1f, 0, -OpeningWidth - .15f, OpeningWidth + .2f, 2.5f, OpeningWidth + 2.15f);
			room.Reserve(RoomBlueprint.XZ(gate * clearance));
		}
		for (int cell = 0; cell < cells; cell++)
		{
			var content = Next(room, rng, out var choice);
			if (content.IsEmpty) continue;
			var center = frame * new Vector3(left + (cell + .5f) * width, 0, 0);
			TryPlaceAgainstWall(room, choice, content, run, RoomBlueprint.AlongRun(run, center) - room.WidthAlong(content, run) / 2);
		}
		return true;
	}

	/// <summary>Straight run of full wall modules, stretching the last piece to fit; local to the row frame.</summary>
	private void AddRun(PlacementPlan plan, Vector3 start, Vector3 end, string item)
	{
		var delta = end - start;
		float length = delta.Length();
		if (length < .01f) return;
		var direction = delta / length;
		var rotation = new Basis(Vector3.Up, Mathf.Atan2(-direction.Z, direction.X));
		for (float offset = 0; offset < length - .001f;)
		{
			float size = Mathf.Min(Room.TileSize, length - offset);
			string name = size < Room.TileSize - .1f ? FrontWall : item;
			var basis = rotation * Basis.FromScale(new Vector3(size / Room.TileSize, 1, 1));
			plan.Add(Structure(name), 0, new Transform3D(basis, start + direction * (offset + size / 2)), optional: false);
			offset += size;
		}
	}

	/// <summary>Runtime props for structural pieces; shared definitions, so they are cached on this step.</summary>
	private Prop Structure(string item)
	{
		if (_structure.TryGetValue(item, out var prop)) return prop;
		prop = new Prop { Id = $"structure.{item}", Library = WallLibrary, Items = [item], Gap = 0 };
		_structure[item] = prop;
		return prop;
	}

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		if (WallLibrary == null || PartitionWalls.Length == 0 || PartitionWalls.Append(FrontWall).Any(item => WallLibrary.FindItemByName(item) < 0))
			errors.Add($"Step {Id}: assign the wall library and existing front/partition pieces.");
		if (Gate == null || Gate.Scenes.Length == 0 || Gate.Blocking) errors.Add($"Step {Id}: assign a non-blocking gate prop with its door scene.");
		if (CellSize.X - OpeningWidth < 1 || CellSize.Y < 4) errors.Add($"Step {Id}: cells must fit the opening, its jambs and a bed.");
		return errors.ToArray();
	}
}
