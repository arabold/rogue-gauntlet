using Godot;

/// <summary>
/// A light that flickers around its authored energy through a <see cref="LightFlicker"/>, starting at a random offset
/// so torches, candles and projectiles spawned together never flicker in step. Lights whose energy an effect driver
/// owns (fades, impact flashes) apply their flicker inside that driver instead.
/// </summary>
public partial class FlickerLight : OmniLight3D
{
	private LightFlicker _flicker;
	private float _energy;
	private float _time;
	private float _offset;

	/// <summary>Unassigned leaves the light steady at its authored energy.</summary>
	[Export]
	public LightFlicker Flicker
	{
		get => _flicker;
		set
		{
			_flicker = value;
			if (!IsNodeReady()) return;
			SetProcess(value != null);
			if (value == null) LightEnergy = _energy;
		}
	}

	public override void _Ready()
	{
		_energy = LightEnergy;
		_offset = LightFlicker.RandomOffset(this);
		SetProcess(Flicker != null);
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		LightEnergy = _energy * Flicker.Sample(_time, _offset);
	}
}