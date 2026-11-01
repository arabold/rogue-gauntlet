using Godot;

/// <summary>
/// Menu-side 3D turntable that shows a character class playing its preview animation in an
/// isolated world. Deliberately separate from the item-icon Preview tool: this one animates
/// and frames a full-height character instead of baking a static thumbnail.
/// </summary>
public partial class CharacterPreview : SubViewport
{
	/// <summary>Turntable spin in radians per second; subtle by default.</summary>
	[Export] public float RotationSpeed { get; set; } = 0.35f;

	/// <summary>Delay in seconds before the turntable begins rotating after a character is shown or switched.</summary>
	[Export] public float RotationDelay { get; set; } = 0.5f;

	/// <summary>Whether rotation is currently paused waiting for the initial preview delay.</summary>
	public bool IsPaused => _pauseRemaining > 0f;

	private Node3D _turntable;
	private Node3D _model;
	private float _pauseRemaining;

	public override void _Ready()
	{
		_turntable = GetNode<Node3D>("Turntable");
	}

	public override void _Process(double delta)
	{
		if (_model == null)
		{
			return;
		}

		if (_pauseRemaining > 0f)
		{
			float consumed = Mathf.Min(_pauseRemaining, (float)delta);
			_pauseRemaining -= consumed;
			delta -= consumed;
			if (delta <= 0.0)
			{
				return;
			}
		}

		_turntable.RotateY(RotationSpeed * (float)delta);
	}

	/// <summary>Replaces the displayed model with the given class and plays its preview animation.</summary>
	public void ShowClass(CharacterClass characterClass)
	{
		_turntable ??= GetNode<Node3D>("Turntable");
		_turntable.Transform = Transform3D.Identity;
		_pauseRemaining = RotationDelay;

		_model?.QueueFree();
		_model = CharacterModel.Instantiate(characterClass);
		_turntable.AddChild(_model);

		var animationPlayer = _model.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
		if (animationPlayer != null && animationPlayer.HasAnimation(characterClass.PreviewAnimation))
		{
			animationPlayer.Play(characterClass.PreviewAnimation);
		}
	}

	/// <summary>Pauses rendering while hidden so the menu doesn't pay for an invisible viewport.</summary>
	public void SetActive(bool active)
	{
		RenderTargetUpdateMode = active ? UpdateMode.Always : UpdateMode.Disabled;
		SetProcess(active);
	}
}