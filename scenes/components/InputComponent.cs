using Godot;

[GlobalClass]
public partial class InputComponent : Node
{
	[Signal] public delegate void DirectionChangedEventHandler(Vector3 direction);
	[Signal] public delegate void ActionEventHandler(int action);

	public Vector3 InputDirection { get; set; } = Vector3.Zero;

	/// <summary>
	/// Reads keyboard, mouse and gamepad state. Scripted harness runs turn it off so device state cannot
	/// perturb them; queries then report nothing pressed and <see cref="InputDirection"/> is left to the script.
	/// </summary>
	[Export] public bool DeviceEnabled { get; set; } = true;

	/// <summary>
	/// Speed multiplier applied when holding the walk modifier key (Shift).
	/// </summary>
	[Export(PropertyHint.Range, "0.1,0.9,0.05")] public float WalkSpeedFactor { get; set; } = 0.4f;

	public bool IsMoving()
	{
		return InputDirection != Vector3.Zero;
	}

	private static readonly StringName ActionPrimary = "primary_action";
	private static readonly StringName ActionSecondary = "secondary_action";
	private static readonly StringName ActionInteract = "interact";
	private static readonly StringName ActionDodge = "dodge";
	private static readonly StringName ActionOffhand = "offhand_action";
	private static readonly StringName ActionGuard = "guard";
	private static readonly StringName ActionWalk = "walk";
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

	public bool IsOffhandPressed()
	{
		return DeviceEnabled && (Input.IsActionPressed(ActionOffhand) || Input.IsActionPressed(ActionGuard));
	}

	public bool IsOffhandJustPressed()
	{
		return DeviceEnabled && (Input.IsActionJustPressed(ActionOffhand) || Input.IsActionJustPressed(ActionGuard));
	}

	public bool IsGuardPressed() => IsOffhandPressed();

	public bool IsGuardJustPressed() => IsOffhandJustPressed();

	public override void _Ready() { ProcessPhysicsPriority = CombatPhysics.Input; }

	public override void _PhysicsProcess(double delta)
	{
		if (!DeviceEnabled) return;
		// Get the camera's forward and right vectors
		Camera3D camera = GetViewport()?.GetCamera3D();
		if (camera == null) return;
		Vector3 cameraForward = camera.GlobalTransform.Basis.Z;
		Vector3 cameraRight = camera.GlobalTransform.Basis.X;
		cameraForward.Y = 0;
		cameraRight.Y = 0;
		if (cameraForward.LengthSquared() > 0.0001f) cameraForward = cameraForward.Normalized();
		if (cameraRight.LengthSquared() > 0.0001f) cameraRight = cameraRight.Normalized();

		Vector2 raw = Input.GetVector(ActionMoveLeft, ActionMoveRight, ActionMoveUp, ActionMoveDown);
		var direction = Vector3.Zero;

		if (raw != Vector2.Zero)
		{
			float magnitude = Mathf.Clamp(raw.Length(), 0f, 1f);
			direction = (cameraRight * raw.X + cameraForward * raw.Y).Normalized() * magnitude;

			// Keyboard / digital walk modifier: holding Shift (or "walk" action) caps max speed to a walk pace (~40%)
			bool isWalkModifier = Input.IsActionPressed(ActionWalk) || Input.IsKeyPressed(Key.Shift);
			if (isWalkModifier && direction.Length() > WalkSpeedFactor)
			{
				direction = direction.Normalized() * WalkSpeedFactor;
			}
		}

		InputDirection = direction;
		EmitSignalDirectionChanged(direction);
	}
}