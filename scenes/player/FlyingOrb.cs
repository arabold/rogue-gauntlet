using Godot;
using System;

public partial class FlyingOrb : Node3D
{
	// Distance from the player
	[Export] public float OrbitRadius = 2.0f;
	// Speed of rotation (radians per second) 
	[Export] public float OrbitSpeed = 10.0f;
	[Export] public float OrbitHeight = 1.0f;
	// Damage dealt by the orb
	[Export] public int Damage = 20;

	private float _angle = 0.0f; // Current angle of the orb around the player
	private Node3D _player; // Reference to the player

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Find the player in the scene tree
		_player = GetParent<Node3D>();

		// Connect the Area3D signals
		var area = GetNode<Area3D>("Area3D");
		area.BodyEntered += OnBodyEntered;
		area.AreaEntered += OnAreaEntered;
	}


	public override void _Process(double delta)
	{
		// Increment the angle based on the orbit speed
		_angle += (float)(OrbitSpeed * delta);

		// Keep the angle within 0 to 2π for consistency
		if (_angle > Mathf.Tau)
		{
			_angle -= Mathf.Tau;
		}

		// Calculate the orb's position in circular motion around the player
		Vector3 orbitPosition = new Vector3(
			Mathf.Cos(_angle) * OrbitRadius,
			OrbitHeight,
			Mathf.Sin(_angle) * OrbitRadius
		);

		// Update the orb's global position relative to the player
		GlobalTransform = new Transform3D(GlobalTransform.Basis, _player.GlobalTransform.Origin + orbitPosition);
	}

	private void OnBodyEntered(Node3D body)
	{
		// Check if the object implements IDamageable
		if (body is IDamageable damageable)
		{
			damageable.TakeDamage(Damage); // Apply damage
		}
	}

	private void OnAreaEntered(Area3D area)
	{
		GD.Print($"Orb collided with another area: {area.Name}");
	}
}
