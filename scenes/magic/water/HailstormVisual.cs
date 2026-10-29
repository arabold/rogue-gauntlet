using Godot;

/// <summary>
/// Hailstorm presentation: swirling snow over a frosted floor, and ice shards dropped wherever the caller asks.
/// Each shard marks its landing spot and falls for <see cref="FlightTime"/>; its particle ends on landing, which
/// bursts splinters through the shard emitter's sub-emitter. Gameplay picks the landing points and resolves the
/// hits. The shard and mark emitters only emit on request, so any number of shards costs a few draws.
/// </summary>
public partial class HailstormVisual : Node
{
	private const GpuParticles3D.EmitFlags Placed = GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.Velocity;
	private static readonly StringName ImpactAtParameter = "impact_at";
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public GpuParticles3D Snow { get; set; }
	/// <summary>Falling shards; their lifetime is the flight time, so each one ends exactly as it lands.</summary>
	[Export] public GpuParticles3D Shards { get; set; }
	/// <summary>Floor marks that close in on a landing spot during the fall, then burst into a frost ring.</summary>
	[Export] public GpuParticles3D Marks { get; set; }
	[Export] public int BaseSnow { get; set; } = 160;
	[Export] public int AdditionalSnowPerLevel { get; set; } = 80;
	[Export] public float FallHeight { get; set; } = 7f;
	/// <summary>Horizontal drift over a shard's fall, so the hail slants with the storm.</summary>
	[Export] public Vector3 Drift { get; set; } = new(1.4f, 0, .7f);
	public float FlightTime => (float)Shards.Lifetime;
	/// <summary>Shards fall until the storm starts fading out.</summary>
	public bool IsFalling => Timeline.Started && Timeline.Progress < Timeline.FadeOutStart;

	public void ConfigureLevel(int level)
	{
		if (Snow != null) Snow.Amount = BaseSnow + AdditionalSnowPerLevel * (Mathf.Clamp(level, 1, 3) - 1);
	}
	public override void _Ready()
	{
		if (Timeline == null || Snow == null || Shards == null || Marks?.MaterialOverride is not ShaderMaterial marks)
		{ GD.PushError("Hailstorm requires a timeline and snow, shard and mark emitters."); SetProcess(false); return; }
		marks.SetShaderParameter(ImpactAtParameter, FlightTime / (float)Marks.Lifetime);
	}
	/// <summary>Drops one shard onto a floor point; it lands after <see cref="FlightTime"/>.</summary>
	public void DropShard(Vector3 point)
	{
		Vector3 start = point + Vector3.Up * FallHeight - Drift;
		Shards.EmitParticle(new Transform3D(Basis.Identity, start), (point - start) / FlightTime, Colors.White, Colors.White, (uint)Placed);
		Marks.EmitParticle(new Transform3D(Basis.Identity, point + Vector3.Up * .04f), Vector3.Zero, Colors.White, Colors.White, (uint)Placed);
	}
	public override void _Process(double delta)
	{
		if (Timeline.Progress < Timeline.FadeOutStart) return;
		Snow.Emitting = false; SetProcess(false);
	}
}