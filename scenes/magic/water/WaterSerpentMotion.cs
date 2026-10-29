using Godot;

/// <summary>Moves water-ribbon heads in rising, interlaced paths around the caster.</summary>
public partial class WaterSerpentMotion : Node
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public Node3D[] Heads { get; set; } = [];
	[Export] public Node3D SecondaryRibbon { get; set; }
	[Export] public GpuParticles3D Droplets { get; set; }
	[Export] public float Radius { get; set; } = 1.3f;
	[Export] public float OrbitSpeed { get; set; } = 6f;
	[Export] public float ColumnHeight { get; set; } = 2.8f;
	private int _level = 1;
	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public override void _Ready()
	{
		if (Timeline == null || Heads.Length < 2 || Heads[0] == null || Heads[1] == null || SecondaryRibbon == null || Droplets == null)
		{ GD.PushError("Water serpent requires a timeline, two heads, a secondary ribbon and droplets."); SetProcess(false); return; }
		SecondaryRibbon.Visible = _level >= 2;
		SecondaryRibbon.ProcessMode = _level >= 2 ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
		Droplets.Amount = 24 + _level * 16;
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		float time = Timeline.Elapsed;
		for (int i = 0; i < Heads.Length; i++)
		{
			float angle = time * OrbitSpeed + i * Mathf.Pi;
			float y = .4f + ColumnHeight * (.5f + .5f * Mathf.Sin(time * 3f + i * Mathf.Pi));
			float radius = Radius + .13f * Mathf.Sin(time * 3f + i);
			Heads[i].Position = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
		}
	}
}