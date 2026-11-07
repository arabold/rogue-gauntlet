namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Water is blocked terrain for walking actors, independent of optional bank fencing.</summary>
[TestSuite, RequireGodotRuntime]
public class SewerWaterTest
{
	private static RoomType Load(string name) => TestFactories.Load<RoomType>($"res://scenes/levels/rooms/room_types/{name}.tres");
	private static readonly RoomAssemblyOptions Sewer = new() { Realm = "sewer" };

	[TestCase]
	public void CisternsAlwaysHaveCenteredWaterAndReachableBanks()
	{
		for (ulong seed = 1; seed <= 12; seed++)
		{
			var room = RoomAssembler.Build(Load("cistern_hall"), seed * 523, Sewer);
			try
			{
				AssertInt(room.Pits.Count).IsGreaterEqual(4);
				float centerX = (room.Pits.Min(tile => tile.X) + room.Pits.Max(tile => tile.X)) / 2f;
				float centerZ = (room.Pits.Min(tile => tile.Y) + room.Pits.Max(tile => tile.Y)) / 2f;
				AssertFloat(Mathf.Abs(centerX - (room.Footprint.Width - 1) / 2f)).IsLessEqual(.5f);
				AssertFloat(Mathf.Abs(centerZ - (room.Footprint.Length - 1) / 2f)).IsLessEqual(.5f);
				AssertWaterTerrain(room);
			}
			finally { room.Room.Free(); }
		}
	}

	[TestCase(3, PoolSides.Left)]
	[TestCase(3, PoolSides.Right)]
	[TestCase(3, PoolSides.Both)]
	[TestCase(5, PoolSides.Left)]
	[TestCase(5, PoolSides.Right)]
	[TestCase(5, PoolSides.Both)]
	public void WalkwaysKeepADrySpineWithWaterReachingTheSideWalls(int width, PoolSides sides)
	{
		using var type = (RoomType)Load("drain_tunnel").Duplicate();
		using var shape = (RoomShape)type.Shapes[0].Duplicate();
		shape.MinimumSize = shape.MaximumSize = new Vector2I(width, 6);
		type.Shapes = [shape];
		using var water = (PoolStep)type.Steps.OfType<PoolStep>().Single().Duplicate();
		water.Sides = sides;
		type.Steps = [water, .. type.Steps.Where(step => step is not PoolStep)];
		for (ulong seed = 1; seed <= 4; seed++)
		{
			var room = RoomAssembler.Build(type, seed * 631, Sewer);
			try
			{
				for (int z = 0; z < room.Footprint.Length; z++)
				{
					AssertBool(room.IsFloor(new(width / 2, z))).IsTrue();
					if (z == 0 || z == room.Footprint.Length - 1) continue;
					AssertBool(room.Pits.Contains(new(0, z))).IsEqual(sides is PoolSides.Left or PoolSides.Both);
					AssertBool(room.Pits.Contains(new(width - 1, z))).IsEqual(sides is PoolSides.Right or PoolSides.Both);
				}
				AssertWaterTerrain(room);
			}
			finally { room.Room.Free(); }
		}
	}

	[TestCase("cistern_hall")]
	[TestCase("drain_tunnel")]
	public void OptionalFencesNeverRerollWaterOrBlockDoorways(string name)
	{
		using var type = (RoomType)Load(name).Duplicate();
		using var water = (PoolStep)type.Steps.OfType<PoolStep>().Single().Duplicate();
		type.Steps = [water];
		for (ulong seed = 1; seed <= 4; seed++)
		{
			water.FenceChance = 0;
			var bare = RoomAssembler.Build(type, seed * 617, Sewer);
			water.FenceChance = 1;
			var fenced = RoomAssembler.Build(type, seed * 617, Sewer);
			try
			{
				AssertArray(fenced.Pits.ToArray()).IsEqual(bare.Pits.ToArray());
				AssertInt(bare.Pieces.Count(piece => piece.Prop == water.Fence)).IsEqual(0);
				AssertInt(fenced.Pieces.Count(piece => piece.Prop == water.Fence)).IsGreater(0);
				AssertBool(fenced.IsConnected).IsTrue();
				foreach (var fence in fenced.Pieces.Where(piece => piece.Prop == water.Fence))
					foreach (var pit in fenced.Pits)
					{
						var center = RoomBlueprint.XZ(RoomFootprint.TileCenter(pit));
						AssertBool(RoomBlueprint.XZ(fence.Envelope).Intersects(new Rect2(center - Vector2.One * 2, Vector2.One * 4))).IsFalse();
					}
			}
			finally { bare.Room.Free(); fenced.Room.Free(); }
		}
	}

	[TestCase("cistern_hall", PoolSides.Random, 4)]
	[TestCase("drain_tunnel", PoolSides.Left, 2)]
	[TestCase("drain_tunnel", PoolSides.Right, 2)]
	[TestCase("drain_tunnel", PoolSides.Both, 4)]
	public void BankFencesJoinTheirTurnsWithCornerPieces(string name, PoolSides sides, int expectedCorners)
	{
		using var type = (RoomType)Load(name).Duplicate();
		using var water = (PoolStep)type.Steps.OfType<PoolStep>().Single().Duplicate();
		water.Sides = sides;
		water.FenceChance = 1;
		type.Steps = [water];
		for (ulong seed = 1; seed <= 4; seed++)
		{
			var room = RoomAssembler.Build(type, seed * 617, Sewer);
			try
			{
				var corners = room.Pieces.Where(piece => piece.Prop == water.FenceCorner).ToArray();
				var straights = room.Pieces.Where(piece => piece.Prop == water.Fence).ToArray();
				AssertInt(corners.Length).OverrideFailureMessage($"{name} {sides} seed {seed}: missing fence turns").IsEqual(expectedCorners);
				foreach (var corner in corners)
				foreach (var end in new Vector3[] { new(-2, .5f, 0), new(0, .5f, 2) })
				{
					var point = RoomBlueprint.XZ(corner.Transform * end);
					AssertBool(straights.Any(piece => RoomBlueprint.XZ(piece.Envelope).Grow(.06f).HasPoint(point)))
						.OverrideFailureMessage($"{name} {sides} seed {seed}: a corner leg does not meet a straight segment at {point}").IsTrue();
				}
				AssertBool(room.IsConnected).IsTrue();
				// Pit overhang must not exempt the dry arms from collision checks or leave failed placements behind.
				int before = room.Pieces.Count;
				var duplicate = new PlacementPlan();
				duplicate.Add(corners[0].Prop, corners[0].Variant, corners[0].Transform, optional: false);
				AssertBool(room.TryPlace(duplicate, Transform3D.Identity, allowPitOverhang: true)).IsFalse();
				AssertInt(room.Pieces.Count).IsEqual(before);
			}
			finally { room.Room.Free(); }
		}
	}

	[TestCase("res://scenes/player/player.tscn")]
	[TestCase("res://scenes/enemies/skeleton/skeleton_minion.tscn")]
	[TestCase("res://scenes/enemies/skeleton/skeleton_warrior.tscn")]
	[TestCase("res://scenes/enemies/skeleton/skeleton_crossbow.tscn")]
	[TestCase("res://scenes/enemies/rat/rat.tscn")]
	[TestCase("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")]
	public async Task ActorCollisionStopsAtUnfencedWater(string actorScene)
	{
		using var type = (RoomType)Load("cistern_hall").Duplicate();
		using var water = (PoolStep)type.Steps.OfType<PoolStep>().Single().Duplicate();
		water.Fence = null;
		type.Steps = [water];
		var room = RoomAssembler.Build(type, 523, Sewer);
		var root = new Node3D();
		root.AddChild(room.Room);
		// Use the authored actor's body shape and mask, without starting its gameplay components.
		var prototype = TestFactories.Load<PackedScene>(actorScene).Instantiate<CharacterBody3D>();
		var body = new CharacterBody3D { CollisionMask = prototype.CollisionMask, CollisionLayer = prototype.CollisionLayer };
		var shape = (CollisionShape3D)prototype.GetNode<CollisionShape3D>("CollisionShape3D").Duplicate();
		var bodyBounds = shape.Transform * shape.Shape.GetDebugMesh().GetAabb();
		body.AddChild(shape);
		prototype.Free();
		root.AddChild(body);
		TestFactories.AddToSceneTree(root);
		try
		{
			await body.ToSignal(body.GetTree(), SceneTree.SignalName.PhysicsFrame);
			await body.ToSignal(body.GetTree(), SceneTree.SignalName.PhysicsFrame);
			var bank = room.Pits.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).First();
			// Actor origins differ; start the bottom of each authored collider at the same height above the floor.
			var start = RoomFootprint.TileCenter(bank + Vector2I.Left) + Vector3.Up * (.26f - bodyBounds.Position.Y);
			using var hit = new KinematicCollision3D();
			AssertBool(body.TestMove(new Transform3D(Basis.Identity, start), Vector3.Right * Room.TileSize, hit)).IsTrue();
			AssertObject(hit.GetCollider()).IsSame(room.Room.GetNode("WalkingBarriers"));
			body.GlobalPosition = start;
			body.ResetPhysicsInterpolation();
			// A sweep alone misses rounded bodies climbing a low curb during sustained MoveAndSlide.
			for (int frame = 0; frame < 90; frame++)
			{
				await body.ToSignal(body.GetTree(), SceneTree.SignalName.PhysicsFrame);
				body.Velocity = new Vector3(8, body.IsOnFloor() ? 0 : body.Velocity.Y - .5f, 0);
				body.MoveAndSlide();
			}
			AssertFloat(body.GlobalPosition.X).IsLess(RoomFootprint.TileCenter(bank).X - Room.TileSize / 2f);
			// The actor-only barrier stays out of projectile collision queries above the original low curb.
			var ray = PhysicsRayQueryParameters3D.Create(start + Vector3.Up * 1.5f, start + Vector3.Right * Room.TileSize + Vector3.Up * 1.5f, 19);
			AssertInt(body.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count).IsEqual(0);
		}
		finally { root.Free(); }
	}

	private static void AssertWaterTerrain(RoomBlueprint room)
	{
		AssertBool(room.IsAcceptable).IsTrue();
		AssertString(RoomAssemblyTest.FloorCoverage(room)).IsEmpty();
		foreach (var pit in room.Pits)
		{
			AssertBool(room.IsFloor(pit)).IsFalse();
			AssertBool(room.IsReachable(RoomBlueprint.XZ(RoomFootprint.TileCenter(pit)))).IsFalse();
		}
		var floor = room.Room.FloorGridMap;
		AssertBool(floor.GetUsedCells().Any(cell => floor.MeshLibrary.GetItemName(floor.GetCellItem(cell)).StartsWith("floor_water_"))).IsFalse();
	}
}