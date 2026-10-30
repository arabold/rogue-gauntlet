using Godot;

/// <summary>A standalone light that flickers around <see cref="Energy"/> through a <see cref="LightFlicker"/>.</summary>
[Tool, GlobalClass]
public partial class FlickerLight : OmniLight3D
{
	private LightFlicker _flicker;
	private float _energy = 1f;
	private float _time;

	/// <summary>Brightness the flicker multiplies. It replaces the light's own energy, which flickering drives.</summary>
	[Export(PropertyHint.Range, "0,16,0.01,or_greater")]
	public float Energy
	{
		get => _energy;
		set
		{
			_energy = Mathf.Max(0f, value);
			LightEnergy = _energy;
		}
	}

	/// <summary>Unassigned leaves the light steady at <see cref="Energy"/>.</summary>
	[Export]
	public LightFlicker Flicker
	{
		get => _flicker;
		set
		{
			_flicker = value;
			if (!IsNodeReady()) return;
			LightEnergy = _energy;
			UpdateProcessing();
		}
	}

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		// Energy is the one authored brightness; the light's own energy is runtime output.
		if (property["name"].AsStringName() == PropertyName.LightEnergy) property["usage"] = (int)PropertyUsageFlags.None;
	}

	public override void _Ready()
	{
		_time = LightFlicker.RandomOffset(this);
		UpdateProcessing();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationVisibilityChanged && IsNodeReady()) UpdateProcessing();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		LightEnergy = _energy * _flicker.Sample(_time);
	}

	/// <summary>Flickers only at runtime and while visible, so extinguished, fogged or inactive lights cost nothing
	/// and the editor previews the authored energy.</summary>
	private void UpdateProcessing() => SetProcess(_flicker != null && !Engine.IsEditorHint() && IsVisibleInTree());
}