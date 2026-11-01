using System.Collections.Generic;
using System.Linq;
using Godot;

public enum CellRowSide { Seeded, Left, Right }

/// <summary>
/// A row of barred cells along one long wall, optionally mirrored on the opposite wall: shared partitions, a front
/// with an operable gate per cell, corner and junction pieces where the bars meet, and
/// <see cref="FurnishingStep.Content"/> (bedding) against each cell's back wall. Gates are doors, so cells count as
/// reachable through them; their swing stays clear of furniture. Counts are cells. A row prefers to start flush in a
/// room corner, using the side wall as its first cell's wall, and otherwise centers on its wall; either way the far
/// end keeps <see cref="EndClearance"/> free for the room's entrances.
/// </summary>
[Tool, GlobalClass]
public partial class CellsStep : FurnishingStep
{
	/// <summary>How far the kit's corner and T pieces reach along each leg from their joint.</summary>
	private const float JointReach = 2;
	private readonly Dictionary<string, Prop> _structure = [];

	[Export] public MeshLibrary WallLibrary { get; set; }
	[Export] public string FrontWall { get; set; } = "wall_gated";
	/// <summary>Partition pieces, alternating along the row; short sections use the front piece.</summary>
	[Export] public string[] PartitionWalls { get; set; } = ["wall_gated", "wall_archedwindow_gated"];
	/// <summary>Corner where the front meets the last partition at a free end of the row; empty for none.</summary>
	[Export] public string CornerWall { get; set; } = "wall_corner_gated";
	/// <summary>Joint where an inner partition meets the front (its arms run along the front); empty for none.</summary>
	[Export] public string JunctionWall { get; set; } = "wall_Tsplit";
	/// <summary>Start rows in a room corner when they fit there, instead of centering them.</summary>
	[Export] public bool FlushToCorner { get; set; } = true;
	/// <summary>The first row's wall across the room's width. Pin it when an asymmetric doorway needs the other side free.</summary>
	[Export] public CellRowSide FirstRowSide { get; set; }
	/// <summary>Operable gate: a door scene with an interior-door capability.</summary>
	[Export] public Prop Gate { get; set; }
	/// <summary>Cell width along the wall (X) and depth into the room (Y), in world units. With the default 2.4 opening, a
	/// 6.4 width gives 2-unit jambs that corner and junction pieces cover exactly.</summary>
	[Export] public Vector2 CellSize { get; set; } = new(6.4f, 7);
	[Export(PropertyHint.Range, "2.1,4,0.1")] public float OpeningWidth { get; set; } = 2.4f;
	/// <summary>Free floor kept at both ends of a row.</summary>
	[Export(PropertyHint.Range, "0,8,0.1")] public float EndClearance { get; set; } = 4;
	/// <summary>Chance of a second row on the opposite wall, when the aisle stays wide enough.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float OpposingChance { get; set; } = .5f;
	[Export(PropertyHint.Range, "2,8,0.1")] public float MinimumAisle { get; set; } = 4;
	/// <summary>Optional floor material inside accepted cell rows; the aisle keeps the room's floor style.</summary>
	[Export] public FloorMaterial CellFloor { get; set; }

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var runs = room.WallRuns.Where(run => run.Outward.X != 0).OrderByDescending(run => run.Length).ThenBy(run => run.Outward.X).ToList();
		var candidates = runs.Where(run => FirstRowSide == CellRowSide.Seeded
			|| run.Outward.X == (FirstRowSide == CellRowSide.Left ? -1 : 1)).ToArray();
		if (candidates.Length == 0) return 0;
		var first = candidates[rng.RandiRange(0, candidates.Length - 1)];
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
			foreach (var (left, cells, wallSide) in Anchors(room, run, rng))
			{
				int fitted = Mathf.Min(cells, MaximumCount - count);
				if (fitted < 1 || !TryPlaceRow(room, run, wallSide == 1 ? left + (cells - fitted) * CellSize.X : left, fitted, wallSide, rng)) continue;
				count += fitted;
				break;
			}
		}
		return count;
	}

	/// <summary>
	/// Candidate rows in the run frame: flush against each corner end first (in random order), then centered.
	/// <c>wallSide</c> is -1 when the room wall closes the row's low-X end, +1 for its high-X end, 0 for none.
	/// </summary>
	private IEnumerable<(float Left, int Cells, int WallSide)> Anchors(RoomBlueprint room, WallRun run, RandomNumberGenerator rng)
	{
		var frame = room.RunFrame(run);
		float half = run.LengthUnits / 2;
		// Local X may run either way along the wall; find which side the run's start lies on.
		bool startLow = (run.LineStart - frame.Origin).Dot(frame.Basis.X) < 0;
		var corners = new List<(float Face, int Side)>();
		if (room.StartsAtCorner(run)) corners.Add((room.EndDepth(run, atStart: true), startLow ? -1 : 1));
		if (room.EndsAtCorner(run)) corners.Add((room.EndDepth(run, atStart: false), startLow ? 1 : -1));
		if (corners.Count == 2 && rng.Randf() < .5f) corners.Reverse();
		if (FlushToCorner)
			foreach (var (face, side) in corners)
			{
				int cells = Mathf.FloorToInt((run.LengthUnits - face - EndClearance) / CellSize.X);
				yield return (side < 0 ? -half + face : half - face - cells * CellSize.X, cells, side);
			}
		int centered = Mathf.FloorToInt((run.LengthUnits - 2 * EndClearance) / CellSize.X);
		yield return (-centered * CellSize.X / 2, centered, 0);
	}

	private bool TryPlaceRow(RoomBlueprint room, WallRun run, float left, int cells, int wallSide, RandomNumberGenerator rng)
	{
		var frame = room.RunFrame(run);
		float width = CellSize.X, depth = CellSize.Y, jamb = (width - OpeningWidth) / 2, right = left + cells * width;
		var plan = new PlacementPlan();
		int phase = rng.RandiRange(0, PartitionWalls.Length - 1);
		bool cornerLow = !string.IsNullOrEmpty(CornerWall) && wallSide >= 0;
		bool cornerHigh = !string.IsNullOrEmpty(CornerWall) && wallSide <= 0;
		bool junctions = !string.IsNullOrEmpty(JunctionWall);
		// Corners and junctions cover the last stretch of the bars they join; those stretches are left out so meshes
		// never overlap. Partitions without a joint stop at the front wall's band.
		for (int divider = 0; divider <= cells; divider++)
		{
			if ((divider == 0 && wallSide < 0) || (divider == cells && wallSide > 0)) continue;
			bool joined = divider == 0 ? cornerLow : divider == cells ? cornerHigh : junctions;
			float x = left + divider * width;
			AddRun(plan, new Vector3(x, 0, 0), new Vector3(x, 0, depth - (joined ? JointReach : .5f)), PartitionWalls[(phase + divider) % PartitionWalls.Length]);
		}
		var gates = new List<Transform3D>();
		for (int cell = 0; cell < cells; cell++)
		{
			float x = left + cell * width;
			float lowCover = (cell == 0 ? cornerLow : junctions) ? Mathf.Min(JointReach, jamb) : 0;
			float highCover = (cell == cells - 1 ? cornerHigh : junctions) ? Mathf.Min(JointReach, jamb) : 0;
			AddRun(plan, new Vector3(x + lowCover, 0, depth), new Vector3(x + jamb, 0, depth), FrontWall);
			AddRun(plan, new Vector3(x + width - jamb, 0, depth), new Vector3(x + width - highCover, 0, depth), FrontWall);
			var gate = new Transform3D(Basis.Identity, new Vector3(x + width / 2, 0, depth));
			plan.Add(Gate, 0, gate, optional: false);
			gates.Add(frame * gate);
		}
		// Corners close free ends with legs along the front and back toward the wall; junctions carry the front across
		// inner partitions.
		if (cornerLow) plan.Add(Structure(CornerWall), 0, new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(left, 0, depth)), optional: false);
		if (cornerHigh) plan.Add(Structure(CornerWall), 0, new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), new Vector3(right, 0, depth)), optional: false);
		if (junctions)
			for (int divider = 1; divider < cells; divider++)
				plan.Add(Structure(JunctionWall), 0, new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(left + divider * width, 0, depth)), optional: false);
		if (!room.TryPlace(plan, frame)) return false;
		if (CellFloor != null)
			room.AddFloorPatch(RoomBlueprint.XZ(frame * new Aabb(left, 0, 0, right - left, 0, depth)), CellFloor);
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
		if (length < .05f) return;
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
		prop = new Prop { Id = $"structure.{item}", Library = WallLibrary, Items = [item], Gap = 0, Structural = true };
		_structure[item] = prop;
		return prop;
	}

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		var pieces = PartitionWalls.Append(FrontWall).Concat(new[] { CornerWall, JunctionWall }.Where(item => !string.IsNullOrEmpty(item)));
		if (WallLibrary == null || PartitionWalls.Length == 0 || pieces.Any(item => WallLibrary.FindItemByName(item) < 0))
			errors.Add($"Step {Id}: assign the wall library and existing front, partition, corner and junction pieces.");
		if (Gate == null || Gate.Scenes.Length == 0 || Gate.Blocking) errors.Add($"Step {Id}: assign a non-blocking gate prop with its door scene.");
		if (CellSize.X - OpeningWidth < 1 || CellSize.Y < 4) errors.Add($"Step {Id}: cells must fit the opening, its jambs and a bed.");
		if (CellFloor != null) errors.AddRange(CellFloor.Validate());
		return errors.ToArray();
	}
}