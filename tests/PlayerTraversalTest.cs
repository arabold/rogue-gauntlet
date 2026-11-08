namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The player's real body gets through generated floors: up and down both staircases, and from the arrival to the
/// exit with every door open. Furniture never closes the way for a body the size of the player's, because generation
/// checks fit with the full radius of the largest ordinary body, and decor hanging on the walls never pins it.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class PlayerTraversalTest
{
	private const int FrameMilliseconds = 16;
	private const float Speed = 7.5f;

	[TestCase(23ul, 1u)]
	[TestCase(2983949030ul, 1u)]
	public async Task PlayerClimbsAndDescendsBothStaircases(ulong seed, uint depth)
	{
		var (runner, level) = await TestFactories.GenerateLevel(seed, depth);
		using var scope = runner;
		var arrivalStairs = level.Floor.Arrival.GetParent<Node3D>();
		var descentStairs = level.Floor.Descent.GetParent<Node3D>();
		foreach (var (stairs, trigger) in new[] { (arrivalStairs, (Node3D)level.Floor.Arrival.GetParent().GetNode<Node3D>("UpTransitionTrigger")),
			(descentStairs, level.Floor.Descent) })
		{
			var (body, movement) = AddPlayerBody(level, stairs.GetNode<Node3D>("Landing").GlobalPosition);
			await runner.SimulateFrames(2, FrameMilliseconds);
			bool reached = false;
			for (int frame = 0; frame < 120 && !reached; frame++)
			{
				var to = trigger.GlobalPosition - body.GlobalPosition;
				to.Y = 0;
				reached = to.Length() < .7f;
				movement.SetInputDirection(to.Normalized());
				await runner.SimulateFrames(1, FrameMilliseconds);
			}
			AssertBool(reached).OverrideFailureMessage($"{stairs.Name}: stuck at {body.GlobalPosition}").IsTrue();
			body.QueueFree();
		}
	}

	[TestCase(2983949030ul, 1u)]
	[TestCase(23ul, 1u)]
	[TestCase(7ul, 5u)]
	[TestCase(11ul, 8u)]
	public async Task PlayerWalksFromTheArrivalToTheExit(ulong seed, uint depth)
	{
		var (runner, level) = await TestFactories.GenerateLevel(seed, depth);
		using var scope = runner;
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
		await runner.SimulateFrames(1, FrameMilliseconds);
		var start = level.Floor.Arrival.Landing.GlobalPosition;
		var exit = level.Floor.Descent.GetParent().GetNode<Node3D>("Landing").GlobalPosition;
		var (body, movement) = AddPlayerBody(level, start);
		var route = new List<Vector2>();
		float radius = EnemyBase.HorizontalRadius(body.GetNode<CollisionShape3D>("CollisionShape3D"));
		AssertBool(level.Navigation.TryFindPath(start, exit, radius, route)).OverrideFailureMessage("No route fits the player's body.").IsTrue();
		await runner.SimulateFrames(2, FrameMilliseconds);
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
	}

	/// <summary>
	/// A dodge into a wall shelf, from any side and at any part of it, leaves the player free to walk away. Shelves hang
	/// inside the actor height band; an underside there would press a body into the floor and hold it.
	/// </summary>
	[TestCase]
	public async Task DodgingIntoAWallShelfNeverPinsThePlayer()
	{
		const float DodgeSpeed = 40;
		var root = TestFactories.AddToSceneTree(new Node3D());
		root.AddChild(Box(new Vector3(0, -.05f, 0), new Vector3(30, .2f, 30), 1));
		// The wall's face runs along x = 2; each shelf variant hangs on it, facing -x like a shelf on an east wall.
		root.AddChild(Box(new Vector3(2.5f, 2, 0), new Vector3(1, 4, 30), 2));
		var shelf = TestFactories.Load<Prop>("res://scenes/levels/rooms/props/wall/wall_shelf.tres");
		var facingWest = new Basis(new Vector3(0, 0, 1), Vector3.Up, new Vector3(-1, 0, 0));
		var shelves = new List<float>();
		for (int variant = 0; variant < shelf.VariantCount; variant++)
		{
			float z = variant * 6 - 6;
			shelves.Add(z);
			root.AddChild(RoomBlueprint.CreateNode(new PlacedPiece(shelf, variant, new Transform3D(facingWest, new Vector3(2.16f, shelf.MountHeight, z)), default)));
		}
		var (body, movement) = CreatePlayerBody(Vector3.Zero);
		root.AddChild(body);
		var tree = root.GetTree();
		var pinned = new List<string>();
		foreach (float z in shelves)
		foreach (float end in new[] { -.6f, 0, .6f })
		foreach (float angle in new[] { -70f, -35f, 0, 35f, 70f })
		{
			var target = new Vector3(1.4f, 0, z + end);
			var start = target + new Vector3(-2.5f, 0, 0).Rotated(Vector3.Up, Mathf.DegToRad(angle)) + Vector3.Up * .06f;
			body.GlobalPosition = start;
			body.Velocity = Vector3.Zero;
			movement.ResetTransientMotion();
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			movement.SetLocomotionOverride(this, (target - start).Horizontal(), DodgeSpeed);
			for (int frame = 0; frame < 12; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			movement.ClearLocomotionOverride(this);
			var stopped = body.GlobalPosition;
			movement.SetInputDirection((start - stopped).Horizontal().Normalized());
			for (int frame = 0; frame < 20; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			movement.Stop();
			if (body.GlobalPosition.HorizontalDistanceTo(stopped) < 1) pinned.Add($"shelf z={z} toward {target} from {angle}° stopped at {stopped}");
		}
		AssertArray(pinned.ToArray()).OverrideFailureMessage($"Pinned: {string.Join("; ", pinned)}").IsEmpty();
	}

	/// <summary>
	/// An open door stays solid around its opening: the player walks through the doorway beside the open leaf, but not
	/// through a jamb or the leaf itself.
	/// </summary>
	[TestCase("res://scenes/props/door.tscn")]
	[TestCase("res://scenes/props/cave_door.tscn")]
	public async Task OpenDoorsBlockTheirFrameAndLeafButNotTheOpening(string scene)
	{
		var root = TestFactories.AddToSceneTree(new Node3D());
		root.AddChild(Box(new Vector3(0, -.05f, 0), new Vector3(20, .2f, 20), 1));
		var door = TestFactories.Load<PackedScene>(scene).Instantiate<Door>();
		root.AddChild(door);
		door.IsOpen = true;
		var (body, movement) = CreatePlayerBody(Vector3.Zero);
		root.AddChild(body);
		var tree = root.GetTree();

		async Task<Vector3> Walk(Vector3 start, Vector3 direction, int frames = 60)
		{
			body.GlobalPosition = start;
			body.Velocity = Vector3.Zero;
			movement.ResetTransientMotion();
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			movement.SetInputDirection(direction);
			for (int frame = 0; frame < frames; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
			movement.Stop();
			return body.GlobalPosition;
		}

		// The leaf swings to -z about its hinge at x -0.82, so the clear opening lies to its +x side.
		AssertFloat((await Walk(new Vector3(.25f, .06f, 3), Vector3.Forward)).Z).OverrideFailureMessage("Blocked in the opening").IsLess(-2);
		AssertFloat((await Walk(new Vector3(1.6f, .06f, 3), Vector3.Forward)).Z).OverrideFailureMessage("Walked through a jamb").IsGreater(.5f);
		AssertFloat((await Walk(new Vector3(-3, .06f, -1.2f), Vector3.Right)).X).OverrideFailureMessage("Walked through the open leaf").IsLess(-1);
	}

	private static StaticBody3D Box(Vector3 center, Vector3 size, uint layer)
	{
		var body = new StaticBody3D { CollisionLayer = layer, CollisionMask = 0, Position = center };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		return body;
	}

	/// <summary>The authored player's body shape, layers and stairs trigger, driven by a movement component at its speed.</summary>
	private static (CharacterBody3D Body, MovementComponent Movement) AddPlayerBody(Level level, Vector3 position)
	{
		var (body, movement) = CreatePlayerBody(position + Vector3.Up * .1f);
		level.AddWorldNode(body);
		return (body, movement);
	}

	private static (CharacterBody3D Body, MovementComponent Movement) CreatePlayerBody(Vector3 position)
	{
		var prototype = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<CharacterBody3D>();
		var body = new CharacterBody3D { CollisionLayer = prototype.CollisionLayer, CollisionMask = prototype.CollisionMask };
		body.AddChild(prototype.GetNode<CollisionShape3D>("CollisionShape3D").Duplicate());
		prototype.Free();
		var stairs = TestFactories.Load<PackedScene>("res://scenes/player/stairs_trigger.tscn").Instantiate<StairsTrigger>();
		body.AddChild(stairs);
		var movement = new MovementComponent { Actor = body, Speed = Speed, StairsTrigger = stairs };
		body.AddChild(movement);
		body.Position = position;
		return (body, movement);
	}
}