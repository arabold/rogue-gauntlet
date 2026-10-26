using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
public partial class EnemyBase : CharacterBody3D, IDamageable, ICombatDamageable, IStaggerable
{
	public Node3D Pivot;
	public CollisionShape3D CollisionShape;
	[Export] public InvisibilityComponent Invisibility { get; set; }

	[Export] public EnemyBehaviorComponent EnemyBehaviorComponent;
	[Export] public MovementComponent MovementComponent;
	[Export] public HealthComponent HealthComponent;
	[Export] public HurtBoxComponent HurtBoxComponent;
	public FloatingHealthBar HealthBar;
	[Export] public DeathComponent DeathComponent;
	[Export] public LootTableComponent LootTableComponent;
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public GuardComponent GuardComponent { get; set; }
	[Export] public DodgeComponent DodgeComponent { get; set; }
	[Export] public EnemyDefenseController DefenseController { get; set; }
	[Export] public AttackController AttackController { get; set; }

	[Export] public LargeActorFitComponent LargeActorFit { get; set; }
	[Export] public MonsterDefinition Definition { get; set; }
	public bool DefinitionValid { get; private set; } = true;
	private Level _level;

	/// <summary>Initialize authored defaults before child readiness; never mutate the definition.</summary>
	public override void _EnterTree()
	{
		if (Engine.IsEditorHint()) return;
		_level = this.GetAncestorOrNull<Level>();
		var errors = MonsterSceneValidator.Validate(this);
		DefinitionValid = errors.Length == 0;
		if (!DefinitionValid)
		{
			foreach (var error in errors) GD.PushError($"{Name}: {error}");
			ProcessMode = ProcessModeEnum.Disabled;
			return;
		}
		HealthComponent.SetHealth(Definition.MaxHealth, Definition.MaxHealth);
		MovementComponent.Speed = Definition.MoveSpeed;
		DeathComponent.Xp = Definition.XpReward;
		LootTableComponent.ConfigureProfile(Definition.Loot);
		EnemyBehaviorComponent.Profile = Definition.Behavior;
	}

	public bool IsDead => EnemyBehaviorComponent?.IsDead ?? HealthComponent?.IsDead ?? false;

	public bool TryStagger(float duration)
	{
		return ActionGate?.TryStagger(duration) ?? false;
	}

	public override void _Ready()
	{
		if (Engine.IsEditorHint() || !DefinitionValid) return;
		Pivot = GetNode<Node3D>("Pivot");
		CollisionShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		HealthBar = GetNodeOrNull<FloatingHealthBar>("FloatingHealthBar");

		HealthComponent.Died += () => ActionGate.Disable();

		// An enemy outside a level (previews, harnesses) plays the default run; each still gets its own combat streams.
		ConfigureCombatRandom(_level?.Seed ?? RunState.DefaultSeed, GetMeta("persistent_id", GetPath().ToString()).AsString());

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
			Invisibility = Invisibility,
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
		if (Engine.IsEditorHint() || !DefinitionValid) return;
		Visible = true;
		SetPhysicsProcess(false);
	}

	/// <summary>Forward complete contacts through the authored hurtbox even when a child collider was hit first.</summary>
	public DamageResolution ResolveDamage(in DamageContext contact) => HurtBoxComponent != null
		? HurtBoxComponent.ResolveDamage(in contact) : new DamageResolution(DamageOutcome.Filtered, 0);

	public void TakeDamage(float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		// Forward the damage to the hurtbox component which
		// does the actual damage calculations
		HurtBoxComponent?.TakeDamage(amount, attackDirection, attacker, attackerFaction);
	}
}