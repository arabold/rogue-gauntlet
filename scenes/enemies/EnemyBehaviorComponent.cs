using Godot;

/// <summary>
/// High-level behavior states available to enemy AI controllers.
/// </summary>
public enum EnemyBehaviorState
{
	Sleeping,
	Idle,
	Guarding,
	Patrolling,
	Searching,
	Chasing,
	Fleeing,
	Dead
}

/// <summary>
/// Timed enemy actions that temporarily interrupt normal behavior decisions.
/// </summary>
public enum EnemyAction
{
	None,
	Spawning,
	StandingUp,
	Hit,
	MeeleAttack,
	RangedAttack,
	Dying,
}

/// <summary>
/// Thin host that wires up an enemy's AI: it owns the perception and navigation components, the
/// behavior <see cref="EnemyStateMachine"/>, and the timed-action layer (spawn, hit, attack, death)
/// that gates the machine. Per-state behavior lives in the <see cref="IEnemyState"/> classes and
/// shared data in <see cref="EnemyContext"/>; this component only ties them together, surfaces the
/// animation <c>Is*</c> flags, and runs the action gate in <see cref="_PhysicsProcess"/>.
/// </summary>
/// <remarks>
/// Monster-specific tuning lives in <see cref="EnemyBehaviorProfile"/> resources so new enemy
/// scenes can vary behavior without duplicating this controller.
/// </remarks>
public partial class EnemyBehaviorComponent : Node
{
	/// <summary>
	/// Character body moved and rotated by this behavior controller.
	/// </summary>
	[Export] public CharacterBody3D Actor { get; set; }

	/// <summary>
	/// Movement component used to apply navigation directions.
	/// </summary>
	[Export] public MovementComponent MovementComponent { get; set; }

	/// <summary>
	/// Health component observed for death transitions.
	/// </summary>
	[Export] public HealthComponent HealthComponent { get; set; }

	/// <summary>
	/// Authored behavior tuning for this enemy type.
	/// </summary>
	[Export] public EnemyBehaviorProfile Profile { get; set; }

	public EnemyBehaviorState CurrentBehavior => _machine?.CurrentId ?? EnemyBehaviorState.Idle;
	public EnemyAction CurrentAction { get; private set; } = EnemyAction.Spawning;

	// We can use these properties to automatically transition between animation states
	public bool IsSleeping => CurrentBehavior == EnemyBehaviorState.Sleeping;
	public bool IsMoving => CurrentAction == EnemyAction.None && MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsDead => CurrentAction == EnemyAction.Dying || CurrentBehavior == EnemyBehaviorState.Dead;
	public bool IsAttacking => CurrentAction == EnemyAction.MeeleAttack || CurrentAction == EnemyAction.RangedAttack;
	public bool IsMeleeAttack => CurrentAction == EnemyAction.MeeleAttack;
	public bool IsRangedAttack => CurrentAction == EnemyAction.RangedAttack;
	public bool IsHit => MovementComponent.IsPushed;
	public bool IsSpawning => CurrentAction == EnemyAction.Spawning;

	/// <summary>
	/// The target node that the enemy is chasing
	/// </summary>
	public Node3D Target => _context?.Target;

	private PerceptionComponent _perception;
	private NavigationComponent _navigation;
	private EnemyBehaviorProfile _profile;
	private AttackController _attackController;
	private EnemyContext _context;
	private EnemyStateMachine _machine;
	private Cooldown _actionCooldown;

	public override void _Ready()
	{
		base._Ready();

		_profile = Profile ?? new EnemyBehaviorProfile();
		CurrentAction = _profile.InitialAction;

		// SightRay and NavigationAgent3D stay authored as direct children of this component so
		// inherited enemy scenes are not disturbed; the perception and navigation components drive
		// them by reference.
		_perception = GetNode<PerceptionComponent>("Perception");
		_perception.Initialize(Actor, _profile, GetNode<RayCast3D>("SightRay"));
		_navigation = GetNode<NavigationComponent>("Navigation");
		_navigation.Initialize(Actor, MovementComponent, GetNode<NavigationAgent3D>("NavigationAgent3D"));

		_attackController = Actor.GetNodeOrNull<AttackController>("AttackController");
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} has no AttackController child; melee attacks will not deal damage.");
		}

		// The context is the shared blackboard the behavior states read and mutate; the state machine
		// owns the behavior layer. The timed-action layer (spawn, hit, attack, death) stays on this
		// host as a gate above the machine - see _PhysicsProcess.
		_context = new EnemyContext(Actor, MovementComponent, _perception, _navigation, _profile, RequestMeleeAttack);
		_machine = new EnemyStateMachine(_context, new IEnemyState[]
		{
			new IdleState(),
			new PatrollingState(_profile),
			new SearchingState(),
			new ChasingState(),
			new FleeingState(),
			new PassiveState(EnemyBehaviorState.Sleeping),
			new PassiveState(EnemyBehaviorState.Guarding),
			new PassiveState(EnemyBehaviorState.Dead),
		}, _profile.InitialBehavior);

		// Ensure to properly initialize the enemy's state with the current selection
		GameDebug.Ai($"{GetParent().Name} is initialized with {CurrentBehavior} and {CurrentAction}");
		_actionCooldown.Start(_profile.GetActionDuration(CurrentAction));

		if (HealthComponent != null)
		{
			this.SubscribeUntilExit(
				HealthComponent,
				healthComponent => healthComponent.Died += OnDie,
				healthComponent => healthComponent.Died -= OnDie);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		_context.TickThrottles(delta);

		// Action layer: timed actions (spawn, stand up, hit, attack, death) interrupt the behavior
		// layer. While one is active the body holds still and the state machine does not tick; when it
		// expires control returns to the machine.
		if (CurrentAction != EnemyAction.None)
		{
			MovementComponent.Stop();
			_navigation.ResetStuckTracking();

			if (_actionCooldown.Tick(delta))
			{
				SetAction(EnemyAction.None);
			}
		}
		else
		{
			_machine.Tick(delta);
		}
	}

	/// <summary>
	/// Starts a melee attack through the action layer. Handed to <see cref="EnemyContext"/> so the
	/// Chasing state can request an attack without touching <see cref="CurrentAction"/> directly. The
	/// action starts even when no AttackController is present (TriggerMeleeAttack logs the error),
	/// preserving the original ordering.
	/// </summary>
	private void RequestMeleeAttack()
	{
		SetAction(EnemyAction.MeeleAttack);
		TriggerMeleeAttack();
	}

	/// <summary>
	/// Forces a behavior transition through the state machine. Kept as the public entry point used
	/// by the host (e.g. <see cref="OnDie"/>); normal transitions happen inside the states.
	/// </summary>
	public void SetBehavior(EnemyBehaviorState newBehavior)
	{
		_machine?.ChangeState(newBehavior);
	}

	/// <summary>Sets the chase target (delegates to the shared context).</summary>
	public void SetTarget(Node3D target)
	{
		_context?.SetTarget(target);
	}

	public void SetAction(EnemyAction newAction)
	{
		if (CurrentAction != newAction)
		{
			GameDebug.Ai($"{Actor.Name} is performing {newAction}");
			CurrentAction = newAction;
			_actionCooldown.Start(_profile.GetActionDuration(newAction));
			if (newAction != EnemyAction.None)
			{
				MovementComponent.Stop();
			}

			// Cancel attack hitbox if transitioning out of attack, getting hit, or dying
			if (newAction == EnemyAction.None || newAction == EnemyAction.Hit || newAction == EnemyAction.Dying)
			{
				_attackController?.CancelAttack();
			}
		}
	}

	private void TriggerMeleeAttack()
	{
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} cannot start melee attack without AttackController.");
			return;
		}

		var def = _profile.MeleeAttackDefinition ?? CreateDefaultMeleeAttackDefinition();

		uint targetMask = 4; // Targets player (HurtBoxComponent is on Layer 3 / Mask 4)

		_attackController.StartAttack(
			def,
			_profile.MeleeAttackMinDamage,
			_profile.MeleeAttackMaxDamage,
			_profile.MeleeAttackAccuracy,
			_profile.MeleeAttackCritChance,
			targetMask
		);
	}

	private AttackDefinition CreateDefaultMeleeAttackDefinition()
	{
		var def = new AttackDefinition();
		def.AnimationId = "melee_attack";

		float duration = _profile.GetActionDuration(EnemyAction.MeeleAttack);
		def.HitWindowStart = 0.3f * duration;
		def.HitWindowEnd = 0.7f * duration;

		def.AttachHitBoxToWeapon = false;
		def.HitBoxSize = new Vector3(1.5f, 2.2f, 2.2f);
		def.HitBoxOffset = new Vector3(0.0f, 1.0f, -1.1f);
		return def;
	}

	public void OnDie()
	{
		SetAction(EnemyAction.Dying);
		SetBehavior(EnemyBehaviorState.Dead);
		MovementComponent.Stop();
	}
}
