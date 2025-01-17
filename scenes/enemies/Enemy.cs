using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
public partial class Enemy : CharacterBody3D
{
	public Node3D Pivot;
	public EnemyBehavior EnemyBehavior;
	public MovementComponent MovementComponent;
	public HealthComponent HealthComponent;
	public HurtBoxComponent HurtBoxComponent;
	public FloatingHealthBar HealthBar;

	public bool IsDead => EnemyBehavior.IsDead;

	public override void _Ready()
	{
		base._Ready();

		Pivot = GetNode<Node3D>("Pivot");
		EnemyBehavior = GetNode<EnemyBehavior>("EnemyBehavior");
		MovementComponent = GetNode<MovementComponent>("MovementComponent");
		HealthComponent = GetNode<HealthComponent>("HealthComponent");
		HurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");
		HealthBar = GetNode<FloatingHealthBar>("FloatingHealthBar");

		// Connect the health component's Died signal to the enemy behavior's Die method
		HealthComponent.Died += OnDie;

		// Hide the mesh until the animations are fully initialized to
		// prevent any flickering
		Visible = false;
	}

	public void Initialize(Vector3 startPosition)
	{
		Position = startPosition;
	}

	private void OnDie()
	{
		// Disable collision detection for the enemy
		CollisionLayer = 0;
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
		MovementComponent.Move(this);
	}
}
