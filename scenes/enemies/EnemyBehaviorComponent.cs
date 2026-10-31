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
public partial class EnemyBehaviorComponent : Node, ICombatAwareness
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
	[Export] public bool AIEnabled { get; set; } = true;

	public EnemyBehaviorState CurrentBehavior => _machine?.CurrentId ?? EnemyBehaviorState.Idle;
	public EnemyAction CurrentAction { get; private set; } = EnemyAction.Spawning;

	// The actor's AnimationTree advances on these; attacks, defenses and staggers pose through CombatAnimationComponent.
	public bool IsSleeping => CurrentBehavior == EnemyBehaviorState.Sleeping;
	public bool IsMoving => CurrentAction == EnemyAction.None && MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsDead => CurrentAction == EnemyAction.Dying || CurrentBehavior == EnemyBehaviorState.Dead;
	public bool IsAttacking => CurrentAction == EnemyAction.MeeleAttack || CurrentAction == EnemyAction.RangedAttack;
	/// <summary>Drives the tree's Hit state: a knockback outside a stagger, since a stagger poses its own reaction.</summary>
	public bool IsKnockedBack => MovementComponent.IsKnockedBack;

	/// <summary>
	/// The target node that the enemy is chasing
	/// </summary>
	public Node3D Target => _context?.Target;
	/// <summary>Latest engagement choice for debug tools; changes are also emitted to the combat trace.</summary>
	public string TacticalDecisionReason => _context?.TacticalDecisionReason ?? "inactive";

	private PerceptionComponent _perception;
	private NavigationComponent _navigation;
	private EnemyBehaviorProfile _profile;
	private AttackController _attackController;
	private EnemyContext _context;
	private EnemyStateMachine _machine;
	private Cooldown _actionCooldown;
	// Slots of the target currently engaged, and of the target whose slot the running attack holds.
	private AttackSlotsComponent _engagedSlots;
	private AttackSlotsComponent _heldSlots;

	public override void _Ready()
	{
		if (Actor is EnemyBase { DefinitionValid: false }) return;
		ProcessPhysicsPriority = CombatPhysics.Steering;
		base._Ready();

		_profile = Profile ?? new EnemyBehaviorProfile();
		CurrentAction = _profile.InitialAction;

		// SightRay and NavigationAgent3D stay authored as direct children of this component so
		// inherited enemy scenes are not disturbed; the perception and navigation components drive
		// them by reference.
		_perception = GetNode<PerceptionComponent>("Perception");
		_perception.Initialize(Actor, _profile, GetNode<RayCast3D>("SightRay"));
		_navigation = GetNode<NavigationComponent>("Navigation");
		_navigation.Initialize(Actor, MovementComponent, GetNode<NavigationAgent3D>("NavigationAgent3D"), (Actor as EnemyBase)?.LargeActorFit);

		_attackController = Actor is EnemyBase enemy ? enemy.AttackController : Actor.GetNodeOrNull<AttackController>("AttackController");
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} has no AttackController child; melee attacks will not deal damage.");
		}

		// The context is the shared blackboard the behavior states read and mutate; the state machine
		// owns the behavior layer. The timed-action layer (spawn, hit, attack, death) stays on this
		// host as a gate above the machine - see _PhysicsProcess.
		_context = new EnemyContext(Actor, MovementComponent, _perception, _navigation, _profile, RequestAttack);
		if (_attackController != null) _attackController.AttackFinished += OnAttackFinished;
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
		if (_context == null) return;
		_context.TickThrottles(delta);
		SyncEngagedSlots();
		var gate = (Actor as EnemyBase)?.ActionGate;
		if (gate?.Disabled == true || (gate?.IsBusy == true && !IsAttacking))
		{
			MovementComponent.Stop();
			_navigation.ResetStuckTracking();
			return;
		}

		// Action layer: timed actions (spawn, stand up, hit, attack, death) interrupt the behavior
		// layer. While one is active the body holds still and the state machine does not tick; when it
		// expires control returns to the machine.
		if (CurrentAction != EnemyAction.None)
		{
			MovementComponent.Stop();
			_navigation.ResetStuckTracking();

			// Track the target through the wind-up (movement rotation is off while stopped), no faster than
			// the attack's authored turn rate, then hold the committed facing. An early facing lock plus a
			// capped turn lets a target sidestep a committed swing.
			if (IsAttacking && Target != null && GodotObject.IsInstanceValid(Target))
			{
				if (_attackController.HasCommittedFacing) MovementComponent.FaceDirection(_attackController.CommittedFacing);
				else MovementComponent.FaceDirection(Target.GlobalPosition - Actor.GlobalPosition,
					_attackController.CurrentAttack?.WindUpTurnRate ?? 0);
			}

			if (IsAttacking ? !_attackController.IsAttacking : _actionCooldown.Tick(delta))
			{
				SetAction(EnemyAction.None);
			}
		}
		else if (AIEnabled)
		{
			_machine.Tick(delta);
		}
	}

	private void RequestAttack() => TryAttack();

	/// <summary>Sneak-attack window: no target yet, and not searching, chasing or fleeing.</summary>
	public bool IsUnaware => _context != null && !IsDead && Target == null
		&& CurrentBehavior is EnemyBehaviorState.Sleeping or EnemyBehaviorState.Idle or EnemyBehaviorState.Guarding or EnemyBehaviorState.Patrolling;

	/// <summary>A sneak attack wakes the monster and turns it on the player who struck it.</summary>
	public void NoticeAttacker(Node attacker)
	{
		if (_context == null || IsDead || Target != null || attacker is not Player player) return;
		SetTarget(player);
		SetBehavior(EnemyBehaviorState.Chasing);
	}

	private void OnAttackFinished(AttackIdentity identity, bool interrupted)
	{
		if (GodotObject.IsInstanceValid(_heldSlots)) _heldSlots.Release(Actor);
		_heldSlots = null;
		_context?.Tactics.AttackFinished(identity, interrupted);
		_context?.TacticalSelector?.AttackFinished(identity);
	}

	public override void _ExitTree()
	{
		if (_attackController != null) _attackController.AttackFinished -= OnAttackFinished;
		ForgetSlots();
	}

	/// <summary>Leave the previous target's ring and slots when the target changes or is lost.</summary>
	private void SyncEngagedSlots()
	{
		var slots = _context.TargetSlots;
		if (slots == _engagedSlots) return;
		if (GodotObject.IsInstanceValid(_engagedSlots)) _engagedSlots.Forget(Actor);
		_engagedSlots = slots;
	}

	private void ForgetSlots()
	{
		if (GodotObject.IsInstanceValid(_engagedSlots)) _engagedSlots.Forget(Actor);
		if (GodotObject.IsInstanceValid(_heldSlots)) _heldSlots.Forget(Actor);
		_engagedSlots = _heldSlots = null;
	}

	/// <summary>Execute the one authored monster attack after range, sight and action validation.</summary>
	public bool TryAttack()
	{
		var policy = Profile?.AttackStrategy;
		if (_context == null || policy == null || CurrentAction != EnemyAction.None || IsDead
			|| MovementComponent.IsPushed || !policy.CanAttack(_context)) return false;
		// Take a turn through the target's attack slots; a start that fails afterwards hands the slot straight back.
		var slots = _context.PolicySlots;
		if (slots != null && !slots.TryReserve(Actor, policy.SlotKind)) return false;
		if (!_attackController.TryPrepareAttack(policy.Attack, policy.Style, policy.Duration, out var attack)
			|| !_attackController.StartAttack(attack, new AttackStats(policy.MinDamage, policy.MaxDamage, policy.CritChance), 4))
		{
			slots?.Release(Actor, yield: false);
			return false;
		}
		_heldSlots = slots;
		_context.TacticalSelector?.AttackAccepted(_attackController.CurrentExecutionId);
		if (Profile.MovementStrategy is AttackThenRetreatStrategy) _context.Tactics.TrackAttack(_attackController.CurrentExecutionId);
		SetAction(policy.Attack.IsRanged ? EnemyAction.RangedAttack : EnemyAction.MeeleAttack);
		return true;
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

	public void OnDie()
	{
		ForgetSlots();
		_context?.Tactics.ClearTarget();
		_context?.TacticalSelector?.ClearTarget();
		SetAction(EnemyAction.Dying);
		SetBehavior(EnemyBehaviorState.Dead);
		MovementComponent.Stop();
	}
}