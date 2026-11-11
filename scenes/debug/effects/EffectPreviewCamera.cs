using Godot;

/// <summary>
/// Mouse orbit plus the game's existing camera actions, shared by all effect previews. With a <see cref="GameView"/> it
/// starts at the game camera's angle, so a workbench shows its subject from the game's perspective, and
/// <see cref="ApplyGameView"/> also takes the game's zoom.
/// </summary>
public partial class EffectPreviewCamera : Camera3D
{
	[Export] public Vector3 Focus { get; set; } = new(1.6f, 1, 0);
	/// <summary>The game camera's angle and zoom; unassigned, the preview keeps its own starting view.</summary>
	[Export] public GameCameraProfile GameView { get; set; }
	private float _yaw = .55f, _elevation = .64f;
	/// <summary>Starts at the game's heading and downward angle; the authored zoom stays closer for inspection.</summary>
	public override void _Ready()
	{
		TakeGameAngle();
		UpdateView();
	}
	/// <summary>Returns to the game camera's heading, downward angle and zoom around <see cref="Focus"/>.</summary>
	public void ApplyGameView()
	{
		if (GameView == null) return;
		TakeGameAngle();
		Size = GameView.Size;
		UpdateView();
	}
	private void TakeGameAngle()
	{
		if (GameView == null) return;
		_yaw = Mathf.DegToRad(GameView.YawDegrees);
		_elevation = Mathf.DegToRad(GameView.PitchDegrees);
	}
	public override void _Process(double delta)
	{
		if (!Current) return;
		float rotate = Input.GetAxis("camera_rotate_left", "camera_rotate_right");
		float zoom = Input.GetAxis("camera_zoom_in", "camera_zoom_out");
		if (Mathf.IsZeroApprox(rotate) && Mathf.IsZeroApprox(zoom)) return;
		_yaw += rotate * Mathf.Pi * (float)delta;
		Size = Mathf.Clamp(Size + zoom * 12 * (float)delta, 6, 32); UpdateView();
	}
	public override void _UnhandledInput(InputEvent input)
	{
		if (!Current) return;
		if (input is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Right) != 0)
		{
			_yaw -= motion.Relative.X * .01f;
			_elevation = Mathf.Clamp(_elevation + motion.Relative.Y * .01f, .14f, 1.48f); UpdateView();
		}
		else if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{ Size = Mathf.Clamp(Size + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 6, 32); }
		else return;
		GetViewport().SetInputAsHandled();
	}
	private void UpdateView()
	{
		Position = Focus + new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_elevation), Mathf.Sin(_elevation), Mathf.Cos(_yaw) * Mathf.Cos(_elevation)) * 18;
		LookAt(Focus);
	}
}