using System.Collections.Generic;
using System.Linq;
using Godot;

public enum ArcadeLayout { Bays, Aisles }

/// <summary>
/// Rows of arches that divide a room without closing any part of it. Each row runs wall to wall on the line between
/// two tile rows: an arch spans every inner tile corner and a stretched <see cref="Pier"/> fills from the outermost arch
/// to each wall, so bodies pass only under the arches. <see cref="ArcadeLayout.Bays"/> crosses the long axis every
/// <see cref="BayLength"/> tiles, cutting a long hall into vaulted bays; <see cref="ArcadeLayout.Aisles"/> runs a
/// mirrored pair along it, <see cref="AisleWidth"/> tiles in from both side walls, like a church's arcades.
/// <see cref="FurnishingStep.Content"/> holds the arch spans: one tile wide along local X, centered, blocking only at
/// their jambs. One arch serves the whole room. Counts are rows; <see cref="FurnishingStep.MaximumCount"/> caps bays.
/// </summary>
[Tool, GlobalClass]
public partial class ArcadeStep : FurnishingStep
{
	[Export] public ArcadeLayout Layout { get; set; }
	/// <summary>Tiles between bay rows; leftover tiles widen the end bays.</summary>
	[Export(PropertyHint.Range, "2,6")] public int BayLength { get; set; } = 3;
	/// <summary>Side-aisle width in tiles; the nave between the arcades keeps at least two.</summary>
	[Export(PropertyHint.Range, "1,3")] public int AisleWidth { get; set; } = 1;
	/// <summary>Structural wall piece stretched along the row between the outermost arch and the wall.</summary>
	[Export] public Prop Pier { get; set; }

	/// <summary>A row on the corner line <see cref="Index"/>, spanning tiles <see cref="First"/>..<see cref="Last"/>
	/// along X (bays) or along Z (aisles).</summary>
	private readonly record struct Row(int Index, int First, int Last, bool AlongX)
	{
		public Vector2I Corner(int along) => AlongX ? new(along, Index) : new(Index, along);
	}

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var rows = Layout == ArcadeLayout.Bays ? BayRows(room.Footprint) : AisleRows(room.Footprint);
		if (rows.Count == 0) return 0;
		var arch = room.Remember(this, () => room.Pick(Content, rng));
		if (arch == null) return 0;
		if (Layout == ArcadeLayout.Aisles)
		{
			// The two arcades mirror each other: both stand or neither does.
			var pair = new PlacementPlan();
			foreach (var row in rows) AddRow(room, arch, pair, row, rng);
			return TryPlace(room, arch, pair, Transform3D.Identity) ? rows.Count : 0;
		}
		int placed = 0;
		foreach (var row in rows)
		{
			if (placed >= MaximumCount) break;
			var plan = new PlacementPlan();
			AddRow(room, arch, plan, row, rng);
			if (TryPlace(room, arch, plan, Transform3D.Identity)) placed++;
		}
		return placed;
	}

	/// <summary>Corner lines across the long axis, every <see cref="BayLength"/> tiles, centered on the room's length.</summary>
	private List<Row> BayRows(RoomFootprint footprint) => RoomFootprint.Spaced(footprint.Length, BayLength, margin: BayLength)
		.Select(index => Span(footprint, index, alongX: true)).Where(row => row != null).Select(row => row.Value).ToList();

	/// <summary>The mirrored pair of corner lines along the long axis, or none when either does not run wall to wall.</summary>
	private List<Row> AisleRows(RoomFootprint footprint)
	{
		if (footprint.Width - 2 * AisleWidth < 2) return [];
		var rows = new[] { AisleWidth, footprint.Width - AisleWidth }.Select(index => Span(footprint, index, alongX: false)).ToList();
		return rows.All(row => row != null) ? rows.Select(row => row.Value).ToList() : [];
	}

	/// <summary>
	/// The run of tiles on both sides of corner line <paramref name="index"/>, when it is one unbroken run from wall to
	/// wall at least two tiles long; a line that ends against a jog in the outline stands nowhere.
	/// </summary>
	private static Row? Span(RoomFootprint footprint, int index, bool alongX)
	{
		int extent = alongX ? footprint.Width : footprint.Length;
		bool Floor(int along, int side) => alongX ? footprint.IsFloor(along, side) : footprint.IsFloor(side, along);
		bool Both(int along) => Floor(along, index - 1) && Floor(along, index);
		bool Either(int along) => Floor(along, index - 1) || Floor(along, index);
		var tiles = Enumerable.Range(0, extent).Where(Both).ToList();
		if (tiles.Count < 2 || tiles[^1] - tiles[0] + 1 != tiles.Count) return null;
		if (Either(tiles[0] - 1) || Either(tiles[^1] + 1)) return null;
		return new Row(index, tiles[0], tiles[^1], alongX);
	}

	/// <summary>
	/// Arches over the row's inner tile corners, then piers from each wall face to the outer end of the nearest arch,
	/// which stands on the end tile's middle.
	/// </summary>
	private void AddRow(RoomBlueprint room, Placeable arch, PlacementPlan plan, Row row, RandomNumberGenerator rng)
	{
		var basis = row.AlongX ? Basis.Identity : new Basis(Vector3.Up, -Mathf.Pi / 2);
		var along = basis.X;
		for (int corner = row.First + 1; corner <= row.Last; corner++)
			arch.Compose(room, rng, plan, new Transform3D(basis, RoomFootprint.CornerPoint(row.Corner(corner))), optional: false);
		float half = Room.TileSize / 2f;
		var start = RoomFootprint.CornerPoint(row.Corner(row.First));
		var end = RoomFootprint.CornerPoint(row.Corner(row.Last + 1));
		AddPier(plan, basis, start + along * FaceDepth(room, row, row.First, -1), start + along * half);
		AddPier(plan, basis, end - along * half, end - along * FaceDepth(room, row, row.Last, 1));
	}

	/// <summary>How far the wall at the row's end reaches into the room, on either tile beside the line.</summary>
	private static float FaceDepth(RoomBlueprint room, Row row, int along, int direction)
	{
		var outward = row.AlongX ? new Vector2I(direction, 0) : new Vector2I(0, direction);
		var tile = row.Corner(along);
		var before = row.AlongX ? tile + Vector2I.Up : tile + Vector2I.Left;
		return Mathf.Max(room.WallDepth(before, outward), room.WallDepth(tile, outward));
	}

	/// <summary>The pier stretched along the row's direction to cover <paramref name="from"/>..<paramref name="to"/>.</summary>
	private void AddPier(PlacementPlan plan, Basis basis, Vector3 from, Vector3 to)
	{
		float length = from.DistanceTo(to);
		if (length < .05f) return;
		var extent = Pier.Geometry(0).Envelope;
		float scale = length / extent.Size.X;
		plan.Add(Pier, 0, new Transform3D(basis * Basis.FromScale(new Vector3(scale, 1, 1)), from - basis.X * (scale * extent.Position.X)), optional: false);
	}

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		if (Pier is not { Structural: true }) errors.Add($"Step {Id}: assign a structural pier piece.");
		else errors.AddRange(Pier.Validate());
		return errors.ToArray();
	}
}