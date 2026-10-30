namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Room dressing follows authored intent while preserving required furniture and deterministic layout.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomDressingTest
{
	private const string Root = "res://scenes/levels/garrison/rooms/";

	[TestCase]
	public void HallUsesOnlyItsAssignedColumnPalette()
	{
		var recipe = TestFactories.Load<HallRoomGenerator>(Root + "hall/garrison_hall_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		recipe.ColumnItems = ["pillar"];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			AssertBool(recipe.PopulateRoom(room, rng, true, false)).IsTrue();
			AssertInt(room.DecorationGridMap.GetUsedCells().Count).IsGreater(0);
			AssertBool(room.DecorationGridMap.GetUsedCells().All(cell => room.DecorationGridMap.MeshLibrary.GetItemName(room.DecorationGridMap.GetCellItem(cell)) == "pillar")).IsTrue();
		}
		finally { room.Free(); }
	}

	[TestCase("dining")]
	[TestCase("barracks")]
	[TestCase("library")]
	public void DoorFixturesHaveRealSupportAndHonorLitAndUnlitVariants(string kind)
	{
		var recipe = TestFactories.Load<RoomGenerator>($"{Root}{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		var lighting = recipe.Dressing.Lighting;
		lighting.DoorChance = lighting.DoorPairChance = 1;
		lighting.BedChance = 0;
		recipe.Dressing.Clutter = null;
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			for (int lit = 0; lit <= 1; lit++)
			{
				lighting.LitChance = lit;
				rng.Seed = 23;
				AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
				var torches = room.GetChildren().OfType<RoomArrangement>().Where(a => a.GetMeta("arrangement_id").AsString().Contains("wall_torch")).ToArray();
				AssertInt(torches.Length).IsEqual(4);
				foreach (var torch in torches)
				{
					AssertString(torch.GetMeta("arrangement_id").AsString()).IsEqual(lit == 1 ? lighting.LitTorch.Id : lighting.UnlitTorch.Id);
					AssertBool(torch.GetNode<OmniLight3D>("Content/FlickerLight").Visible).IsEqual(lit == 1);
					AssertBool(torch.GetNode<GpuParticles3D>("Content/Fire/GPUParticles3D").Emitting).IsEqual(lit == 1);
					var anchor = torch.GetMeta("anchor").AsVector2I();
					AssertBool(new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right }.Any(offset => room.Map.IsWithinBounds(anchor.X + offset.X, anchor.Y + offset.Y) && room.Map.IsDoorway(anchor.X + offset.X, anchor.Y + offset.Y))).IsTrue();
				}
			}
		}
		finally { room.Free(); }
	}

	[TestCase("dining")]
	[TestCase("barracks")]
	[TestCase("library")]
	public void OptionalDressingPreservesRequiredFurnitureAndLayout(string kind)
	{
		var recipe = TestFactories.Load<RoomGenerator>($"{Root}{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
			string Furniture() => string.Join("|", room.GetChildren().OfType<RoomArrangement>()
				.Where(a => !a.GetMeta("is_room_detail", false).AsBool()).Select(a => $"{a.GetMeta("arrangement_id")}:{a.Transform}"));
			string original = Furniture();
			string floor = string.Join("|", room.FloorGridMap.GetUsedCells().Select(cell => $"{cell}:{room.FloorGridMap.GetCellItem(cell)}"));
			recipe.Dressing = null;
			rng.Seed = 23;
			AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
			AssertString(Furniture()).IsEqual(original);
			AssertString(string.Join("|", room.FloorGridMap.GetUsedCells().Select(cell => $"{cell}:{room.FloorGridMap.GetCellItem(cell)}"))).IsEqual(floor);
		}
		finally { room.Free(); }
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(2)]
	[TestCase(3)]
	public void BookshelfBacksStayOnOnePlaneDespiteWallProjections(int turn)
	{
		var definition = TestFactories.Load<RoomArrangementDefinition>("res://scenes/levels/arrangements/furniture/library_bookcase.tres");
		var instance = definition.Instantiate();
		try
		{
			var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
			var rotation = new Transform3D(basis, Vector3.Zero);
			var outward = ConnectedArrangementStrategy.Rotate(Vector2I.Up, turn);
			var normal = new Vector3(outward.X, 0, outward.Y);
			var plane = normal * definition.WallPlaneDistance;
			var support = instance.PlacementBounds;
			support.Size = new(support.Size.X, support.End.Y, support.Size.Z);
			support.Position = new(support.Position.X, 0, support.Position.Z);
			foreach (float projection in new[] { 0f, .13f, .4f })
			{
				var wall = rotation * new Aabb(-2, 0, -2.5f, 4, 4, 1 + projection);
				AssertBool(WallArrangementMount.TryPlace(support, basis, Vector3.Zero, outward, 0,
					[wall], out var transform, wallPlaneDistance: definition.WallPlaneDistance)).IsTrue();
				var bounds = transform * instance.PlacementBounds;
				float back = bounds.GetCenter().Dot(normal) + (bounds.Size.X * Mathf.Abs(normal.X) + bounds.Size.Z * Mathf.Abs(normal.Z)) / 2;
				AssertBool(Mathf.Abs(back - (definition.WallPlaneDistance - .005f)) < .001f).IsTrue();
				AssertBool(WallArrangementMount.OverlapsWall(bounds, wall, outward, 0, plane)).IsFalse();
				if (projection > 0) AssertBool(WallArrangementMount.Overlaps(bounds, wall)).IsTrue();
				var columnInFront = rotation * new Aabb(-.3f, 0, -1.3f, .6f, 3, .6f);
				AssertBool(WallArrangementMount.OverlapsWall(bounds, columnInFront, outward, 0, plane)).IsTrue();
			}
			var lintel = rotation * new Aabb(-2, 3, -2.5f, 4, 1, 1);
			AssertBool(WallArrangementMount.TryPlace(support, basis, Vector3.Zero, outward, 0,
				[lintel], out _, wallPlaneDistance: definition.WallPlaneDistance)).IsFalse();
		}
		finally { instance.Free(); }
	}

	[TestCase]
	public void StoragePaletteIncludesMixedCratesAndBarrelsWithValidBounds()
	{
		var cellar = TestFactories.Load<CellarRoomGenerator>(Root + "cellar/garrison_cellar_generator.tres");
		AssertInt(cellar.StorageArrangements.Length).IsGreater(3);
		foreach (var definition in cellar.StorageArrangements) AssertArray(definition.Validate()).IsEmpty();
	}
}