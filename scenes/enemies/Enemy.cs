using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
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

		// Connect the health component's Died signal to the enemy behavior's Die method
		_healthComponent.Died += OnDie;

		// Hide the mesh until the animations are fully initialized to
		// prevent any flickering
		Visible = false;
	}

	public void Initialize(Vector3 startPosition)
	{
		Position = startPosition;
	}

	public void StartChasing(Node3D player)
	{
		// _enemyBehavior.SetBehavior(BehaviorState.Idle);
		// _enemyBehavior.SetTarget(player);
	}

	private void OnVisibilityNotifierScreenExited()
	{
		// QueueFree();
	}

	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		_enemyBehavior.OnHit();
		_movementComponent.Push(attackDirection, 2.0f);
		_healthComponent.TakeDamage(amount);
	}

	private void OnDie()
	{
		// Disable collision detection for the enemy
		CollisionLayer = 0;
		_enemyBehavior.OnDie();
		_movementComponent.Stop();
		SetPhysicsProcess(false);

		// Wait for death animation to finish
		GetTree().CreateTimer(2.0f).Connect("timeout", Callable.From(() =>
		{
			GD.Print($"{Name} is destroyed!");
			QueueFree();
		}));
	}

	public override void _PhysicsProcess(double delta)
	{
		Visible = true;
		Velocity = _movementComponent.GetVelocity();
		MoveAndSlide();

		LookAt(Position + _movementComponent.GetLookAtDirection(), Vector3.Up);
	}
}
