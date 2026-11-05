namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class MovementPhysicsTest
{
	private static (CharacterBody3D body, MovementComponent movement) CreateActor(float speed = 7.5f, float accel = 50f, float decel = 90f)
	{
		var body = new CharacterBody3D();
		var movement = new MovementComponent
		{
			Actor = body,
			Speed = speed,
			Acceleration = accel,
			Deceleration = decel,
			UseAcceleration = true,
			Gravity = 0f
		};
		body.AddChild(movement);
		movement._Ready();
		return (body, movement);
	}

	[TestCase]
	public void AccelerationRampsVelocityTowardTarget()
	{
		var (body, movement) = CreateActor(speed: 10f, accel: 50f, decel: 90f);
		movement.SetInputDirection(Vector3.Right);

		movement._PhysicsProcess(0.1);
		AssertFloat(movement.Velocity.X).IsEqualApprox(5.0f, 0.01f);
		AssertFloat(movement.Velocity.Z).IsEqualApprox(0f, 0.01f);

		movement._PhysicsProcess(0.1);
		AssertFloat(movement.Velocity.X).IsEqualApprox(10.0f, 0.01f);

		movement._PhysicsProcess(0.1);
		AssertFloat(movement.Velocity.X).IsEqualApprox(10.0f, 0.01f);

		body.QueueFree();
	}

	[TestCase]
	public void DecelerationBrakesVelocityWhenInputStops()
	{
		var (body, movement) = CreateActor(speed: 10f, accel: 100f, decel: 100f);
		movement.SetInputDirection(Vector3.Right);
		movement._PhysicsProcess(0.1); // reaches 10 m/s

		movement.SetInputDirection(Vector3.Zero);
		movement._PhysicsProcess(0.05); // brakes by 100 * 0.05 = 5 m/s
		AssertFloat(movement.Velocity.X).IsEqualApprox(5.0f, 0.01f);

		movement._PhysicsProcess(0.05); // brakes to 0
		AssertFloat(movement.Velocity.X).IsEqualApprox(0.0f, 0.01f);

		body.QueueFree();
	}

	[TestCase]
	public void ReversingDirectionBrakesWithDecelerationBeforeAccelerating()
	{
		var (body, movement) = CreateActor(speed: 10f, accel: 50f, decel: 100f);
		movement.SetInputDirection(Vector3.Right);
		movement._PhysicsProcess(0.2); // reaches 10 m/s

		// Snap input to opposite direction (Left)
		movement.SetInputDirection(Vector3.Left);

		// Frame 1: brakes toward 0 with Deceleration (100 * 0.05 = 5 m/s reduction)
		movement._PhysicsProcess(0.05);
		AssertFloat(movement.Velocity.X).IsEqualApprox(5.0f, 0.01f);

		// Frame 2: brakes to 0
		movement._PhysicsProcess(0.05);
		AssertFloat(movement.Velocity.X).IsEqualApprox(0.0f, 0.01f);

		// Frame 3: accelerates leftward with Acceleration (50 * 0.05 = 2.5 m/s)
		movement._PhysicsProcess(0.05);
		AssertFloat(movement.Velocity.X).IsEqualApprox(-2.5f, 0.01f);

		body.QueueFree();
	}

	[TestCase]
	public void StopZeroesVelocityImmediately()
	{
		var (body, movement) = CreateActor(speed: 10f, accel: 50f, decel: 90f);
		movement.SetInputDirection(Vector3.Right);
		movement._PhysicsProcess(0.2);
		AssertFloat(movement.Velocity.X).IsEqualApprox(10.0f, 0.01f);

		movement.Stop();
		AssertFloat(movement.Velocity.X).IsEqualApprox(0f, 0.001f);
		AssertBool(movement.IsMoving).IsFalse();

		body.QueueFree();
	}

	[TestCase]
	public void UseAccelerationFalseSnapsInstantly()
	{
		var (body, movement) = CreateActor(speed: 10f, accel: 50f, decel: 90f);
		movement.UseAcceleration = false;
		movement.SetInputDirection(Vector3.Right);

		movement._PhysicsProcess(0.016);
		AssertFloat(movement.Velocity.X).IsEqualApprox(10.0f, 0.01f);

		movement.SetInputDirection(Vector3.Zero);
		movement._PhysicsProcess(0.016);
		AssertFloat(movement.Velocity.X).IsEqualApprox(0f, 0.01f);

		body.QueueFree();
	}
}
