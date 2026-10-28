using Godot;

public partial class InputComponent : Node
{
	[Signal] public delegate void DirectionChangedEventHandler(Vector3 direction);
	[Signal] public delegate void ActionEventHandler(int action);

	public Vector3 InputDirection { get; set; } = Vector3.Zero;

	/// <summary>
	/// Reads keyboard, mouse and gamepad state. Scripted harness runs turn it off so device state cannot
	/// perturb them; queries then report nothing pressed and <see cref="InputDirection"/> is left to the script.
	/// </summary>
	public bool DeviceEnabled { get; set; } = true;

	public bool IsMoving()
	{
		return InputDirection != Vector3.Zero;
	}

	private static readonly StringName ActionPrimary = "primary_action";
	private static readonly StringName ActionSecondary = "secondary_action";
	private static readonly StringName ActionInteract = "interact";
	private static readonly StringName ActionDodge = "dodge";
	private static readonly StringName ActionGuard = "guard";
	private static readonly StringName ActionMoveRight = "move_right";
	private static readonly StringName ActionMoveLeft = "move_left";
	private static readonly StringName ActionMoveUp = "move_up";
	private static readonly StringName ActionMoveDown = "move_down";

	private static readonly StringName[] QuickItemActions =
	[
		"quick_item_1",
		"quick_item_2",
		"quick_item_3",
		"quick_item_4"
	];

	private static readonly StringName[] LegacyActions =
	[
		"action_1",
		"action_2",
		"action_3",
		"action_4"
	];

	public bool IsPrimaryActionPressed()
	{
		return DeviceEnabled && Input.IsActionPressed(ActionPrimary);
	}

	public bool IsPrimaryActionJustPressed()
	{
		return DeviceEnabled && Input.IsActionJustPressed(ActionPrimary);
	}

	public bool IsSecondaryActionJustPressed()
	{
		return DeviceEnabled && Input.IsActionJustPressed(ActionSecondary);
	}

	public bool IsQuickItemJustPressed(int slotIndex)
	{
		if ((uint)slotIndex < (uint)QuickItemActions.Length)
		{
			return DeviceEnabled && (Input.IsActionJustPressed(QuickItemActions[slotIndex])
				|| Input.IsActionJustPressed(LegacyActions[slotIndex]));
		}
		return false;
	}

	public bool IsActionSlotPressed(int slotIndex)
	{
		return IsQuickItemJustPressed(slotIndex);
	}

	public bool IsInteractPressed()
	{
		return DeviceEnabled && Input.IsActionPressed(ActionInteract);
	}

	public bool IsInteractJustPressed()
	{
		return DeviceEnabled && Input.IsActionJustPressed(ActionInteract);
	}

	public bool IsDodgeJustPressed()
	{
		return DeviceEnabled && Input.IsActionJustPressed(ActionDodge);
	}

	public bool IsGuardPressed()
	{
		return DeviceEnabled && Input.IsActionPressed(ActionGuard);
	}

	public bool IsGuardJustPressed()
	{
		return DeviceEnabled && Input.IsActionJustPressed(ActionGuard);
	}

	public override void _Ready() { ProcessPhysicsPriority = CombatPhysics.Input; }

	public override void _PhysicsProcess(double delta)
	{
		if (!DeviceEnabled) return;
		// Get the camera's forward and right vectors
		Camera3D camera = GetViewport().GetCamera3D();
		Vector3 cameraForward = camera.GlobalTransform.Basis.Z;
		Vector3 cameraRight = camera.GlobalTransform.Basis.X;
		var direction = Vector3.Zero;

		// We check for each move input and update the direction accordingly.
		if (Input.IsActionPressed(ActionMoveRight))
		{
			direction += cameraRight;
		}
		if (Input.IsActionPressed(ActionMoveLeft))
		{
			direction -= cameraRight;
		}
		if (Input.IsActionPressed(ActionMoveUp))
		{
			direction -= cameraForward;
		}
		if (Input.IsActionPressed(ActionMoveDown))
		{
			direction += cameraForward;
		}

		if (direction != Vector3.Zero)
		{
			direction.Y = 0;
			direction = direction.Normalized();
		}

		InputDirection = direction;
		EmitSignalDirectionChanged(direction);
	}
}