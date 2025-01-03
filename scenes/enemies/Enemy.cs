using Godot;
using System;
using System.Collections.Generic;

public partial class Enemy : CharacterBody3D, IDamageable
{
	private Node3D _pivot;
	private EnemyBehavior _enemyBehavior;
	private MovementComponent _movementComponent;
	private HealthComponent _healthComponent;

	public override void _Ready()
	{
		base._Ready();

		_pivot = GetNode<Node3D>("Pivot");
		if (_pivot == null)
		{
			GD.PrintErr("Pivot node not found! Make sure to add a Node3D named 'Pivot' as a child of the Enemy.");
			QueueFree();
			return;
		}

		_enemyBehavior = GetNode<EnemyBehavior>("EnemyBehavior");
		if (_enemyBehavior == null)
		{
			GD.PrintErr("EnemyBehavior node not found!");
			QueueFree();
			return;
		}

		_movementComponent = GetNode<MovementComponent>("MovementComponent");
		if (_movementComponent == null)
		{
			GD.PrintErr("MovementComponent node not found!");
			QueueFree();
			return;
		}

		_healthComponent = GetNode<HealthComponent>("HealthComponent");
		if (_healthComponent == null)
		{
			GD.PrintErr("HealthComponent node not found!");
			QueueFree();
			return;
		}

		// Rotate the pivot 180 degrees to correct initial orientation
		_pivot.RotateY(Mathf.Pi);

		// Hide the mesh until the animations are fully initialized to
		// prevent any flickering
		Visible = false;
	}

	public override void _PhysicsProcess(double delta)
	{
		Visible = true;
	}

	public void Initialize(Vector3 startPosition)
	{
		Position = startPosition;
	}

	public void StartChasing(Node3D player)
	{
		GD.Print("Chasing player: " + player.Name);
		_enemyBehavior.SetChasing(player);
	}

	private void OnVisibilityNotifierScreenExited()
	{
		QueueFree();
	}

	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		_enemyBehavior.Hit();
		_movementComponent.Push(attackDirection, 5.0f);
		_healthComponent.TakeDamage(amount);
	}
}
