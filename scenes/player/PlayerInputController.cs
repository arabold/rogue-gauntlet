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

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		InputComponent ??= Player.InputComponent;
		MovementComponent ??= Player.MovementComponent;
		ActionManager ??= Player.ActionManager;
		InteractionController ??= Player.InteractionController;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Active world interaction channel: check for input cancellation
		if (Player.InteractionComponent?.IsActive == true)
		{
			MovementComponent.SetInputDirection(Vector3.Zero);

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
}