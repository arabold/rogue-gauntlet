using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
public partial class EnemyBase : CharacterBody3D, IDamageable
{
	public Node3D Pivot;
	public CollisionShape3D CollisionShape;
	public EnemyBehavior EnemyBehavior;
	public MovementComponent MovementComponent;
	public HealthComponent HealthComponent;
	public HurtBoxComponent HurtBoxComponent;
	public FloatingHealthBar HealthBar;
	public DeathComponent DeathComponent;
	public LootTableComponent LootTableComponent;

	public bool IsDead => EnemyBehavior.IsDead;

	public override void _Ready()
	{
		Pivot = GetNode<Node3D>("Pivot");
		CollisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		EnemyBehavior = GetNodeOrNull<EnemyBehavior>("EnemyBehavior");
		MovementComponent = GetNodeOrNull<MovementComponent>("MovementComponent");
		HealthComponent = GetNodeOrNull<HealthComponent>("HealthComponent");
		HurtBoxComponent = GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent");
		HealthBar = GetNodeOrNull<FloatingHealthBar>("FloatingHealthBar");
		DeathComponent = GetNodeOrNull<DeathComponent>("DeathComponent");
		LootTableComponent = GetNodeOrNull<LootTableComponent>("LootTableComponent");

		// Hide the mesh until the animations are fully initialized to
		// prevent any flickering
		Visible = false;
	}

	public override void _PhysicsProcess(double delta)
	{
		Visible = true;
	}

	public void TakeDamage(float amount, Vector3 attackDirection)
	{
		HurtBoxComponent?.TakeDamage(amount, attackDirection);
	}
}
