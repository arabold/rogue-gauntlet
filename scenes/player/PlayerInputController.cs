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

	private bool _requireDefenseRelease;
	private bool _guardIntent;
	public void SetGuardIntent(bool held) => _guardIntent = held;
	private bool IsGuardHeld => _guardIntent || InputComponent.IsGuardPressed();
	public void RequireFreshDefenseInput()
	{
		_requireDefenseRelease = true;
		_guardIntent = false;
		Player?.GuardComponent?.EndGuard();
	}

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
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Player.IsDead) { MovementComponent.Stop(); return; }
		if (_requireDefenseRelease && !IsGuardHeld) _requireDefenseRelease = false;
		// Active world interaction channel: check for input cancellation
		if (Player.InteractionComponent?.IsActive == true)
		{
			MovementComponent.SetInputDirection(Vector3.Zero);

			if (InputComponent.IsDodgeJustPressed())
			{
				Vector3 dodgeDir = GetDodgeDirection();
				if (Player.DodgeComponent?.TryDodge(dodgeDir) == DefenseRequestResult.Success)
				{
					return;
				}
			}

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

		// 1. Dodge: evaluated before other voluntary actions; can cancel attack startup or held guard
		if (InputComponent.IsDodgeJustPressed())
		{
			Vector3 dodgeDir = GetDodgeDirection();
			if (Player.DodgeComponent?.TryDodge(dodgeDir) == DefenseRequestResult.Success)
			{
				return;
			}
		}

		// 2. Guard: raise, hold, release
		if (!_requireDefenseRelease && InputComponent.IsGuardJustPressed())
		{
			if (Player.GuardComponent?.IsGuarding != true)
			{
				Player.GuardComponent?.TryBeginGuard(MovementComponent.LookAtDirection);
			}
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

		// Secondary technique: one request per new press
		if (InputComponent.IsSecondaryActionJustPressed())
		{
			ActionManager.TryPerformSecondary();
		}
		// Primary action: press or hold to repeat complete basic attacks at normal cadence
		else if (InputComponent.IsPrimaryActionPressed())
		{
			ActionManager.TryPerformPrimary();
		}
		else
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