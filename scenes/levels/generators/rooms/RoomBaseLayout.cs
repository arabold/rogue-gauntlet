using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Reusable footprint and entrance choices, independent of room contents, material and columns.</summary>
[Tool, GlobalClass]
public partial class RoomBaseLayout : Resource
{
	private RoomShapeArchetype _shape = RoomShapeArchetype.Rectangle;
	[Export] public RoomShapeArchetype Shape
	{
		get => _shape;
		set { if (_shape == value) return; _shape = value; NotifyPropertyListChanged(); }
	}
	[Export(PropertyHint.Range, "2,6")] public int LArmWidthTiles { get; set; } = 2;
	[Export] public Vector2I MinimumSize { get; set; } = new(1, 1);
	[Export] public Vector2I MaximumSize { get; set; } = new(4, 5);
	[Export] public bool OpposingEntrances { get; set; }
	[Export(PropertyHint.Range, "1,4")] public int Doorways { get; set; } = 2;

	public string[] Validate()
	{
		var errors = new List<string>();
		if (!Enum.IsDefined(Shape) || Shape == RoomShapeArchetype.Random) errors.Add("Base layout: choose an explicit footprint shape.");
		if (MinimumSize.X < 1 || MinimumSize.Y < 1 || MaximumSize.X > 12 || MaximumSize.Y > 12
			|| MaximumSize.X < MinimumSize.X || MaximumSize.Y < MinimumSize.Y)
			errors.Add("Base layout: use ordered minimum/maximum dimensions between 1 and 12 tiles.");
		if (Shape == RoomShapeArchetype.LShape && (LArmWidthTiles < 2 || LArmWidthTiles > 6
			|| MinimumSize.X - LArmWidthTiles < 2 || MinimumSize.Y - LArmWidthTiles < 2))
			errors.Add("L layout: use arms at least two tiles wide and a corner recess at least two tiles across each axis.");
		if (Doorways < 1 || Doorways > 4 || (OpposingEntrances && (Shape != RoomShapeArchetype.Rectangle || Doorways != 2)))
			errors.Add("Base layout: opposing entrances require a rectangle and exactly two doorways.");
		return errors.ToArray();
	}

	/// <summary>Build one isolated shell through the existing footprint planner, retaining walls for composition validation.</summary>
	public bool Populate(Room room, ProceduralRoomBuilder shell, RandomNumberGenerator rng, int minimumFloorTiles = 1)
	{
		using var builder = (ProceduralRoomBuilder)shell.Duplicate();
		builder.ShapeArchetype = Shape;
		builder.ColumnChance = 0;
		builder.EnableChasms = false;
		builder.GenerateWalls = true;
		builder.DoorwayChance = 1;
		builder.MinDoorways = builder.MaxDoorways = Doorways;
		var sizes = EligibleSizes(minimumFloorTiles).ToArray();
		if (sizes.Length == 0) return false;
		var size = sizes[rng.RandiRange(0, sizes.Length - 1)];
		bool built = Shape == RoomShapeArchetype.LShape
			? builder.PopulateRoomFromFootprint(room, rng, CreateLFootprint(size, rng))
			: builder.PopulateRoomAtSize(room, rng, size.X, size.Y, OpposingEntrances ? [Vector2I.Up, Vector2I.Down] : null);
		if (built && OpposingEntrances) room.LayoutAxis = Vector2I.Down;
		return built;
	}
	public IEnumerable<Vector2I> EligibleSizes(int minimumFloorTiles)
	{
		if (Validate().Length > 0) yield break;
		for (int x = MinimumSize.X; x <= MaximumSize.X; x++)
		for (int z = MinimumSize.Y; z <= MaximumSize.Y; z++)
		{
			int area = Shape == RoomShapeArchetype.LShape ? x * z - (x - LArmWidthTiles) * (z - LArmWidthTiles) : x * z;
			if (area >= minimumFloorTiles) yield return new(x, z);
		}
	}

	private ProceduralRoomBuilder.Footprint CreateLFootprint(Vector2I size, RandomNumberGenerator rng)
	{
		int corner = rng.RandiRange(0, 3);
		var floor = new bool[size.X, size.Y];
		for (int x = 0; x < size.X; x++)
		for (int z = 0; z < size.Y; z++)
		{
			int fromX = (corner & 1) == 0 ? x : size.X - 1 - x;
			int fromZ = (corner & 2) == 0 ? z : size.Y - 1 - z;
			floor[x, z] = fromX < LArmWidthTiles || fromZ < LArmWidthTiles;
		}
		return new(floor);
	}

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (property["name"].AsStringName() == PropertyName.LArmWidthTiles && Shape != RoomShapeArchetype.LShape)
			property["usage"] = (int)PropertyUsageFlags.NoEditor;
	}
}