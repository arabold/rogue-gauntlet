using Godot;

/// <summary>Animates tiered flame ribbons around the caster.</summary>
public partial class FireSweepMotion : Node
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public Node3D[] Orbits { get; set; } = [];
	[Export] public Node3D[] Arms { get; set; } = [];
	[Export] public Node3D[] Heads { get; set; } = [];
	[Export] public GpuParticles3D[] Emitters { get; set; } = [];
	[Export] public float Revolutions { get; set; } = 2.4f;
	[Export] public float InnerRadius { get; set; } = 2.6f;
	[Export] public float RingSpacing { get; set; } = 1.5f;
	private int _level = 1;
	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public override void _Ready()
	{
		if (Timeline == null || Orbits.Length == 0 || Arms.Length != Orbits.Length || Heads.Length != Orbits.Length || Emitters.Length != Orbits.Length
			|| System.Array.Exists(Orbits, node => node == null) || System.Array.Exists(Arms, node => node == null)
			|| System.Array.Exists(Heads, node => node == null) || System.Array.Exists(Emitters, node => node == null))
		{ GD.PushError("Fire sweep requires a timeline and matching orbit, arm, head and emitter assignments."); SetProcess(false); return; }
		for (int i = 0; i < Orbits.Length; i++)
		{
			bool active = i < _level;
			if (i < Heads.Length) Heads[i].Position = new Vector3(InnerRadius + i * RingSpacing, 1, 0);
			Orbits[i].Visible = active;
			Orbits[i].ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
			if (i < Emitters.Length) Emitters[i].Emitting = active;
		}
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		for (int i = 0; i < _level && i < Arms.Length && i < Heads.Length; i++)
		{
			float phase = Mathf.Tau * i / _level;
			Arms[i].Rotation = Vector3.Up * (-Timeline.Progress * Mathf.Tau * Revolutions + phase);
			Heads[i].Position = new Vector3(InnerRadius + i * RingSpacing + .3f * Mathf.Sin(Timeline.Elapsed * 6f + phase), 1f + .35f * Mathf.Sin(Timeline.Elapsed * 5f + phase), 0);
		}
		if (Timeline.Progress >= Timeline.FadeOutStart)
			foreach (var emitter in Emitters) emitter.Emitting = false;
	}
}