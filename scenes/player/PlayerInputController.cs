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

	private InputIntentBuffer _intents;
	// Gameplay clock for the intent buffer; it stops while the tree is paused.
	private double _clock;
	private bool _requireDefenseRelease;
	private bool _guardIntent;
	public void SetGuardIntent(bool held) => _guardIntent = held;
	private bool IsGuardHeld => _guardIntent || InputComponent.IsGuardPressed();
	public void RequireFreshDefenseInput()
	{
		_requireDefenseRelease = true;
		_guardIntent = false;
		_intents?.Clear();
		Player?.GuardComponent?.EndGuard();
	}

	/// <summary>Buffer a fresh press as if it came from the input device; used by the combat harness and tests.</summary>
	public void PushIntent(PlayerIntent intent) => _intents.Push(intent, _clock);

	public override void _Notification(int what)
	{
		if (what == NotificationPaused || what == NotificationWMWindowFocusOut) RequireFreshDefenseInput();
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
		if (Player.IsDead) { MovementComponent.Stop(); _intents.Clear(); return; }
		_clock += delta;
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
		if (!_requireDefenseRelease && IsGuardHeld && Player.GuardComponent?.IsGuarding == false
			&& _intents.IsPending(PlayerIntent.GuardPress, _clock)
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

		// Secondary technique: one request per buffered press
		bool started = _intents.IsPending(PlayerIntent.Secondary, _clock) && ActionManager.TryPerformSecondary()
			&& _intents.TryConsume(PlayerIntent.Secondary, _clock);
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
		if (InputComponent.IsGuardJustPressed()) _intents.Push(PlayerIntent.GuardPress, _clock);
		if (InputComponent.IsPrimaryActionJustPressed()) _intents.Push(PlayerIntent.Primary, _clock);
		if (InputComponent.IsSecondaryActionJustPressed()) _intents.Push(PlayerIntent.Secondary, _clock);
	}

	/// <summary>Dodge while a press is buffered; an accepted dodge drops every older press.</summary>
	private bool TryBufferedDodge()
	{
		if (!_intents.IsPending(PlayerIntent.Dodge, _clock)
			|| Player.DodgeComponent?.TryDodge(GetDodgeDirection()) != DefenseRequestResult.Success) return false;
		_intents.Clear();
		return true;
	}

	/// <summary>Use steering intent, never physical velocity; idle input backsteps from facing.</summary>
	public Vector3 GetDodgeDirection()
	{
		Vector3 facing = MovementComponent != null && MovementComponent.LookAtDirection != Vector3.Zero
			? MovementComponent.LookAtDirection
			: (Player != null ? -Player.GlobalTransform.Basis.Z : Vector3.Forward);
		facing.Y = 0;
		if (facing.LengthSquared() > 0.0001f)
		{
			facing = facing.Normalized();
		}
		else
		{
			facing = Vector3.Forward;
		}

		if (InputComponent == null || !InputComponent.IsMoving())
		{
			return -facing;
		}

		Vector3 input = InputComponent.InputDirection;
		input.Y = 0;
		if (input.LengthSquared() < 0.0001f)
		{
			return -facing;
		}
		input = input.Normalized();

		return input;
	}
}