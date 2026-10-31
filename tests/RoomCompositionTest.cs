namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Physical circulation, coherent surface choices and independently editable assemblies share the production pipeline.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomCompositionTest
{
	private static T Load<T>(string kind) where T : RoomGenerator => TestFactories.Load<T>(
		$"res://scenes/levels/garrison/rooms/{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);

	[TestCase("partitioned_store", 0)]
	[TestCase("partitioned_store", 1)]
	[TestCase("partitioned_store", 2)]
	[TestCase("fenced_walkway", 0)]
	[TestCase("fenced_walkway", 1)]
	[TestCase("fenced_walkway", 2)]
	public void BoundariesRemainAccessibleAndSurviveEveryRotation(string kind, int variant)
	{
		var recipe = Load<ModularRoomGenerator>(kind);
		recipe.Layouts = [recipe.Layouts[variant]];
		AssertArray(recipe.Validate()).IsEmpty();
		for (ulong seed = 23; seed <= 24; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = recipe.BuildRoom(rng);
			try
			{
				var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
				AssertBool(slots.Any(slot => slot.Definition.Id.Contains("fence") || slot.Definition.Id.Contains("half_wall"))).IsTrue();
				AssertInt(slots.Select(slot => slot.Id).Distinct().Count()).IsEqual(slots.Length);
				for (int turn = 0; turn < 4; turn++) { PrisonRoomTest.AssertRequiredSlotsFit(room, recipe); room.Rotate(1); }
				ProceduralRoomBuilder.SetOwnersForPacking(room, room);
				using var scene = new PackedScene();
				AssertBool(scene.Pack(room) == Error.Ok).IsTrue();
				var restored = scene.Instantiate<Room>();
				try { restored.BakeTileMap(); PrisonRoomTest.AssertRequiredSlotsFit(restored, recipe); }
				finally { restored.Free(); }
			}
			finally { room.Free(); }
		}
	}

	[TestCase]
	public void ASealedBayIsRejectedWhileItsSingleEntranceConnectsEveryFreeArea()
	{
		var recipe = Load<ModularRoomGenerator>("partitioned_store");
		var boundary = (RoomBoundaryLayout)((CompositeRoomLayout)recipe.Layouts[0].Contents).Steps[0];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			var baseLayout = recipe.Layouts[0].BaseLayout;
			baseLayout.MinimumSize = baseLayout.MaximumSize = new(4, 4);
			AssertBool(baseLayout.Populate(room, recipe.ShellBuilder, rng)).IsTrue();
			var context = new RoomFurnishingContext(room, 23, 1);
			var transform = new Transform3D(Basis.Identity, new Vector3(-1.5f, 0, -1.5f));
			AssertBool(RoomBoundaryPatterns.PlaceBay(context, boundary.Palette, transform, new(6, 6), 4, true, "bay")).IsTrue();
			AssertBool(context.IsConnected).IsTrue();
			int pieces = context.Placements.Count;
			var closed = context.Fork();
			AssertBool(RoomBoundaryPatterns.PlaceRun(closed, boundary.Palette, transform * new Vector3(0, 0, 6), transform * new Vector3(4, 0, 6), "seal")).IsTrue();
			AssertBool(closed.IsConnected).IsFalse();
			AssertInt(context.Placements.Count).IsEqual(pieces);
		}
		finally { room.Free(); }
	}

}