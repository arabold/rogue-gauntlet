using Godot;

/// <summary>
/// Coordinates player input with movement, action slots, and interactions.
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
			else
			{
				MovementComponent.SetLookAtDirection(InputComponent.InputDirection);
			}
			return;
		}

		for (int i = 0; i < ActionManager.ActionSlotCount; i++)
		{
			if (InputComponent.IsActionSlotPressed(i))
			{
				ActionManager.TryPerformAction(i);
			}
		}

		if (InputComponent.IsInteractPressed())
		{
			InteractionController.TryInteract();
		}

		MovementComponent.SetInputDirection(InputComponent.InputDirection);
	}
}
