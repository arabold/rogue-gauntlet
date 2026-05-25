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
			MovementComponent.SetLookAtDirection(-InputComponent.InputDirection);
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
