using Godot;

/// <summary>A spell light outside any effect timeline (such as a projectile's) that flickers around its authored energy.</summary>
public partial class FlickeringSpellLight : OmniLight3D
{
	[Export] public LightFlickerStyle Flicker { get; set; } = LightFlickerStyle.Flame;
	private float _energy;
	private float _time;
	private float _seed;

	public override void _Ready()
	{
		_energy = LightEnergy;
		_seed = GetInstanceId() % 997;
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		LightEnergy = _energy * SpellLightFlicker.Sample(Flicker, _time, _seed);
	}
}
