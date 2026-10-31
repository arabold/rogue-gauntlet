using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum PrisonCellRows { Random, OneWall, OpposingWalls }

/// <summary>A sized prison assembled from reusable chamber rows, cell bedding and optional guard/supply assemblies.</summary>
[Tool, GlobalClass]
public partial class PrisonRoomGenerator : RoomGenerator
{
	[Export] public RoomChamberDefinition Cell { get; set; }
	[Export] public PrisonCellRows Rows { get; set; }
	[Export] public int[] CellsPerWall { get; set; } = [1, 2];
	[Export] public BedClusterDefinition Bedding { get; set; }
	[Export] public RoomAssemblyDefinition GuardStation { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float GuardStationChance { get; set; } = .8f;
	[Export] public RoomAssemblyDefinition Supplies { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float SuppliesChance { get; set; } = .5f;
	[Export(PropertyHint.Range, "3,8")] public int EndBayLength { get; set; } = 4;
	[Export(PropertyHint.Range, "3,8,0.5")] public float MinimumAisleWidth { get; set; } = 4;

	public override RoomArrangementPool CreateInteriorPool() => CreateSlotPool(64, 64,
		(Bedding?.Definitions ?? []).Concat(GuardStation?.Definitions ?? []).Concat(Supplies?.Definitions ?? []).DistinctBy(entry => entry.Id).ToArray());
	public override RoomArrangementPool CreateInteriorPool(Room room)
	{
		var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
		return CreateSlotPool(slots.Count(slot => slot.Definition.Placement == ArrangementPlacement.Floor),
			slots.Count(slot => slot.Definition.Placement == ArrangementPlacement.Wall), slots.Select(slot => slot.Definition).DistinctBy(entry => entry.Id).ToArray());
	}

	public override string[] Validate()
	{
		var errors = base.Validate().Concat(ValidateSlotShell()).ToList();
		errors.AddRange(Cell?.Validate() ?? ["Prison: assign a cell module."]);
		if (Cell?.DoorScene == null) errors.Add("Prison: every cell needs an operable door scene.");
		errors.AddRange(Bedding?.Validate() ?? ["Prison: assign cell bedding."]);
		errors.AddRange(GuardStation?.Validate() ?? []); errors.AddRange(Supplies?.Validate() ?? []);
		if (!Enum.IsDefined(Rows) || CellsPerWall == null || CellsPerWall.Length == 0 || CellsPerWall.Any(count => count < 1 || count > 4)
			|| CellsPerWall.Distinct().Count() != CellsPerWall.Length) errors.Add("Prison: choose unique cell counts from one to four and a supported row mode.");
		if (EndBayLength < 3 || EndBayLength > 8 || !float.IsFinite(MinimumAisleWidth) || MinimumAisleWidth < 3 || MinimumAisleWidth > 8
			|| new[] { GuardStationChance, SuppliesChance }.Any(chance => !float.IsFinite(chance) || chance < 0 || chance > 1)) errors.Add("Prison: assign clear end bays, an aisle and optional furniture probabilities.");
		if (Cell != null && CellsPerWall != null && CellsPerWall.Any(count => SizeFor(count, 2).X > 12 || SizeFor(count, 2).Y > 12)) errors.Add("Prison: cell counts exceed the shell size limit.");
		return errors.ToArray();
	}

	public Vector2I SizeFor(int cells, int sides) => new(Mathf.CeilToInt((Cell.Size.Y * sides + MinimumAisleWidth + 1) / Room.TileSize),
		Mathf.CeilToInt((Cell.Size.X * cells + EndBayLength * 2f) / Room.TileSize));

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		ulong seed = ((ulong)rng.Randi() << 32) | rng.Randi();
		using var layout = GenerationRandom.Create(seed, GenerationRandom.Layout, "prison");
		int sides = Rows == PrisonCellRows.Random ? layout.RandiRange(1, 2) : (int)Rows;
		int cells = CellsPerWall[layout.RandiRange(0, CellsPerWall.Length - 1)];
		var size = SizeFor(cells, sides);
		if (!PopulateSlotShell(room, layout, true, size)) return false;
		room.InteriorStructure = new Node3D { Name = "InteriorStructure" };
		room.AddChild(room.InteriorStructure); room.InteriorStructure.Owner = room;
		var sites = new List<RoomChamberPatterns.ChamberSite>();
		float start = EndBayLength - Room.TileSize / 2f;
		bool west = sides == 2 || layout.RandiRange(0, 1) == 0;
		if (west)
			sites.AddRange(RoomChamberPatterns.PlaceRow(room, Cell, new(new Basis(Vector3.Up, Mathf.Pi / 2),
				new Vector3(-2, 0, start + cells * Cell.Size.X)), cells, "west", layout));
		if (sides == 2 || !west)
			sites.AddRange(RoomChamberPatterns.PlaceRow(room, Cell, new(new Basis(Vector3.Up, -Mathf.Pi / 2),
				new Vector3(size.X * Room.TileSize - 2, 0, start)), cells, "east", layout));
		var context = new RoomFurnishingContext(room, GenerationRandom.DeriveSeed(seed, GenerationRandom.Arrangements, "prison"), 1);
		foreach (var site in sites)
		{
			var bed = site.Frame * new Transform3D(Basis.Identity, new Vector3(Cell.Size.X / 2f, 0, 2));
			if (!RoomFurnishingPatterns.PlaceBed(context, Bedding, bed, "cell:" + site.Id))
				throw new InvalidOperationException($"Prison: bedding cannot fit cell {site.Id}.");
		}
		var freeWall = sides == 1 ? (west ? Vector2I.Right : Vector2I.Left) : Vector2I.Zero;
		PlaceGuardContent(context, GuardStation, GuardStationChance, "guard", size, freeWall);
		PlaceGuardContent(context, Supplies, SuppliesChance, "supplies", size, freeWall);
		if (!context.IsConnected) throw new InvalidOperationException("Prison: open cell gates must connect every usable floor area.");
		AddPlannedSlots(room, context.Placements, "prison");
		room.SetMeta("room_layout_variant", sides == 1 ? "prison.one_wall" : "prison.opposing_walls");
		room.SetMeta("prison_cells_per_wall", cells);
		room.BakeTileMap();
		return true;
	}

	private static void PlaceGuardContent(RoomFurnishingContext context, RoomAssemblyDefinition assembly, float chance, string key, Vector2I size, Vector2I freeWall)
	{
		if (assembly == null) return;
		using var rng = context.Random(key);
		if (rng.Randf() >= chance) return;
		var candidates = new List<Transform3D>();
		foreach (int end in new[] { 0, 1 })
		foreach (float x in new[] { 1.5f, (size.X - 1) * Room.TileSize - 1.5f })
			candidates.Add(new(new Basis(Vector3.Up, end * Mathf.Pi), new Vector3(x, 0, end * (size.Y - 1) * Room.TileSize)));
		if (freeWall != Vector2I.Zero)
		{
			float yaw = freeWall.X > 0 ? -Mathf.Pi / 2 : Mathf.Pi / 2;
			float x = freeWall.X > 0 ? (size.X - 1) * Room.TileSize : 0;
			candidates.Add(new(new Basis(Vector3.Up, yaw), new Vector3(x, 0, (size.Y - 1) * Room.TileSize / 2f)));
		}
		foreach (var candidate in candidates.Select(transform => (Transform: transform, Order: rng.Randi())).OrderBy(entry => entry.Order))
			if (RoomFurnishingPatterns.PlaceAssembly(context, assembly, candidate.Transform, key)) return;
	}
}