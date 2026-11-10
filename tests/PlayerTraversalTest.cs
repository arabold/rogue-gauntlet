namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The player's real body gets through generated floors: up the arrival stairs, from the arrival to the exit with every
/// door open, and down the exit stairs. Furniture never closes the way for a body the size of the player's, because
/// generation checks fit with the full radius of the largest ordinary body, and decor hanging on the walls never pins it.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class PlayerTraversalTest
{
	private const int FrameMilliseconds = 16;
	private const float Speed = 7.5f;
	/// <summary>Distance between the independent copies of a staged scenario, so their bodies never meet.</summary>
	private const float Spacing = 12;

	/// <summary>
	/// One floor per realm; 2983949030 is a Garrison floor a playtest got stuck on. The body climbs the arrival stairs,
	/// walks the navigation route to the exit landing and descends the exit stairs.
	/// </summary>
	[TestCase(2983949030ul, 1u)]
	[TestCase(7ul, 5u)]
	[TestCase(11ul, 8u)]
	public async Task PlayerClimbsTheStairsAndWalksFromTheArrivalToTheExit(ulong seed, uint depth)
	{
		var (runner, level) = await TestFactories.GenerateLevel(seed, depth);
		using var scope = runner;
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
		await runner.SimulateFrames(1, FrameMilliseconds);
		var start = level.Floor.Arrival.Landing.GlobalPosition;
		var exit = level.Floor.Descent.GetParent().GetNode<Node3D>("Landing").GlobalPosition;
		var (body, movement) = CreatePlayerBody(TestFactories.Load<PackedScene>("res://scenes/player/player.tscn"),
			TestFactories.Load<PackedScene>("res://scenes/player/stairs_trigger.tscn"), start + Vector3.Up * .1f);
		level.AddWorldNode(body);
		await runner.SimulateFrames(2, FrameMilliseconds);

		var up = level.Floor.Arrival.GetParent().GetNode<Node3D>("UpTransitionTrigger").GlobalPosition;
		AssertBool(await WalkTo(runner, body, movement, up)).OverrideFailureMessage($"Up-stairs: stuck at {body.GlobalPosition}").IsTrue();
		body.GlobalPosition = start + Vector3.Up * .1f;
		body.Velocity = Vector3.Zero;
		body.ResetPhysicsInterpolation();
		movement.ResetTransientMotion();
		await runner.SimulateFrames(2, FrameMilliseconds);

		var route = new List<Vector2>();
		float radius = EnemyBase.HorizontalRadius(body.GetNode<CollisionShape3D>("CollisionShape3D"));
		AssertBool(level.Navigation.TryFindPath(start, exit, radius, route)).OverrideFailureMessage("No route fits the player's body.").IsTrue();
		float length = GridPath.Length(start.Xz(), route);
		int budget = Mathf.CeilToInt(length / Speed * 1000 / FrameMilliseconds * 1.6f) + 60;
		int index = 0;
		for (int frame = 0; frame < budget && index < route.Count; frame++)
		{
			var position = body.GlobalPosition.Xz();
			if (position.DistanceTo(route[index]) < .4f) { index++; continue; }
			var heading = route[index] - position;
			movement.SetInputDirection(new Vector3(heading.X, 0, heading.Y).Normalized());
			await runner.SimulateFrames(1, FrameMilliseconds);
		}
		AssertFloat(body.GlobalPosition.HorizontalDistanceTo(exit)).OverrideFailureMessage(
			$"Stuck at {body.GlobalPosition} on waypoint {index}/{route.Count} toward {exit}").IsLess(1f);

		AssertBool(await WalkTo(runner, body, movement, level.Floor.Descent.GlobalPosition))
			.OverrideFailureMessage($"Down-stairs: stuck at {body.GlobalPosition}").IsTrue();
	}

	/// <summary>
	/// A dodge into a wall shelf, from any side and at any part of it, leaves the player free to walk away. Shelves hang
	/// inside the actor height band; an underside there would press a body into the floor and hold it. Every approach is
	/// staged on its own copy of the wall and dodged at the same time.
	/// </summary>
	[TestCase]
	public async Task DodgingIntoAWallShelfNeverPinsThePlayer()
	{
		const float DodgeSpeed = 40;
		var playerScene = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn");
		var stairsScene = TestFactories.Load<PackedScene>("res://scenes/player/stairs_trigger.tscn");
		var shelf = TestFactories.Load<Prop>("res://scenes/levels/rooms/props/wall/wall_shelf.tres");
		var approaches = (from end in new[] { -.6f, 0, .6f } from angle in new[] { -70f, -35f, 0, 35f, 70f } select (end, angle)).ToArray();
		var root = TestFactories.AddToSceneTree(new Node3D());
		root.AddChild(Box(new Vector3(approaches.Length * Spacing / 2, -.05f, 0), new Vector3(approaches.Length * Spacing + 20, .2f, 30), 1));
		// The wall's face runs along x = 2 of each copy; each shelf variant hangs on it, facing -x like a shelf on an east wall.
		var facingWest = new Basis(new Vector3(0, 0, 1), Vector3.Up, new Vector3(-1, 0, 0));
		var dodgers = new List<(CharacterBody3D Body, MovementComponent Movement, Vector3 Start, Vector3 Target, string Label)>();
		for (int copy = 0; copy < approaches.Length; copy++)
		{
			var (end, angle) = approaches[copy];
			float x = copy * Spacing;
			root.AddChild(Box(new Vector3(x + 2.5f, 2, 0), new Vector3(1, 4, 30), 2));
			for (int variant = 0; variant < shelf.VariantCount; variant++)
			{
				float z = variant * 6 - 6;
				root.AddChild(RoomBlueprint.CreateNode(new PlacedPiece(shelf, variant, new Transform3D(facingWest, new Vector3(x + 2.16f, shelf.MountHeight, z)), default)));
				var target = new Vector3(x + 1.4f, 0, z + end);
				var start = target + new Vector3(-2.5f, 0, 0).Rotated(Vector3.Up, Mathf.DegToRad(angle)) + Vector3.Up * .06f;
				var (body, movement) = CreatePlayerBody(playerScene, stairsScene, start);
				// Dodgers share the floor; only walls and shelves may stop them.
				body.CollisionLayer = 0;
				root.AddChild(body);
				dodgers.Add((body, movement, start, target, $"shelf {variant} toward {target} from {angle}°"));
			}
		}
		var tree = root.GetTree();
		await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		foreach (var dodger in dodgers) dodger.Movement.SetLocomotionOverride(this, (dodger.Target - dodger.Start).Horizontal(), DodgeSpeed);
		for (int frame = 0; frame < 12; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		var stopped = dodgers.Select(dodger => dodger.Body.GlobalPosition).ToArray();
		var fellShort = dodgers.Where((dodger, i) => stopped[i].X < dodger.Target.X - .5f).Select(dodger => dodger.Label).ToArray();
		AssertArray(fellShort).OverrideFailureMessage($"Dodged short of the shelf: {string.Join("; ", fellShort)}").IsEmpty();
		for (int i = 0; i < dodgers.Count; i++)
		{
			dodgers[i].Movement.ClearLocomotionOverride(this);
			dodgers[i].Movement.SetInputDirection((dodgers[i].Start - stopped[i]).Horizontal().Normalized());
		}
		for (int frame = 0; frame < 20; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		var pinned = dodgers.Where((dodger, i) => dodger.Body.GlobalPosition.HorizontalDistanceTo(stopped[i]) < 1)
			.Select(dodger => dodger.Label).ToArray();
		AssertArray(pinned).OverrideFailureMessage($"Pinned: {string.Join("; ", pinned)}").IsEmpty();
	}

	/// <summary>
	/// An open door stays solid around its opening: the player walks through the doorway beside the open leaf, but not
	/// through a jamb or the leaf itself. Each approach to each door scene is staged on its own copy and walked at once.
	/// </summary>
	[TestCase]
	public async Task OpenDoorsBlockTheirFrameAndLeafButNotTheOpening()
	{
		var playerScene = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn");
		var stairsScene = TestFactories.Load<PackedScene>("res://scenes/player/stairs_trigger.tscn");
		var scenes = new[] { "res://scenes/props/door.tscn", "res://scenes/props/cave_door.tscn" }.Select(path => TestFactories.Load<PackedScene>(path)).ToArray();
		// The leaf swings to -z about its hinge at x -0.82, so the clear opening lies to its +x side.
		var walks = new (Vector3 Start, Vector3 Direction, System.Func<Vector3, bool> Passed, string Failure)[]
		{
			(new(.25f, .06f, 3), Vector3.Forward, end => end.Z < -2, "Blocked in the opening"),
			(new(1.6f, .06f, 3), Vector3.Forward, end => end.Z > .5f, "Walked through a jamb"),
			(new(-3, .06f, -1.2f), Vector3.Right, end => end.X < -1, "Walked through the open leaf"),
		};
		var root = TestFactories.AddToSceneTree(new Node3D());
		int copies = scenes.Length * walks.Length;
		root.AddChild(Box(new Vector3(copies * Spacing / 2, -.05f, 0), new Vector3(copies * Spacing + 20, .2f, 20), 1));
		var walkers = new List<(CharacterBody3D Body, MovementComponent Movement, Vector3 Origin, int Walk, string Scene)>();
		for (int copy = 0; copy < copies; copy++)
		{
			var origin = new Vector3(copy * Spacing, 0, 0);
			var door = scenes[copy / walks.Length].Instantiate<Door>();
			door.Position = origin;
			root.AddChild(door);
			door.IsOpen = true;
			var walk = walks[copy % walks.Length];
			var (body, movement) = CreatePlayerBody(playerScene, stairsScene, origin + walk.Start);
			body.CollisionLayer = 0;
			root.AddChild(body);
			walkers.Add((body, movement, origin, copy % walks.Length, scenes[copy / walks.Length].ResourcePath));
		}
		var tree = root.GetTree();
		await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		foreach (var walker in walkers) walker.Movement.SetInputDirection(walks[walker.Walk].Direction);
		for (int frame = 0; frame < 60; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		foreach (var walker in walkers)
		{
			walker.Movement.Stop();
			var (_, _, passed, failure) = walks[walker.Walk];
			AssertBool(passed(walker.Body.GlobalPosition - walker.Origin)).OverrideFailureMessage($"{walker.Scene}: {failure}").IsTrue();
		}
	}

	/// <summary>Steers the body straight at <paramref name="target"/> until it stands within reach of it.</summary>
	private static async Task<bool> WalkTo(ISceneRunner runner, CharacterBody3D body, MovementComponent movement, Vector3 target, int frames = 120)
	{
		for (int frame = 0; frame < frames; frame++)
		{
			var to = (target - body.GlobalPosition).Horizontal();
			if (to.Length() < .7f) return true;
			movement.SetInputDirection(to.Normalized());
			await runner.SimulateFrames(1, FrameMilliseconds);
		}
		return false;
	}

	private static StaticBody3D Box(Vector3 center, Vector3 size, uint layer)
	{
		var body = new StaticBody3D { CollisionLayer = layer, CollisionMask = 0, Position = center };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		return body;
	}

	/// <summary>The authored player's body shape, layers and stairs trigger, driven by a movement component at its speed.</summary>
	private static (CharacterBody3D Body, MovementComponent Movement) CreatePlayerBody(PackedScene playerScene, PackedScene stairsScene, Vector3 position)
	{
		var prototype = playerScene.Instantiate<CharacterBody3D>();
		var body = new CharacterBody3D { CollisionLayer = prototype.CollisionLayer, CollisionMask = prototype.CollisionMask };
		body.AddChild(prototype.GetNode<CollisionShape3D>("CollisionShape3D").Duplicate());
		prototype.Free();
		var stairs = stairsScene.Instantiate<StairsTrigger>();
		body.AddChild(stairs);
		var movement = new MovementComponent { Actor = body, Speed = Speed, StairsTrigger = stairs };
		body.AddChild(movement);
		body.Position = position;
		return (body, movement);
	}
}