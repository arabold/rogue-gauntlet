using Godot;
using System;
using System.Collections.Generic;

public partial class Enemy : CharacterBody3D, IDamageable
{
	private EnemyBehavior _enemyBehavior;
	private MovementComponent _movementComponent;
	private HealthComponent _healthComponent;

	public override void _Ready()
	{
		base._Ready();

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
		_movementComponent.Push(attackDirection, 2.0f);
		_healthComponent.TakeDamage(amount);
	}
}
