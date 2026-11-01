using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A room's wall family: weighted straight pieces (plain, cracked, broken, arched…), corner and half pieces, an
/// optional rhythm piece every few modules (pillared walls), occasional accents (windows, built-in shelves) and the
/// frame set into open doorways. Asymmetric pieces face into the room. Data only: new looks are new .tres files, not
/// new code.
/// </summary>
[Tool, GlobalClass]
public partial class WallStyle : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	/// <summary>Realms (by ID) this appears in; none means every realm.</summary>
	[Export] public string[] Realms { get; set; } = [];
	[Export] public MeshLibrary Library { get; set; }
	/// <summary>Plain straight pieces by weight; decorations may hang on these.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Straight { get; set; } = new() { ["wall"] = 1 };
	/// <summary>Corner and half pieces; a run's end beside a corner gets the half (see <see cref="WallFootprints.PlaceWalls"/>).</summary>
	[Export] public string Corner { get; set; } = "wall_corner";
	[Export] public string Half { get; set; } = "wall_half";
	/// <summary>Whether decorations may hang in front of the corner and half pieces that end each run; off for
	/// families that dress those pieces (chains, slime, moss).</summary>
	[Export] public bool PlainEnds { get; set; } = true;
	/// <summary>Full-width frame set into doorways without a door, so an opening reads as a passage and the wall ends
	/// beside it are finished. Visual only: it never blocks the opening. Empty leaves openings bare.</summary>
	[Export] public string Opening { get; set; } = "wall_open_scaffold";
	/// <summary>Rhythm piece placed every <see cref="PillarEvery"/> modules along each wall, centered on the run.</summary>
	[Export] public string Pillar { get; set; } = "";
	[Export(PropertyHint.Range, "0,6")] public int PillarEvery { get; set; }
	/// <summary>Occasional feature pieces by weight; decorations never hang on them.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Accents { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float AccentChance { get; set; }

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || Library == null || Straight.Count == 0)
			return [$"Wall style {Id}: assign an ID, the wall library and straight pieces."];
		var names = Straight.Keys.Concat(Accents.Keys).Append(Corner).Append(Half);
		if (PillarEvery > 0) names = names.Append(Pillar);
		if (!string.IsNullOrEmpty(Opening)) names = names.Append(Opening);
		errors.AddRange(names.Where(name => Library.FindItemByName(name) < 0).Select(name => $"Wall style {Id}: '{name}' is not in the wall library."));
		// Half pieces fill exactly what a corner arm leaves of an edge; any other length would overlap or leave a gap.
		if (Library.GetItemMesh(Library.FindItemByName(Half)) is { } half && Mathf.Abs(half.GetAabb().Size.X - Room.TileSize / 2f) > WallFootprints.Eps)
			errors.Add($"Wall style {Id}: half piece '{Half}' must be half a tile long.");
		return errors.ToArray();
	}

	/// <summary>
	/// Builds the room's perimeter walls into its wall GridMap and records each edge's depth and decor support as
	/// measured from the pieces on it; accents and pillars drawn for a run end beside a corner give way to the half.
	/// </summary>
	public void Build(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var pieces = new Dictionary<Vector3I, (Vector2I Tile, Vector2I Outward, string Item)>();
		foreach (var run in room.WallRuns)
		{
			int pillarPhase = PillarEvery > 0 ? (run.Length - 1) / 2 % PillarEvery : -1;
			for (int index = 0; index < run.Length; index++)
			{
				string edge = DrawEdge(rng);
				var tile = run.TileAt(index);
				string item = PillarEvery > 0 && index % PillarEvery == pillarPhase ? Pillar : edge;
				pieces[EdgeCell(tile, run.Outward)] = (tile, run.Outward, item);
			}
		}
		var footprint = room.Footprint;
		var grid = room.Room.WallGridMap;
		WallFootprints.PlaceWalls(
			grid,
			Room.TileSize,
			footprint.Tiles.Select(tile => (tile, (Vector3I)RoomFootprint.TileCenter(tile))),
			(tile, dx, dz) => !footprint.IsFloor(tile + new Vector2I(dx, dz)) && !room.IsDoorway(tile, new Vector2I(dx, dz)),
			() => Library.FindItemByName(Straight.Keys.First()),
			() => Library.FindItemByName(Half),
			() => Library.FindItemByName(Corner),
			position => Library.FindItemByName(pieces.TryGetValue(position, out var piece) ? piece.Item : Straight.Keys.First()));
		foreach (var (cell, piece) in pieces)
		{
			var edge = WallFootprints.MeasureEdge(grid, cell, -piece.Outward, Room.TileSize);
			room.SetWall(piece.Tile, piece.Outward, edge.Depth, IsPlain(edge.Item));
		}
	}

	/// <summary>Whether decorations may hang in front of an edge's own piece (a library item): a plain straight piece,
	/// or a run end's half piece (-1: corners alone) when <see cref="PlainEnds"/>.</summary>
	public bool IsPlain(int item)
	{
		string name = item < 0 ? null : Library.GetItemName(item);
		return name == null || name == Half ? PlainEnds : Straight.ContainsKey(name) && !Accents.ContainsKey(name) && name != Pillar;
	}

	/// <summary>
	/// One wall edge: an accent at <see cref="AccentChance"/>, else a straight piece. Both are drawn for every edge so a
	/// style change never reshuffles the rest of a run.
	/// </summary>
	public string DrawEdge(RandomNumberGenerator rng)
	{
		bool accent = rng.Randf() < AccentChance && Accents.Count > 0;
		string straight = Weighted.Pick(Straight, rng);
		string accentItem = Accents.Count > 0 ? Weighted.Pick(Accents, rng) : null;
		return accent ? accentItem : straight;
	}

	private static Vector3I EdgeCell(Vector2I tile, Vector2I outward) =>
		(Vector3I)RoomFootprint.TileCenter(tile) + new Vector3I(outward.X, 0, outward.Y) * (Room.TileSize / 2);
}