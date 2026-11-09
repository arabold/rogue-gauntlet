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
	SpellCast,
	Rushing,
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
	[Signal] public delegate void PhaseChangedEventHandler(int previous, int current);
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
	private bool _aiEnabled = true;
	[Export]
	public bool AIEnabled
	{
		get => _aiEnabled;
		set
		{
			if (_aiEnabled == value) return;
			_aiEnabled = value;
			if (!IsNodeReady() || _context == null) return;
			if (!value) MovementComponent?.Stop();
			else SetBehavior(Target != null && GodotObject.IsInstanceValid(Target)
				? EnemyBehaviorState.Chasing : EnemyBehaviorState.Idle);
		}
	}

	public EnemyBehaviorState CurrentBehavior => _machine?.CurrentId ?? EnemyBehaviorState.Idle;
	public EnemyAction CurrentAction { get; private set; } = EnemyAction.Spawning;

	// The actor's AnimationTree advances on these; attacks, defenses and staggers pose through CombatAnimationComponent.
	public bool IsSleeping => CurrentBehavior == EnemyBehaviorState.Sleeping;
	public bool IsMoving => CurrentAction == EnemyAction.None && MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsDead => CurrentAction == EnemyAction.Dying || CurrentBehavior == EnemyBehaviorState.Dead;
	public bool IsAttacking => CurrentAction is EnemyAction.MeeleAttack or EnemyAction.RangedAttack or EnemyAction.SpellCast;
	/// <summary>Drives the tree's Hit state: a knockback outside a stagger, since a stagger poses its own reaction.</summary>
	public bool IsKnockedBack => MovementComponent.IsKnockedBack;

	/// <summary>
	/// The target node that the enemy is chasing
	/// </summary>
	public Node3D Target => _context?.Target;
	/// <summary>Moves the actor over the floor's navigation; null until ready.</summary>
	public NavigationComponent Navigation => _navigation;
	/// <summary>Latest engagement choice for debug tools; changes are also emitted to the combat trace.</summary>
	public string TacticalDecisionReason => _context?.TacticalDecisionReason ?? "inactive";

	private PerceptionComponent _perception;
	private NavigationComponent _navigation;
	private EnemyBehaviorProfile _profile;
	private AttackController _attackController;
	private EnemyContext _context;
	private EnemyStateMachine _machine;
	private readonly EnemyActionSelector _actionSelector = new();
	private AttackIdentity _rushExecution;
	private Cooldown _actionCooldown;
	// Slots of the target currently engaged, and of the target whose slot the running attack holds.
	private AttackSlotsComponent _engagedSlots;
	private AttackSlotsComponent _heldSlots;
	private int _currentPhaseIndex;
	private int _pendingPhaseIndex = -1;
	public int CurrentPhaseIndex => _currentPhaseIndex;
	public EnemyCombatProfile CurrentCombatProfile => _context?.CombatProfile;

	/// <summary>Seed action choices independently from attack and defense execution.</summary>
	public void ConfigureActionRandom(ulong seed) => _actionSelector.ConfigureRandom(seed);

	public override void _Ready()
	{
		if (Actor is EnemyBase { DefinitionValid: false }) return;
		ProcessPhysicsPriority = CombatPhysics.Steering;
		base._Ready();

		_profile = Profile ?? new EnemyBehaviorProfile();
		string[] profileErrors = _profile.ValidateCombat();
		if (profileErrors.Length > 0)
		{
			foreach (string error in profileErrors) GD.PushError($"{Name}: invalid monster combat profile: {error}");
			return;
		}
		CurrentAction = _profile.InitialAction;

		// SightRay stays authored as a direct child of this component so inherited enemy scenes are not
		// disturbed; the perception component drives it by reference.
		_perception = GetNode<PerceptionComponent>("Perception");
		_perception.Initialize(Actor, _profile, GetNode<RayCast3D>("SightRay"));
		_navigation = GetNode<NavigationComponent>("Navigation");
		_navigation.Initialize(Actor, MovementComponent, (Actor as EnemyBase)?.BodyRadius ?? EnemyBase.DefaultBodyRadius, _profile.StayInHomeRoom);

		_attackController = Actor is EnemyBase enemy ? enemy.AttackController : Actor.GetNodeOrNull<AttackController>("AttackController");
		if (_attackController == null)
		{
			GD.PushError($"{Actor.Name} has no AttackController child; melee attacks will not deal damage.");
		}

		// The context is the shared blackboard the behavior states read and mutate; the state machine
		// owns the behavior layer. The timed-action layer (spawn, hit, attack, death) stays on this
		// host as a gate above the machine - see _PhysicsProcess.
		_context = new EnemyContext(Actor, MovementComponent, _perception, _navigation, _profile, _profile.Combat, RequestAction);
		if (_attackController != null) _attackController.AttackFinished += OnAttackFinished;
		MovementComponent.RushFinished += OnRushFinished;
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
			this.SubscribeUntilExit(
				HealthComponent,
				healthComponent => healthComponent.HealthChanged += OnHealthChanged,
				healthComponent => healthComponent.HealthChanged -= OnHealthChanged);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_context == null) return;
		_context.TickThrottles(delta);
		_actionSelector.Tick(delta, _context, _context.CombatProfile);
		PrepareDecision();
		SyncEngagedSlots();
		var gate = (Actor as EnemyBase)?.ActionGate;
		if (gate?.Disabled == true || (gate?.IsBusy == true && !IsAttacking && CurrentAction != EnemyAction.Rushing))
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
			if (CurrentAction == EnemyAction.Rushing && Target != null && GodotObject.IsInstanceValid(Target))
				MovementComponent.UpdateRushDirection(_rushExecution, Target.GlobalPosition - Actor.GlobalPosition);

			// Track the target through the wind-up (movement rotation is off while stopped), no faster than
			// the attack's authored turn rate, then hold the committed facing. An early facing lock plus a
			// capped turn lets a target sidestep a committed swing.
			if (IsAttacking && Target != null && GodotObject.IsInstanceValid(Target))
			{
				if (_attackController.HasCommittedFacing) MovementComponent.FaceDirection(_attackController.CommittedFacing);
				else MovementComponent.FaceDirection(Target.GlobalPosition - Actor.GlobalPosition,
					_attackController.CurrentAttack?.WindUpTurnRate ?? 0);
			}

			bool actionFinished = CurrentAction switch
			{
				EnemyAction.MeeleAttack or EnemyAction.RangedAttack or EnemyAction.SpellCast => !_attackController.IsAttacking,
				EnemyAction.Rushing => !MovementComponent.IsRushing,
				_ => _actionCooldown.Tick(delta),
			};
			if (actionFinished)
			{
				SetAction(EnemyAction.None);
			}
		}
		else if (AIEnabled)
		{
			// Self-support actions remain available without a hostile target or a chase state.
			if (Target == null && _actionSelector.DecisionReady && TryRequestAction() == ActorActionRequestResult.Success) return;
			_machine.Tick(delta);
		}
	}

	private void RequestAction() => TryRequestAction();

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

	private void OnRushFinished(ulong sourceInstanceId, ulong sequence, bool interrupted)
	{
		if (CurrentAction == EnemyAction.Rushing && _rushExecution == new AttackIdentity(sourceInstanceId, sequence))
		{
			_rushExecution = default;
			SetAction(EnemyAction.None);
		}
	}

	private void OnHealthChanged(float currentHealth, float maxHealth)
	{
		if (currentHealth <= 0 || maxHealth <= 0)
		{
			_pendingPhaseIndex = -1;
			return;
		}
		float fraction = currentHealth / maxHealth;
		int deepest = 0;
		for (int index = 0; index < (_profile.Phases?.Count ?? 0); index++)
		{
			if (fraction > _profile.Phases[index].EnterHealthFraction) break;
			deepest = index + 1;
		}
		if (deepest > _currentPhaseIndex) _pendingPhaseIndex = deepest;
	}

	/// <summary>Apply a latched health phase only at a safe idle decision boundary; walking and cooldowns survive.</summary>
	public void PrepareDecision()
	{
		if (_pendingPhaseIndex <= _currentPhaseIndex || IsDead || CurrentAction != EnemyAction.None
			|| IsAttacking || MovementComponent?.IsPushed == true) return;
		if ((Actor as EnemyBase)?.ActionGate is { } gate && (gate.Disabled || gate.IsBusy)) return;
		int previous = _currentPhaseIndex;
		_currentPhaseIndex = _pendingPhaseIndex;
		_pendingPhaseIndex = -1;
		var combatProfile = _currentPhaseIndex == 0 ? _profile.Combat : _profile.Phases[_currentPhaseIndex - 1].Combat;
		_context.SetCombatProfile(combatProfile);
		EmitSignalPhaseChanged(previous, _currentPhaseIndex);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "enemy.phase_changed", ("previous", previous), ("current", _currentPhaseIndex));
	}

	public override void _ExitTree()
	{
		if (_attackController != null) _attackController.AttackFinished -= OnAttackFinished;
		if (GodotObject.IsInstanceValid(MovementComponent)) MovementComponent.RushFinished -= OnRushFinished;
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

	/// <summary>Select and start one eligible action without changing cooldown or history on a rejected request.</summary>
	public ActorActionRequestResult TryRequestAction(bool riposteOnly = false, bool attackOnly = false)
	{
		PrepareDecision();
		if (_context?.CombatProfile == null || CurrentAction != EnemyAction.None || IsDead || MovementComponent.IsPushed)
			return ActorActionRequestResult.Busy;
		var action = _actionSelector.Select(_context.CombatProfile, _context, riposteOnly, attackOnly);
		if (action == null) return ActorActionRequestResult.Unavailable;
		var result = TryStartEligibleAction(action);
		if (CombatTrace.Enabled)
			CombatTrace.Write(Actor, "ai.action_attempt", ("action", action.ResourcePath), ("type", action.GetType().Name), ("result", result.ToString()));
		if (result == ActorActionRequestResult.Success) _actionSelector.Accepted(action);
		else _actionSelector.Rejected();
		return result;
	}

	/// <summary>Request only an authored weapon-style attack; innate spells and movement actions remain available through TryRequestAction.</summary>
	public bool TryAttack() => TryRequestAction(attackOnly: true) == ActorActionRequestResult.Success;

	/// <summary>Start a selected profile through its existing gameplay executor.</summary>
	public ActorActionRequestResult TryAction(EnemyActionProfile action)
	{
		if (!CanStartAction(action) || !action.IsEligible(_context, _actionSelector.PressureDurationFor(action)))
			return ActorActionRequestResult.Unavailable;
		return TryStartEligibleAction(action);
	}

	/// <summary>Execute a profile after the selector has already approved its eligibility.</summary>
	private ActorActionRequestResult TryStartEligibleAction(EnemyActionProfile action)
	{
		if (!CanStartAction(action)) return ActorActionRequestResult.Unavailable;
		if (action is EnemyRushProfile rushProfile)
		{
			Vector3 direction = _context.Target.GlobalPosition - Actor.GlobalPosition;
			if (!_navigation.CanRush(direction, rushProfile.Rush.Distance)) return ActorActionRequestResult.Unavailable;
			var result = MovementComponent.TryRush(rushProfile.Rush, direction, out var execution);
			if (result != ActorActionRequestResult.Success) return result;
			_rushExecution = execution;
			SetAction(EnemyAction.Rushing);
			return ActorActionRequestResult.Success;
		}
		if (action is EnemySpellProfile spellProfile)
			return TryStartSpell(spellProfile);
		if (action is not EnemyAttackProfile attackProfile) return ActorActionRequestResult.Unavailable;
		var slots = action.SlotKind == EnemyActionSlotKind.None ? null : _context.TargetSlots;
		AttackSlotKind slotKind = action.SlotKind == EnemyActionSlotKind.Ranged ? AttackSlotKind.Ranged : AttackSlotKind.Melee;
		if (slots != null && !slots.TryReserve(Actor, slotKind)) return ActorActionRequestResult.Busy;
		if (_attackController == null || !_attackController.TryPrepareAttack(attackProfile.Attack, attackProfile.Style,
			attackProfile.Duration, out var prepared)
			|| !_attackController.StartAttack(prepared,
				(Actor as EnemyBase)?.Stats?.ResolveAttack(attackProfile.MinDamage, attackProfile.MaxDamage, attackProfile.CritChance)
					?? new AttackStats(attackProfile.MinDamage, attackProfile.MaxDamage, attackProfile.CritChance), 4))
		{
			slots?.Release(Actor, yield: false);
			return ActorActionRequestResult.InvalidDefinition;
		}
		_heldSlots = slots;
		NotifyAttackAccepted(_attackController.CurrentExecutionId);
		SetAction(attackProfile.Attack.IsRanged ? EnemyAction.RangedAttack : EnemyAction.MeeleAttack);
		return ActorActionRequestResult.Success;
	}

	private ActorActionRequestResult TryStartSpell(EnemySpellProfile profile)
	{
		if (_attackController == null || !profile.TryPrepare(_context, out var release, out var castContext))
			return ActorActionRequestResult.Unavailable;
		var enemy = Actor as EnemyBase;
		float manaCost = profile.PaysMana ? profile.Spell.ManaCost(profile.Level) : 0;
		if (profile.PaysMana && enemy?.ManaComponent?.CanSpend(manaCost) != true) return ActorActionRequestResult.Unavailable;
		var slots = profile.SlotKind == EnemyActionSlotKind.None ? null : _context.TargetSlots;
		AttackSlotKind slotKind = profile.SlotKind == EnemyActionSlotKind.Ranged ? AttackSlotKind.Ranged : AttackSlotKind.Melee;
		if (slots != null && !slots.TryReserve(Actor, slotKind)) return ActorActionRequestResult.Busy;
		if (!_attackController.TryPrepareAttack(profile.Spell.Cast, profile.Style, profile.Duration, out var prepared)
			|| !_attackController.StartAttack(prepared with { Spell = release }, castContext.Power.Hit, 4))
		{
			slots?.Release(Actor, yield: false);
			return ActorActionRequestResult.InvalidDefinition;
		}
		if (manaCost > 0 && !enemy.ManaComponent.TrySpend(manaCost))
		{
			_attackController.CancelAttack();
			slots?.Release(Actor, yield: false);
			return ActorActionRequestResult.Unavailable;
		}
		_heldSlots = slots;
		if (profile.IsOffensive)
			NotifyAttackAccepted(_attackController.CurrentExecutionId);
		SetAction(EnemyAction.SpellCast);
		return ActorActionRequestResult.Success;
	}

	private bool CanStartAction(EnemyActionProfile action) => _context != null && action != null
		&& CurrentAction == EnemyAction.None && !IsDead && !MovementComponent.IsPushed;

	private void NotifyAttackAccepted(AttackIdentity identity)
	{
		_context.TacticalSelector?.AttackAccepted(identity);
		_context.CombatProfile.MovementStrategy?.AttackAccepted(_context, identity);
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
		_pendingPhaseIndex = -1;
		ForgetSlots();
		_context?.Tactics.ClearTarget();
		_context?.TacticalSelector?.ClearTarget();
		SetAction(EnemyAction.Dying);
		SetBehavior(EnemyBehaviorState.Dead);
		MovementComponent.Stop();
	}
}
