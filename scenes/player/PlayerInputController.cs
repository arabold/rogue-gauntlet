using Godot;

/// <summary>
/// Coordinates player input with movement, action slots, dedicated weapon actions, and interactions.
/// </summary>
public partial class PlayerInputController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public InputComponent InputComponent { get; set; }
	[Export] public MovementComponent MovementComponent { get; set; }
	[Export] public ActionManager ActionManager { get; set; }
	[Export] public PlayerInteractionController InteractionController { get; set; }
	/// <summary>Seconds a fresh primary, secondary, dodge or guard press waits for an action that can accept it.</summary>
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float InputBufferWindow { get; set; } = 0.2f;
	private float _dodgeSteeringWindow = 0.04f;
	/// <summary>Seconds after an input-driven dodge starts that fresh steering can correct its travel direction.</summary>
	[Export(PropertyHint.Range, "0,0.15,0.005,suffix:s")]
	public float DodgeSteeringWindow
	{
		get => _dodgeSteeringWindow;
		set => _dodgeSteeringWindow = float.IsFinite(value) ? Mathf.Clamp(value, 0f, 0.15f) : 0f;
	}
	private float _dodgeDirectionMemoryDuration = 0.08f;
	/// <summary>How long a released steering direction remains available to a new dodge.</summary>
	[Export(PropertyHint.Range, "0,0.2,0.005,suffix:s")]
	public float DodgeDirectionMemoryDuration
	{
		get => _dodgeDirectionMemoryDuration;
		set => _dodgeDirectionMemoryDuration = float.IsFinite(value) ? Mathf.Clamp(value, 0f, 0.2f) : 0f;
	}

	private InputIntentBuffer _intents;
	private readonly DodgeDirectionMemory _dodgeDirectionMemory = new();
	// Gameplay clock for the intent buffer; it stops while the tree is paused.
	private double _clock;
	private bool _requireDefenseRelease;
	private bool _guardIntent;
	private bool _isFocused = true;
	public void SetGuardIntent(bool held) => _guardIntent = held;
	private bool IsGuardHeld => _guardIntent || InputComponent.IsOffhandPressed();
	public void RequireFreshDefenseInput()
	{
		_requireDefenseRelease = true;
		_guardIntent = false;
		_intents?.Clear();
		ClearDodgeDirectionState();
		Player?.GuardComponent?.EndGuard();
	}

	/// <summary>Buffer a fresh press as if it came from the input device; used by the combat harness and tests.</summary>
	public void PushIntent(PlayerIntent intent) => _intents.Push(intent, _clock);

	public override void _Notification(int what)
	{
		if (what == NotificationPaused) RequireFreshDefenseInput();
		else if (what == NotificationWMWindowFocusOut)
		{
			_isFocused = false;
			RequireFreshDefenseInput();
		}
		else if (what == NotificationWMWindowFocusIn)
		{
			_isFocused = true;
			RequireFreshDefenseInput();
		}
	}

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Intent;
		Player ??= GetOwner<Player>();
		InputComponent ??= Player.InputComponent;
		MovementComponent ??= Player.MovementComponent;
		ActionManager ??= Player.ActionManager;
		InteractionController ??= Player.InteractionController;
		_intents = new InputIntentBuffer(InputBufferWindow);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Player.IsDead) { MovementComponent.Stop(); _intents.Clear(); ClearDodgeDirectionState(); return; }
		if (!_isFocused) return;
		_clock += delta;
		_dodgeDirectionMemory.Observe(InputComponent.InputDirection, _clock);
		BufferFreshPresses();
		if (_requireDefenseRelease && !IsGuardHeld) _requireDefenseRelease = false;
		// Active world interaction channel: check for input cancellation
		if (Player.InteractionComponent?.IsActive == true)
		{
			MovementComponent.SetInputDirection(Vector3.Zero);

			if (TryBufferedDodge()) return;

			if (!InputComponent.IsInteractPressed())
			{
				Player.InteractionComponent.Cancel(InteractionCancelReason.InputReleased);
				return;
			}

			if (InputComponent.IsMoving())
			{
				Player.InteractionComponent.Cancel(InteractionCancelReason.Movement);
				return;
			}

			if (InputComponent.IsPrimaryActionPressed() || InputComponent.IsSecondaryActionJustPressed())
			{
				Player.InteractionComponent.Cancel(InteractionCancelReason.CombatIntent);
				return;
			}

			return;
		}

		// 1. Dodge: evaluated before other voluntary actions; can cancel attack startup, recovery or held guard
		if (TryBufferedDodge()) return;

		// 2. Guard: raise while the press is fresh and still held (it may cancel attack recovery), hold, release
		if (!_requireDefenseRelease && IsGuardHeld && Player.GuardComponent?.HasGuard == true && Player.GuardComponent?.IsGuarding == false
			&& _intents.IsPending(PlayerIntent.Offhand, _clock)
			&& Player.GuardComponent.TryBeginGuard(MovementComponent.LookAtDirection) == DefenseRequestResult.Success)
		{
			_intents.Clear();
		}

		if (Player.GuardComponent?.IsGuarding == true && !IsGuardHeld)
		{
			Player.GuardComponent.EndGuard();
		}

		if (Player.GuardComponent?.IsGuarding == true || Player.GuardComponent?.IsGuardRecovering == true)
		{
			MovementComponent.SetInputDirection(Player.GuardComponent.IsGuarding ? InputComponent.InputDirection : Vector3.Zero);
			return;
		}

		if (Player.DodgeComponent?.IsDodging == true)
		{
			Player.DodgeComponent.TryRedirect(InputComponent.InputDirection);
			// The dodge override owns travel; keep fresh intent ready for the moment it releases.
			MovementComponent.SetInputDirection(InputComponent.InputDirection);
			return;
		}

		if (Player.IsPerformingAction)
		{
			MovementComponent.SetInputDirection(Vector3.Zero);
			// A fresh primary press cancels a combo step's recovery into the next step.
			if (_intents.IsPending(PlayerIntent.Primary, _clock) && ActionManager.TryChainPrimary())
				_intents.TryConsume(PlayerIntent.Primary, _clock);

			// Opposite wand handoff during recovery:
			if (Player.AttackController?.Phase == ActorActionPhase.Recovery)
			{
				if (_intents.IsPending(PlayerIntent.Secondary, _clock) && ActionManager.TryPerformSecondary())
					_intents.TryConsume(PlayerIntent.Secondary, _clock);
				else if (_intents.IsPending(PlayerIntent.Offhand, _clock) && ActionManager.TryPerformOffhand())
					_intents.TryConsume(PlayerIntent.Offhand, _clock);
			}

			// Aim-assist: while attacking, turn toward the locked target so the player does not have to
			// face the enemy exactly. With no target, keep facing the movement/aim input as before.
			Vector3 assistFacing = Player.AttackController?.GetAimAssistFacing() ?? Vector3.Zero;
			if (assistFacing != Vector3.Zero)
			{
				MovementComponent.FaceDirection(assistFacing);
			}
			else if (InputComponent.IsMoving())
			{
				// No aim-assist target: face where the player is aiming. Leave the current facing
				// untouched when there is no input so we never zero out the stored look direction.
				MovementComponent.SetLookAtDirection(InputComponent.InputDirection);
			}
			return;
		}

		// Offhand action (wand cast): one request per buffered press
		bool started = false;
		if (_intents.IsPending(PlayerIntent.Offhand, _clock))
		{
			started = ActionManager.TryPerformOffhand();
			if (started) _intents.TryConsume(PlayerIntent.Offhand, _clock);
		}

		// Secondary technique: one request per buffered press
		if (!started && _intents.IsPending(PlayerIntent.Secondary, _clock))
		{
			started = ActionManager.TryPerformSecondary();
			if (started) _intents.TryConsume(PlayerIntent.Secondary, _clock);
		}
		// Primary action: press, or hold to repeat complete attacks (and combo steps) at normal cadence
		if (!started && (_intents.IsPending(PlayerIntent.Primary, _clock) || InputComponent.IsPrimaryActionPressed()))
		{
			started = ActionManager.TryPerformPrimary();
			if (started) _intents.TryConsume(PlayerIntent.Primary, _clock);
		}
		if (!started)
		{
			// Quick items 1..4: one request per new press; never repeat while held
			for (int i = 0; i < ActionManager.ActionSlotCount; i++)
			{
				if (InputComponent.IsQuickItemJustPressed(i))
				{
					if (Player.QuickItemController != null)
					{
						Player.QuickItemController.TryUseSlot(i);
					}
					else
					{
						ActionManager.TryPerformAction(i);
					}
					break;
				}
			}
		}

		InteractionController.HandleInteractInput(InputComponent.IsInteractPressed());

		MovementComponent.SetInputDirection(InputComponent.InputDirection);
	}

	private void BufferFreshPresses()
	{
		if (InputComponent.IsDodgeJustPressed()) _intents.Push(PlayerIntent.Dodge, _clock);
		if (InputComponent.IsOffhandJustPressed()) _intents.Push(PlayerIntent.Offhand, _clock);
		if (InputComponent.IsPrimaryActionJustPressed()) _intents.Push(PlayerIntent.Primary, _clock);
		if (InputComponent.IsSecondaryActionJustPressed()) _intents.Push(PlayerIntent.Secondary, _clock);
	}

	/// <summary>Dodge while a press is buffered; an accepted dodge drops every older press.</summary>
	private bool TryBufferedDodge()
	{
		if (!_intents.IsPending(PlayerIntent.Dodge, _clock)
			|| Player.DodgeComponent?.TryDodge(GetDodgeDirection(), DodgeSteeringWindow) != DefenseRequestResult.Success) return false;
		_intents.Clear();
		return true;
	}

	/// <summary>Use current steering, recent steering, or facing for a neutral dash; never derive direction from velocity.</summary>
	public Vector3 GetDodgeDirection()
	{
		Vector3 facing = MovementComponent != null && MovementComponent.LookAtDirection != Vector3.Zero
			? MovementComponent.LookAtDirection
			: (Player != null ? -Player.GlobalTransform.Basis.Z : Vector3.Forward);
		Vector3 input = _isFocused && InputComponent != null ? InputComponent.InputDirection : Vector3.Zero;
		return _dodgeDirectionMemory.Resolve(input, facing, _clock, DodgeDirectionMemoryDuration);
	}

	private void ClearDodgeDirectionState()
	{
		_dodgeDirectionMemory.Clear();
		Player?.DodgeComponent?.LockDirection();
	}

	public override void _ExitTree() => ClearDodgeDirectionState();
}