namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Checks that production keyboard sampling and dodge intent use the same responsive heading.</summary>
[RequireGodotRuntime]
[TestSuite]
public class PlayerDodgeInputTest
{
	[TestCase]
	public async Task SimultaneousDirectionAndDodgePressesWorkForWASDAndArrowKeys()
	{
		await AssertPhysicalDirectionAndDodge(Key.D);
		await AssertPhysicalDirectionAndDodge(Key.Right);
	}

	[TestCase]
	public async Task SteeringSetsTheDashHeadingThenCorrectsEarlyAndLocksAtTheOriginalDeadline()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		harness.PracticeTarget.Position = new Vector3(0, 0, -60);
		player.InputComponent.DeviceEnabled = false;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		// Steering picks the heading, not the facing or a knockback's velocity.
		player.MovementComponent.Push(Vector3.Left, 3);
		player.InputComponent.InputDirection = Vector3.Right;
		AssertThat(player.InputController.GetDodgeDirection()).IsEqual(Vector3.Right);
		player.MovementComponent.ResetTransientMotion();
		player.InputComponent.InputDirection = Vector3.Forward;
		player.InputController.PushIntent(PlayerIntent.Dodge);
		player.InputController._PhysicsProcess(0.016);

		AssertBool(player.DodgeComponent.IsDodging).IsTrue();
		AssertThat(player.MovementComponent.LocomotionDirection).IsEqual(Vector3.Forward);
		AssertFloat(player.MovementComponent.LocomotionSpeed).IsGreater(player.Stats.Speed);

		player.InputComponent.InputDirection = Vector3.Right;
		player.InputController._PhysicsProcess(0.016);
		AssertThat(player.MovementComponent.LocomotionDirection).IsEqual(Vector3.Right);
		AssertThat(player.MovementComponent.LookAtDirection).IsEqual(Vector3.Forward);

		player.DodgeComponent.SetPhysicsProcess(false);
		player.DodgeComponent._PhysicsProcess(0.05);
		player.InputComponent.InputDirection = Vector3.Left;
		player.InputController._PhysicsProcess(0.016);
		AssertFloat(player.DodgeComponent.ElapsedTime).IsGreater(0.04f);
		AssertThat(player.MovementComponent.LocomotionDirection).IsEqual(Vector3.Right);
	}

	[TestCase]
	public async Task RecentlyReleasedSteeringSetsTheInitialDashHeading()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		harness.PracticeTarget.Position = new Vector3(0, 0, -60);
		player.InputComponent.DeviceEnabled = false;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		player.InputComponent.InputDirection = Vector3.Right;
		player.InputController._PhysicsProcess(0.016);
		player.InputComponent.InputDirection = Vector3.Zero;
		AssertThat(player.InputController.GetDodgeDirection()).IsEqual(Vector3.Right);

		player.InputController.PushIntent(PlayerIntent.Dodge);
		player.InputController._PhysicsProcess(0.016);

		AssertBool(player.DodgeComponent.IsDodging).IsTrue();
		AssertThat(player.MovementComponent.LocomotionDirection).IsEqual(Vector3.Right);
	}

	[TestCase]
	public async Task FreshDefenseResetClearsRecentSteering()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		harness.PracticeTarget.Position = new Vector3(0, 0, -60);
		player.InputComponent.DeviceEnabled = false;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		player.InputComponent.InputDirection = Vector3.Right;
		player.InputController._PhysicsProcess(0.016);

		player.InputController.RequireFreshDefenseInput();
		player.InputComponent.InputDirection = Vector3.Zero;
		player.InputController.PushIntent(PlayerIntent.Dodge);
		player.InputController._PhysicsProcess(0.016);

		AssertBool(player.DodgeComponent.IsDodging).IsTrue();
		AssertThat(player.MovementComponent.LocomotionDirection).IsEqual(Vector3.Forward);
	}

	private static async Task AssertPhysicalDirectionAndDodge(Key directionKey)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		Vector3 initialFacing = player.MovementComponent.LookAtDirection;

		try
		{
			ParseKey(directionKey, pressed: true);
			ParseKey(Key.Space, pressed: true);
			Input.FlushBufferedEvents();
			await runner.SimulateFrames(1, 16);

			Vector3 expectedDirection = player.InputComponent.InputDirection.Normalized();
			AssertBool(expectedDirection.LengthSquared() > 0.9f).IsTrue();
			AssertBool(player.DodgeComponent.IsDodging).IsTrue();
			AssertBool(player.MovementComponent.LocomotionDirection.IsEqualApprox(expectedDirection)).IsTrue();
			AssertBool(player.MovementComponent.LookAtDirection.Dot(initialFacing) > 0.99f).IsTrue();
		}
		finally
		{
			ParseKey(directionKey, pressed: false);
			ParseKey(Key.Space, pressed: false);
			Input.FlushBufferedEvents();
			await runner.SimulateFrames(2, 16);
		}
	}

	private static void ParseKey(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey
	{
		PhysicalKeycode = key,
		Keycode = key,
		Pressed = pressed
	});
}