using Godot;

/// <summary>
/// Represents an enemy character in the game.
/// </summary>
public partial class EnemyBase : CharacterBody3D, IDamageable, ICombatDamageable, IStaggerable, IStatusActor, IShieldOwner, ISlowActor, IBurningActor, IBuffTarget
{
	HealthComponent IBuffTarget.Health => HealthComponent;
	BuffController IBuffTarget.Buffs => BuffController;
	IStatModifierTarget IBuffTarget.Modifiers => Stats;
	HurtBoxComponent IShieldOwner.ShieldHurtBox => HurtBoxComponent;
	public Node3D Pivot;
	/// <summary>The body's collision shape (a capsule, cylinder or sphere): its radius is the monster's size for navigation.</summary>
	[Export] public CollisionShape3D BodyShape { get; set; }

	/// <summary>Navigation size of an actor whose body shape gives none.</summary>
	public const float DefaultBodyRadius = .5f;

	/// <summary>Horizontal radius of <see cref="BodyShape"/>, scale included; <see cref="DefaultBodyRadius"/> without one.</summary>
	public float BodyRadius => HorizontalRadius(BodyShape) is > 0 and var radius ? radius : DefaultBodyRadius;

	/// <summary>The horizontal radius of an upright capsule, cylinder or sphere body, its node's scale included; 0 for any other shape.</summary>
	public static float HorizontalRadius(CollisionShape3D body)
	{
		float radius = body?.Shape switch
		{
			CapsuleShape3D capsule => capsule.Radius,
			CylinderShape3D cylinder => cylinder.Radius,
			SphereShape3D sphere => sphere.Radius,
			_ => 0,
		};
		if (radius <= 0) return 0;
		var scale = body.Transform.Basis.Scale;
		return radius * Mathf.Max(scale.X, scale.Z);
	}
	[Export] public WorldEffectHost WorldEffects { get; set; }
	[Export] public InvisibilityComponent Invisibility { get; set; }
	[Export] public BuffController BuffController { get; set; }
	[Export] public FrozenStatusComponent FrozenStatus { get; set; }
	[Export] public SlowStatusComponent SlowStatus { get; set; }
	[Export] public BurningStatusComponent BurningStatus { get; set; }
	[Export] public StatusAppearanceComponent IronSkinStatus { get; set; }
	[Export] public StatusAppearanceComponent StoneSkinStatus { get; set; }
	/// <summary>Additional persistent initial conditions, authored as external Resources in the Inspector.</summary>
	/// <summary>Initial persistent conditions; each requires a wired actor component to produce behavior or visuals.</summary>
	[Export] public Godot.Collections.Array<StatusCondition> InitialConditions { get; set; } = [];
	private bool _initialInvisible, _initialFrozen, _initialIronSkin, _initialStoneSkin, _initialSlowed, _initialBurning;
	/// <summary>Inspector-authored initial state; at runtime the value reports all active sources.</summary>
	[Export] public bool Invisible { get => IsNodeReady() ? Invisibility?.IsInvisible == true : _initialInvisible; set { _initialInvisible = value; if (IsNodeReady()) Invisibility?.SetInvisible(value); } }
	[Export] public bool Frozen { get => IsNodeReady() ? FrozenStatus?.IsFrozen == true : _initialFrozen; set { _initialFrozen = value; if (IsNodeReady()) FrozenStatus?.SetFrozen(value); } }
	[Export] public bool Slowed { get => IsNodeReady() ? SlowStatus?.IsSlowed == true : _initialSlowed; set { _initialSlowed = value; if (IsNodeReady()) SlowStatus?.SetSlowed(value); } }
	[Export] public bool Burning { get => IsNodeReady() ? BurningStatus?.IsBurning == true : _initialBurning; set { _initialBurning = value; if (IsNodeReady()) BurningStatus?.SetBurning(value); } }
	[Export] public bool IronSkin { get => IsNodeReady() ? IronSkinStatus?.IsActive == true : _initialIronSkin; set { _initialIronSkin = value; if (IsNodeReady()) IronSkinStatus?.SetActive(value); } }
	[Export] public bool StoneSkin { get => IsNodeReady() ? StoneSkinStatus?.IsActive == true : _initialStoneSkin; set { _initialStoneSkin = value; if (IsNodeReady()) StoneSkinStatus?.SetActive(value); } }

	[Export] public EnemyBehaviorComponent EnemyBehaviorComponent;
	[Export] public VisibleOnScreenNotifier3D EncounterNotifier { get; set; }
	[Export] public MovementComponent MovementComponent;
	[Export] public HealthComponent HealthComponent;
	[Export] public HurtBoxComponent HurtBoxComponent;
	[Export] public DeathComponent DeathComponent;
	[Export] public LootTableComponent LootTableComponent;
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public GuardComponent GuardComponent { get; set; }
	[Export] public DodgeComponent DodgeComponent { get; set; }
	[Export] public EnemyDefenseController DefenseController { get; set; }
	[Export] public AttackController AttackController { get; set; }
	[Export] public SpellCaster SpellCaster { get; set; }
	[Export] public ActorStatsComponent Stats { get; set; }
	[Export] public ManaComponent ManaComponent { get; set; }
	/// <summary>Optional: committed displacement for dash actions (rushes, hop-backs).</summary>
	[Export] public DashComponent DashComponent { get; set; }
	/// <summary>Optional: health phases that actions can be gated and weighted by.</summary>
	[Export] public HealthPhaseComponent HealthPhases { get; set; }

	/// <summary>Optional: shows <see cref="MonsterDefinition.DisplayName"/> above the monster while the player is near.</summary>
	[Export] public Nameplate Nameplate { get; set; }
	[Export] public MonsterDefinition Definition { get; set; }
	public bool DefinitionValid { get; private set; } = true;
	private Level _level;
	// Scenes (with their shared definition) that already validated cleanly, so repeated spawns skip the
	// full check. Code that rewires an instance before it enters the tree must validate it itself.
	private static readonly System.Collections.Generic.HashSet<(string Scene, ulong Definition)> ValidatedScenes = [];

	/// <summary>Initialize authored defaults before child readiness; never mutate the definition.</summary>
	public override void _EnterTree()
	{
		if (Engine.IsEditorHint()) return;
		_level = this.GetAncestorOrNull<Level>();
		var key = (SceneFilePath, Definition?.GetInstanceId() ?? 0);
		var errors = !string.IsNullOrEmpty(SceneFilePath) && ValidatedScenes.Contains(key) ? [] : MonsterSceneValidator.Validate(this);
		DefinitionValid = errors.Length == 0;
		if (DefinitionValid && !string.IsNullOrEmpty(SceneFilePath)) ValidatedScenes.Add(key);
		if (!DefinitionValid)
		{
			foreach (var error in errors) GD.PushError($"{Name}: {error}");
			ProcessMode = ProcessModeEnum.Disabled;
			return;
		}
		Stats.Initialize(Definition);
		ManaComponent.SetMana(Definition.MaxMana, Definition.MaxMana);
		ManaComponent.RegenPerSecond = Definition.ManaRegenPerSecond;
		DeathComponent.Xp = Definition.XpReward;
		DeathComponent.DeathCue = Definition.DeathCue;
		LootTableComponent.Profile = Definition.Loot;
		EnemyBehaviorComponent.Profile = Definition.Behavior;
		if (Nameplate != null) Nameplate.NameLabel.Text = Definition.DisplayName;
	}

	public bool IsDead => EnemyBehaviorComponent?.IsDead ?? HealthComponent?.IsDead ?? false;

	public bool TryStagger(float duration)
	{
		return ActionGate?.TryStagger(duration) ?? false;
	}

	public override void _Ready()
	{
		if (Engine.IsEditorHint() || !DefinitionValid) return;
		BuffController?.ApplyInitialConditions(InitialConditions, this);
		if (_initialInvisible) Invisibility?.SetInvisible(true, true);
		if (_initialFrozen) FrozenStatus?.SetFrozen(true);
		if (_initialSlowed) SlowStatus?.SetSlowed(true);
		if (_initialBurning) BurningStatus?.SetBurning(true);
		if (_initialIronSkin) IronSkinStatus?.SetActive(true);
		if (_initialStoneSkin) StoneSkinStatus?.SetActive(true);
		Pivot = GetNode<Node3D>("Pivot");

		HealthComponent.Died += () => { ActionGate.Disable(); SpellCaster?.CancelInnate(); };

		// An enemy outside a level (previews, harnesses) plays the default run; each still gets its own combat streams.
		ConfigureCombatRandom(_level?.Seed ?? RunState.DefaultSeed, GetMeta("persistent_id", GetPath().ToString()).AsString());

		// Hide the mesh until the animations are fully initialized to
		// prevent any flickering
		Visible = false;

		// Configure Enemy HurtBox to only take damage from Player, Boss, or environmental hazards (traps)
		HurtBoxComponent?.RegisterDefenseEvaluator(DodgeComponent);
		HurtBoxComponent?.RegisterDefenseEvaluator(GuardComponent);
		if (HurtBoxComponent != null) HurtBoxComponent.Awareness = EnemyBehaviorComponent;

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
		SpellCaster?.ConfigureRandom(GenerationRandom.DeriveSeed(levelSeed, "combat.spell", sourceId));
		DefenseController?.ConfigureRandom(GenerationRandom.DeriveSeed(levelSeed, "combat.defense", sourceId));
		EnemyBehaviorComponent?.ConfigureActionRandom(GenerationRandom.DeriveSeed(levelSeed, "combat.action", sourceId));
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