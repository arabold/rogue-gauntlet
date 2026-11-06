using Godot;

/// <summary>Runs a production player through manual and scripted movement for effect tuning.</summary>
public partial class MovementEffectPreview : Node3D
{
	[Export] public PackedScene PlayerScene { get; set; }
	[Export] public Node3D ActorSlot { get; set; }
	[Export] public CheckButton EffectsToggle { get; set; }
	[Export] public Button DemoButton { get; set; }
	[Export] public Button ResetButton { get; set; }
	[Export] public bool AutoPlayDemo { get; set; } = true;

	private Player _player;
	private float _demoTime;
	private float _demoStartDelay = 0.8f;
	private bool _demoPlaying;
	private bool _demoDashStarted;

	public override void _Ready()
	{
		_player = PlayerScene.Instantiate<Player>();
		_player.Name = "PreviewPlayer";
		ActorSlot.AddChild(_player);
		_player.InputComponent.DeviceEnabled = false;
		_player.InputController.SetPhysicsProcess(false);
		_player.MovementComponent.SetInputDirection(Vector3.Zero);

		if (EffectsToggle != null) EffectsToggle.Toggled += enabled => SetEffectsEnabled(enabled);
		if (DemoButton != null) DemoButton.Pressed += PlayDemo;
		if (ResetButton != null) ResetButton.Pressed += ResetPlayer;
		ProcessPhysicsPriority = CombatPhysics.Intent + 1;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (AutoPlayDemo && _demoStartDelay > 0)
		{
			_demoStartDelay -= (float)delta;
			if (_demoStartDelay <= 0) PlayDemo();
		}
		if (_demoPlaying)
		{
			UpdateDemo((float)delta);
			return;
		}

		Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		Vector3 direction = new(input.X, 0, input.Y);
		if (direction.LengthSquared() > 1) direction = direction.Normalized();
		Drive(direction);
		if (Input.IsActionJustPressed("dodge")) StartDodge(direction);
	}

	private void Drive(Vector3 direction)
	{
		_player.MovementComponent.SetInputDirection(direction);
		if (direction.LengthSquared() > 0.0001f) _player.MovementComponent.SetLookAtDirection(direction);
	}

	private void PlayDemo()
	{
		ResetPlayer();
		_demoTime = 0;
		_demoDashStarted = false;
		_demoPlaying = true;
	}

	private void UpdateDemo(float delta)
	{
		_demoTime += delta;
		if (_demoTime < 0.9f) Drive(Vector3.Forward);
		else if (_demoTime < 1.25f) Drive(Vector3.Zero);
		else if (!_demoDashStarted)
		{
			Drive(Vector3.Zero);
			StartDodge(Vector3.Forward);
			_demoDashStarted = true;
		}
		else if (_demoTime < 2.2f) Drive(Vector3.Zero);
		else
		{
			_demoPlaying = false;
			Drive(Vector3.Zero);
		}
	}

	private void StartDodge(Vector3 direction)
	{
		if (direction.LengthSquared() < 0.0001f) direction = _player.MovementComponent.LookAtDirection;
		_player.DodgeComponent?.TryDodge(direction);
	}

	private void SetEffectsEnabled(bool enabled)
	{
		_player.GetNode<MovementEffectsComponent>("MovementEffectsComponent").Enabled = enabled;
	}

	private void ResetPlayer()
	{
		_demoPlaying = false;
		_player.MovementComponent.ResetTransientMotion();
		_player.GlobalPosition = Vector3.Zero;
		_player.ResetPhysicsInterpolation();
		_player.GetNode<MovementEffectsComponent>("MovementEffectsComponent").ResetFeedback();
	}
}
