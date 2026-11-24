using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A room's wall family: weighted straight pieces (plain, cracked, broken, arched…), shared junction pieces, an
/// optional rhythm piece every few modules (pillared walls), occasional accents (windows, built-in shelves), and the
/// door or frame set into each doorway. Asymmetric pieces face into the room. Data only: new looks are new .tres files,
/// not new code.
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
	/// <summary>Whether decorations may hang in front of the corner and half pieces that end each run; off for
	/// families that dress those pieces (chains, slime, moss).</summary>
	[Export] public bool PlainEnds { get; set; } = true;
	/// <summary>Full-width frame set into doorways without a door, so an opening reads as a passage and the wall ends
	/// beside it are finished. Its posts have collision while the central opening stays clear. Empty leaves openings bare.</summary>
	[Export] public string Opening { get; set; } = "wall_open_scaffold";
	/// <summary>The door set into doorways that get one (see <see cref="RoomType.DoorChance"/>), with its own frame in
	/// this family's look. Every style a room type with doors draws from needs one.</summary>
	[Export] public PackedScene DoorScene { get; set; }
	/// <summary>Rhythm piece placed every <see cref="PillarEvery"/> modules along each wall, centered on the run.</summary>
	[Export] public string Pillar { get; set; } = "";
	[Export(PropertyHint.Range, "0,6")] public int PillarEvery { get; set; }
	/// <summary>Occasional feature pieces by weight; decorations never hang on them.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Accents { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float AccentChance { get; set; }
	/// <summary>Straight pieces (such as breached walls) replaced with <see cref="FallbackStraight"/> on two-sided corridor partitions.</summary>
	[Export] public string[] ExteriorOnly { get; set; } = [];
	[ExportGroup("Structural Pieces")]
	[Export] public string Half { get; set; } = "wall_half";
	[Export] public string Short { get; set; } = "wall_junction_short";
	[Export] public string Corner { get; set; } = "wall_corner";
	[Export] public string TSplit { get; set; } = "wall_Tsplit";
	[Export] public string Crossing { get; set; } = "wall_crossing";

	/// <summary>Primary opaque straight piece used for structural fallbacks, secret doors, and partition walls.</summary>
	public string FallbackStraight
	{
		get
		{
			string first = null;
			foreach (string key in Straight.Keys)
			{
				first ??= key;
				if (ExteriorOnly == null || Array.IndexOf(ExteriorOnly, key) < 0) return key;
			}
			return first ?? "wall";
		}
	}

	/// <summary>Structural piece set passed to <see cref="BoundaryWallBuilder.Build"/> for this family.</summary>
	public BoundaryWallBuilder.WallPieces Pieces => new(FallbackStraight, Half, Short, Corner, TSplit, Crossing);

	public virtual string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || Library == null || Straight.Count == 0)
			return [$"Wall style {Id}: assign an ID, the wall library and straight pieces."];
		var names = Straight.Keys.Concat(Accents.Keys)
			.Concat([Half, Short, Corner, TSplit, Crossing])
			.Concat(ExteriorOnly ?? []);
		if (PillarEvery > 0) names = names.Append(Pillar);
		if (!string.IsNullOrEmpty(Opening)) names = names.Append(Opening);
		errors.AddRange(names.Where(name => Library.FindItemByName(name) < 0).Select(name => $"Wall style {Id}: '{name}' is not in the wall library."));

		return errors.ToArray();
	}

	/// <summary>
	/// Builds the room's perimeter walls into its wall GridMap and records each edge's depth and decor support as
	/// measured from the pieces on it; accents and pillars drawn for a run end beside a corner give way to the half.
	/// </summary>
	public virtual bool Build(RoomBlueprint room, RandomNumberGenerator rng)
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
		var itemIds = new Dictionary<string, int>();
		int FindItem(string name) => itemIds.TryGetValue(name, out int id) ? id : itemIds[name] = Library.FindItemByName(name);
		string fallback = FallbackStraight;
		if (!BoundaryWallBuilder.Build(
			grid,
			footprint.Tiles.Select(tile => (tile, (Vector3I)RoomFootprint.TileCenter(tile))),
			(tile, dx, dz) => !footprint.IsFloor(tile + new Vector2I(dx, dz)) && !room.IsDoorway(tile, new Vector2I(dx, dz)),
			(position, _) => FindItem(pieces.TryGetValue(position, out var piece) ? piece.Item : fallback),
			framedEdges: room.Room.FramedEdges,
			structural: Pieces)) return false;
		var measureCache = new Dictionary<int, (Aabb Bounds, Aabb UpperBounds, string Name)>();
		foreach (var (cell, piece) in pieces)
		{
			var edge = WallFootprints.MeasureEdge(grid, cell, -piece.Outward, Room.TileSize, measureCache);
			room.SetWall(piece.Tile, piece.Outward, edge.Depth, IsPlain(edge.Item), edge.MountDepth, edge.Mounts);
		}
		return true;
	}

	/// <summary>Whether decorations may hang in front of an edge's own piece (a library item): a plain straight piece
	/// (excluding exterior-only breached pieces), or a run end's half piece (-1: corners alone) when <see cref="PlainEnds"/>.</summary>
	public bool IsPlain(int item)
	{
		string name = item < 0 ? null : Library.GetItemName(item);
		if (name == null || name == Half || name == Short) return PlainEnds;
		if (ExteriorOnly != null && Array.IndexOf(ExteriorOnly, name) >= 0) return false;
		if (Straight.ContainsKey(name)) return !Accents.ContainsKey(name) && name != Pillar;
		return name == FallbackStraight;
	}

	/// <summary>
	/// One wall edge: an accent at <see cref="AccentChance"/>, else a straight piece. Both are drawn for every edge so a
	/// style change never reshuffles the rest of a run. When <paramref name="partitionOnly"/> is true (a two-sided wall
	/// separating walkable corridors), accents and breached straight pieces are suppressed for an opaque symmetric wall.
	/// </summary>
	public string DrawEdge(RandomNumberGenerator rng, bool partitionOnly = false)
	{
		bool accent = rng.Randf() < AccentChance && Accents.Count > 0;
		string straight = Weighted.Pick(Straight, rng);
		string accentItem = Accents.Count > 0 ? Weighted.Pick(Accents, rng) : null;
		if (!partitionOnly) return accent ? accentItem : straight;
		return ExteriorOnly != null && Array.IndexOf(ExteriorOnly, straight) >= 0 ? FallbackStraight : straight;
	}

	/// <summary>The wall cell centered on a tile's <paramref name="outward"/> edge.</summary>
	internal static Vector3I EdgeCell(Vector2I tile, Vector2I outward) =>
		(Vector3I)RoomFootprint.TileCenter(tile) + new Vector3I(outward.X, 0, outward.Y) * (Room.TileSize / 2);
}