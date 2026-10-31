using System.Collections.Generic;
using System.Linq;
using Godot;

public enum RugLayout
{
	/// <summary>A fringed runner along the room's length (+Z) down its middle column; odd widths only.</summary>
	Runner,
	/// <summary>A bordered rug over the open floor, <see cref="FloorRug.Margin"/> tiles in from the walls.</summary>
	Field,
}

/// <summary>
/// A rug laid with the floor, from one carpet's whole-tile pieces in the decoration library: corners, edges and
/// centers for a field, fringed ends and runs for a runner. Rugs rest on the floor top (y 0.05), so use them on flat
/// floors; they are walk-over decoration with no collision, and props stand on them. Tiles that are not floor
/// (stairwells) or carry an inlay end the rug, and a rug that does not fit is left out.
/// </summary>
[Tool, GlobalClass]
public partial class FloorRug : Resource
{
	// GridMap orientations for Y turns of 0, 90, 180 and -90 degrees.
	private const int Turn0 = 0, Turn90 = 16, Turn180 = 10, TurnMinus90 = 22;

	[Export] public MeshLibrary Library { get; set; }
	/// <summary>Carpets by weight, as their item prefix (carpet_red); one carpet per room.</summary>
	[Export] public Godot.Collections.Dictionary<string, float> Carpets { get; set; } = [];
	[Export] public RugLayout Layout { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1;
	/// <summary>Field rugs: whole tiles of bare floor kept between the rug and the room's bounds.</summary>
	[Export(PropertyHint.Range, "0,3")] public int Margin { get; set; } = 1;

	private static readonly string[] Pieces = ["center", "edge", "corner", "runner", "runner_end"];

	public string[] Validate()
	{
		if (Library == null || Carpets.Count == 0) return ["Floor rug: assign the decoration library and a carpet."];
		return Carpets.Keys.SelectMany(carpet => Pieces.Select(piece => Item(carpet, piece)))
			.Where(item => Library.FindItemByName(item) < 0).Select(item => $"Floor rug: '{item}' is not in the decoration library.").ToArray();
	}

	/// <summary>Lays the rug into the room's decoration GridMap; <paramref name="blocked"/> marks quarter tiles a rug may not cover.</summary>
	public void Lay(RoomBlueprint room, bool[,] blocked, RandomNumberGenerator rng)
	{
		if (rng.Randf() >= Chance) return;
		string carpet = Weighted.Pick(Carpets, rng);
		var cells = Layout == RugLayout.Runner ? RunnerCells(room, carpet, blocked) : FieldCells(room, carpet, blocked);
		if (cells.Count == 0) return;
		var grid = room.Room.DecorationGridMap;
		grid.MeshLibrary = Library;
		foreach (var (tile, item, orientation) in cells)
			grid.SetCellItem((Vector3I)RoomFootprint.TileCenter(tile), Library.FindItemByName(item), orientation);
	}

	private static string Item(string carpet, string piece) => $"{carpet}_{piece}_A";

	private static bool Open(RoomBlueprint room, bool[,] blocked, Vector2I tile)
	{
		if (!room.IsFloor(tile)) return false;
		for (int qx = 0; qx < 2; qx++)
		for (int qz = 0; qz < 2; qz++)
			if (blocked[tile.X * 2 + qx, tile.Y * 2 + qz]) return false;
		return true;
	}

	/// <summary>The longest open run of the middle column; a runner needs at least two tiles for its two ends.</summary>
	private static List<(Vector2I, string, int)> RunnerCells(RoomBlueprint room, string carpet, bool[,] blocked)
	{
		var footprint = room.Footprint;
		if (footprint.Width % 2 == 0) return [];
		int x = footprint.Width / 2;
		(int Start, int Length) best = (0, 0);
		for (int z = 0, start = 0; z <= footprint.Length; z++)
		{
			if (z < footprint.Length && Open(room, blocked, new(x, z))) continue;
			if (z - start > best.Length) best = (start, z - start);
			start = z + 1;
		}
		if (best.Length < 2) return [];
		// Runner pieces run along local X with the fringe toward -X; a 90 degree turn lays them along +Z, fringe at +Z.
		return Enumerable.Range(best.Start, best.Length).Select(z => (new Vector2I(x, z),
			Item(carpet, z == best.Start || z == best.Start + best.Length - 1 ? "runner_end" : "runner"),
			z == best.Start ? TurnMinus90 : Turn90)).ToList();
	}

	/// <summary>A rectangle inset by the margin; all of it must be open, or the room gets no rug.</summary>
	private List<(Vector2I, string, int)> FieldCells(RoomBlueprint room, string carpet, bool[,] blocked)
	{
		var footprint = room.Footprint;
		var area = new Rect2I(Margin, Margin, footprint.Width - 2 * Margin, footprint.Length - 2 * Margin);
		if (area.Size.X < 1 || area.Size.Y < 1 || area.Size.X * area.Size.Y < 2) return [];
		var cells = new List<(Vector2I, string, int)>();
		for (int x = area.Position.X; x < area.End.X; x++)
		for (int z = area.Position.Y; z < area.End.Y; z++)
		{
			var tile = new Vector2I(x, z);
			if (!Open(room, blocked, tile)) return [];
			cells.Add((tile, null, 0));
		}
		return cells.Select(cell => Piece(carpet, area, cell.Item1)).ToList();
	}

	/// <summary>
	/// Field pieces by position. Authored borders face -Z (edge) and -Z and -X (corner); one-tile-wide fields are runners.
	/// </summary>
	private static (Vector2I, string, int) Piece(string carpet, Rect2I area, Vector2I tile)
	{
		bool west = tile.X == area.Position.X, east = tile.X == area.End.X - 1;
		bool north = tile.Y == area.Position.Y, south = tile.Y == area.End.Y - 1;
		if (area.Size.X == 1) return (tile, Item(carpet, north || south ? "runner_end" : "runner"), north ? TurnMinus90 : Turn90);
		if (area.Size.Y == 1) return (tile, Item(carpet, west || east ? "runner_end" : "runner"), west ? Turn0 : east ? Turn180 : Turn0);
		if (north && west) return (tile, Item(carpet, "corner"), Turn0);
		if (north && east) return (tile, Item(carpet, "corner"), TurnMinus90);
		if (south && east) return (tile, Item(carpet, "corner"), Turn180);
		if (south && west) return (tile, Item(carpet, "corner"), Turn90);
		if (north) return (tile, Item(carpet, "edge"), Turn0);
		if (east) return (tile, Item(carpet, "edge"), TurnMinus90);
		if (south) return (tile, Item(carpet, "edge"), Turn180);
		if (west) return (tile, Item(carpet, "edge"), Turn90);
		return (tile, Item(carpet, "center"), Turn0);
	}
}
