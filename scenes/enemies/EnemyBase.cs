using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
public partial class EnemyBase : CharacterBody3D, IDamageable, IStaggerable
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
	public ActorActionGateComponent ActionGate { get; protected set; }
	public GuardComponent GuardComponent => GetNodeOrNull<GuardComponent>("GuardComponent");
	public DodgeComponent DodgeComponent => GetNodeOrNull<DodgeComponent>("DodgeComponent");
	public EnemyDefenseController DefenseController => GetNodeOrNull<EnemyDefenseController>("EnemyDefenseController");
	public AttackController AttackController => GetNodeOrNull<AttackController>("AttackController");

	public bool IsDead => EnemyBehaviorComponent?.IsDead ?? HealthComponent?.IsDead ?? false;

	public bool TryStagger(float duration)
	{
		return ActionGate?.TryStagger(duration) ?? false;
	}

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

		ActionGate = GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent");
		if (ActionGate == null)
		{
			ActionGate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
			AddChild(ActionGate);
		}

		MovementComponent.ActionGate = ActionGate;
		HealthComponent.Died += () => ActionGate.Disable();

		if (GuardComponent != null)
		{
			GuardComponent.Actor ??= this;
			GuardComponent.MovementComponent ??= MovementComponent;
			GuardComponent.ActionGate ??= ActionGate;
		}
		if (DodgeComponent != null)
		{
			DodgeComponent.Actor ??= this;
			DodgeComponent.MovementComponent ??= MovementComponent;
			DodgeComponent.ActionGate ??= ActionGate;
		}
		if (DefenseController != null)
		{
			DefenseController.Enemy ??= this;
			DefenseController.GuardComponent ??= GuardComponent;
			DefenseController.DodgeComponent ??= DodgeComponent;
		}

		var levelSeed = this.GetAncestorOrNull<Level>()?.MapGenerator?.Seed ?? 42;
		ConfigureCombatRandom(levelSeed, GetMeta("persistent_id", IsInsideTree() ? GetPath().ToString() : Name.ToString()).AsString());
		ActionGate.AnimationComponent ??= GetNodeOrNull<CombatAnimationComponent>("CombatAnimationComponent");

		// Hide the mesh until the animations are fully initialized to
		// prevent any flickering
		Visible = false;

		// Configure Enemy HurtBox to only take damage from Player, Boss, or environmental hazards (traps)
		HurtBoxComponent?.RegisterDefenseEvaluator(DodgeComponent);
		HurtBoxComponent?.RegisterDefenseEvaluator(GuardComponent);

		if (HurtBoxComponent != null)
		{
			HurtBoxComponent.DamageFilter = DamageSourceFlags.Player | DamageSourceFlags.Boss | DamageSourceFlags.Environment;
		}

		// See-through-wall silhouette so a hidden enemy reads from any camera angle. Properties
		// are set before AddChild so the component builds its silhouettes with them in place.
		var xray = new OcclusionXrayComponent
		{
			Name = "OcclusionXrayComponent",
			Category = XrayCategory.Monster,
			XrayColor = new Color(1f, 0.32f, 0.32f, 0.85f),
			TargetRoot = "../Pivot",
		};
		AddChild(xray);
	}

	/// <summary>Independent combat streams keyed by the spawn's stable source identity.</summary>
	public void ConfigureCombatRandom(ulong levelSeed, string sourceId)
	{
		AttackController?.ConfigureRandom(GenerationRandom.DeriveSeed(levelSeed, "combat.attack", sourceId));
		DefenseController?.ConfigureRandom(GenerationRandom.DeriveSeed(levelSeed, "combat.defense", sourceId));
	}

	public override void _PhysicsProcess(double delta)
	{
		Visible = true;
		SetPhysicsProcess(false);
	}

	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		// Forward the damage to the hurtbox component which
		// does the actual damage calculations
		HurtBoxComponent?.TakeDamage(accuracy, amount, attackDirection, attacker, attackerFaction);
	}
}