using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A room's wall family: weighted straight pieces (plain, cracked, broken, arched…), corner and half pieces, an
/// optional rhythm piece every few modules (pillared walls) and occasional accents (windows, built-in shelves).
/// Asymmetric pieces face into the room. Data only: new looks are new .tres files, not new code.
/// </summary>
[Tool, GlobalClass]
public partial class WallStyle : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	[Export] public MeshLibrary Library { get; set; }
	/// <summary>Plain straight pieces by weight; decorations may hang on these.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Straight { get; set; } = new() { ["wall"] = 1 };
	[Export] public string Corner { get; set; } = "wall_corner";
	[Export] public string Half { get; set; } = "wall_half";
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
		errors.AddRange(names.Where(name => Library.FindItemByName(name) < 0).Select(name => $"Wall style {Id}: '{name}' is not in the wall library."));
		return errors.ToArray();
	}

	/// <summary>Builds the room's perimeter walls into its wall GridMap and records each edge's depth and decor support.</summary>
	public void Build(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var pieces = new Dictionary<Vector3I, (Vector2I Tile, Vector2I Outward, string Item, bool Decoratable)>();
		foreach (var run in room.WallRuns)
		{
			int pillarPhase = PillarEvery > 0 ? (run.Length - 1) / 2 % PillarEvery : -1;
			for (int index = 0; index < run.Length; index++)
			{
				var (straight, edge) = DrawEdge(rng);
				var tile = run.TileAt(index);
				string item = PillarEvery > 0 && index % PillarEvery == pillarPhase ? Pillar : edge;
				pieces[EdgeCell(tile, run.Outward)] = (tile, run.Outward, item, item == straight);
			}
		}
		var footprint = room.Footprint;
		WallFootprints.PlaceWalls(
			room.Room.WallGridMap,
			Room.TileSize,
			footprint.Tiles.Select(tile => (tile, (Vector3I)RoomFootprint.TileCenter(tile))),
			(tile, dx, dz) => !footprint.IsFloor(tile + new Vector2I(dx, dz)) && !room.IsDoorway(tile, new Vector2I(dx, dz)),
			() => Library.FindItemByName(Straight.Keys.First()),
			() => Library.FindItemByName(Half),
			() => Library.FindItemByName(Corner),
			request => pieces.TryGetValue(request.Position, out var piece)
				? (Library.FindItemByName(piece.Item), WallFootprints.FacingInto(-piece.Outward))
				: (Library.FindItemByName(Straight.Keys.First()), request.Orientation));
		foreach (var piece in pieces.Values)
		{
			// Pieces face the room with local +Z, so the front of the mesh is how far the wall reaches inward.
			var mesh = Library.GetItemMesh(Library.FindItemByName(piece.Item));
			float depth = mesh == null ? .5f : mesh.GetAabb().End.Z;
			room.SetWall(piece.Tile, piece.Outward, depth, piece.Decoratable);
		}
	}

	/// <summary>
	/// One wall edge: an accent at <see cref="AccentChance"/>, else a straight piece, returned with the straight piece it
	/// replaced. Both are drawn for every edge so a style change never reshuffles the rest of a run.
	/// </summary>
	public (string Straight, string Item) DrawEdge(RandomNumberGenerator rng)
	{
		bool accent = rng.Randf() < AccentChance && Accents.Count > 0;
		string straight = Weighted.Pick(Straight, rng);
		string accentItem = Accents.Count > 0 ? Weighted.Pick(Accents, rng) : null;
		return (straight, accent ? accentItem : straight);
	}

	private static Vector3I EdgeCell(Vector2I tile, Vector2I outward) =>
		(Vector3I)RoomFootprint.TileCenter(tile) + new Vector3I(outward.X, 0, outward.Y) * (Room.TileSize / 2);
}