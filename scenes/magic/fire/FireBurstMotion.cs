using Godot;

/// <summary>Expands a fire ribbon using the effect's shared clock.</summary>
public partial class FireBurstMotion : Node
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public MeshInstance3D[] Ribbons { get; set; } = [];
	/// <summary>Extra ring lights that fill the larger ring from level 2; hidden as a group at level 1.</summary>
	[Export] public Node3D HigherLevelLights { get; set; }
	[Export] public float ExpansionEnd { get; set; } = .65f;
	[Export] public float ExpansionPower { get; set; } = 2.2f;
	[Export] public float StartingRadius { get; set; } = .1f;
	private int _level = 1;
	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	/// <summary>The ribbons' expansion, which reaches the gameplay radius at full scale; area contacts follow it.</summary>
	public AreaFront Front => new(0, ExpansionEnd * Timeline.Duration, StartingRadius, 1, ExpansionPower);
	public override void _Ready()
	{
		if (Timeline == null || Ribbons.Length == 0 || System.Array.Exists(Ribbons, ribbon => ribbon == null))
		{ GD.PushError("Fire burst requires a timeline and assigned ribbons."); SetProcess(false); return; }
		foreach (var ribbon in Ribbons)
			if (ribbon.MaterialOverride is ShaderMaterial material) material.SetShaderParameter("brightness", 1.2f + .35f * _level);
		if (HigherLevelLights != null) HigherLevelLights.Visible = _level >= 2;
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		float radius = Front.Travel(Timeline.Elapsed);
		foreach (var ribbon in Ribbons) ribbon.Scale = new Vector3(radius, Mathf.Lerp(.45f, 1f, radius), radius);
	}
}