using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
public partial class EnemyBase : CharacterBody3D, IDamageable
{
	public Node3D Pivot;
	public CollisionShape3D CollisionShape;
	public EnemyBehaviorComponent EnemyBehaviorComponent;
	public MovementComponent MovementComponent;
	public HealthComponent HealthComponent;
	public HurtBoxComponent HurtBoxComponent;
	public FloatingHealthBar HealthBar;
	public DeathComponent DeathComponent;
	public LootTableComponent LootTableComponent;

	public bool IsDead => EnemyBehaviorComponent.IsDead;

	public override void _Ready()
	{
		Pivot = GetNode<Node3D>("Pivot");
		CollisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		EnemyBehaviorComponent = GetNodeOrNull<EnemyBehaviorComponent>("EnemyBehaviorComponent");
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

	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection)
	{
		// Forward the damage to the hurtbox component which
		// does the actual damage calculations
		HurtBoxComponent?.TakeDamage(accuracy, amount, attackDirection);
	}
}
