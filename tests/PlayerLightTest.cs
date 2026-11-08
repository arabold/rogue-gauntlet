namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The player's light never shines from beyond a wall the player faces, and keeps its spot in open space.</summary>
[TestSuite, RequireGodotRuntime]
public class PlayerLightTest
{
	[TestCase]
	public async Task TheLightStopsInFrontOfAWallThePlayerFacesAndReturnsInOpenSpace()
	{
		var player = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>());
		player.MovementComponent.SetPhysicsProcess(false);
		// A one-unit wall whose near face is 0.6 from the player's center: the light's spot two units ahead lies past it.
		var wall = TestFactories.AddToSceneTree(new StaticBody3D { CollisionLayer = 2, CollisionMask = 0, Position = new Vector3(1.1f, 2, 0) });
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, 4, 8) } });
		var pivot = player.GetNode<Node3D>("Pivot");
		var light = player.GetNode<PlayerLight>("Pivot/PlayerLight").Light;

		pivot.Rotation = Vector3.Up * Mathf.Pi / 2; // Facing +X, into the wall.
		await PhysicsFrames(player, 2);
		AssertFloat(light.GlobalPosition.X).OverrideFailureMessage("The light passed the wall's near face.").IsLess(0.6f);
		AssertFloat(light.GlobalPosition.X).OverrideFailureMessage("The light fell behind the player.").IsGreater(0f);

		pivot.Rotation = Vector3.Down * Mathf.Pi / 2; // Facing -X, into open space.
		await PhysicsFrames(player, 2);
		AssertFloat(light.GlobalPosition.X).IsEqualApprox(-2f, .01f);
	}

	private static async Task PhysicsFrames(Node node, int count)
	{
		for (int i = 0; i < count; i++) await node.ToSignal(node.GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
}