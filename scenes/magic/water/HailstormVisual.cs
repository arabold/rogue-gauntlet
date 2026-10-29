using Godot;
using System.Collections.Generic;

/// <summary>
/// Hailstorm presentation: swirling snow over a frosted floor, and ice shards dropped wherever the caller asks.
/// Each shard marks its landing spot, falls for <see cref="FlightTime"/> and shatters on landing. Gameplay picks
/// the landing points and resolves the hits; the workbench can rain cosmetic shards on the same pattern.
/// The shard, mark and shatter emitters only emit on request, so any number of shards costs three draws.
/// </summary>
public partial class HailstormVisual : Node
{
	private const GpuParticles3D.EmitFlags Placed = GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.Velocity;
	private static readonly StringName ImpactAtParameter = "impact_at";
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public GpuParticles3D Snow { get; set; }
	/// <summary>Falling shards; their lifetime is the flight time, so each one ends exactly as it lands.</summary>
	[Export] public GpuParticles3D Shards { get; set; }
	/// <summary>Floor marks that close in on a landing spot during the fall, then splat into frost.</summary>
	[Export] public GpuParticles3D Marks { get; set; }
	[Export] public GpuParticles3D Shatter { get; set; }
	[Export] public int BaseSnow { get; set; } = 160;
	[Export] public int AdditionalSnowPerLevel { get; set; } = 80;
	[Export] public float FallHeight { get; set; } = 7f;
	/// <summary>Horizontal drift over a shard's fall, so the hail slants with the storm.</summary>
	[Export] public Vector3 Drift { get; set; } = new(1.4f, 0, .7f);
	[Export] public int ShatterPieces { get; set; } = 6;
	[Export] public float ShatterSpeed { get; set; } = 3.5f;
	public float FlightTime => (float)Shards.Lifetime;
	/// <summary>Shards fall until the storm starts fading out.</summary>
	public bool IsFalling => Timeline.Started && Timeline.Progress < Timeline.FadeOutStart;
	private readonly Queue<(Vector3 Point, float Due)> _landings = new();
	private readonly RandomNumberGenerator _cosmetic = new();
	private HailPattern _preview;
	private Vector3 _previewOrigin;
	private float _previewRadius, _clock;

	public void ConfigureLevel(int level)
	{
		if (Snow != null) Snow.Amount = BaseSnow + AdditionalSnowPerLevel * (Mathf.Clamp(level, 1, 3) - 1);
	}
	public override void _Ready()
	{
		if (Timeline == null || Snow == null || Shards == null || Marks?.MaterialOverride is not ShaderMaterial marks || Shatter == null)
		{ GD.PushError("Hailstorm requires a timeline, snow, shard, mark and shatter emitters."); SetProcess(false); return; }
		_cosmetic.Seed = GetInstanceId();
		marks.SetShaderParameter(ImpactAtParameter, FlightTime / (float)Marks.Lifetime);
	}
	/// <summary>Drops one shard onto a floor point and returns its flight time.</summary>
	public float DropShard(Vector3 point)
	{
		Vector3 start = point + Vector3.Up * FallHeight - Drift;
		Shards.EmitParticle(new Transform3D(Basis.Identity, start), (point - start) / FlightTime, Colors.White, Colors.White, (uint)Placed);
		Marks.EmitParticle(new Transform3D(Basis.Identity, point + Vector3.Up * .04f), Vector3.Zero, Colors.White, Colors.White, (uint)Placed);
		_landings.Enqueue((point, _clock + FlightTime));
		return FlightTime;
	}
	/// <summary>Workbench only: rain cosmetic shards on the gameplay pattern, since no field drives the preview.</summary>
	public void PreviewDrops(Vector3 origin, float radius, float shardsPerSecond, ulong seed)
	{
		_preview = new HailPattern(new RandomNumberGenerator { Seed = seed }, shardsPerSecond);
		_previewOrigin = origin; _previewRadius = radius;
	}
	public override void _Process(double delta)
	{
		_clock += (float)delta;
		if (_preview != null && IsFalling)
			for (int count = _preview.Advance((float)delta); count > 0; count--) DropShard(_preview.Next(_previewOrigin, _previewRadius));
		while (_landings.Count > 0 && _landings.Peek().Due <= _clock)
		{
			Vector3 point = _landings.Dequeue().Point + Vector3.Up * .1f;
			for (int i = 0; i < ShatterPieces; i++)
			{
				float angle = _cosmetic.RandfRange(0, Mathf.Tau);
				Vector3 velocity = new Vector3(Mathf.Cos(angle), _cosmetic.RandfRange(.6f, 1.4f), Mathf.Sin(angle)) * ShatterSpeed * _cosmetic.RandfRange(.5f, 1f);
				Shatter.EmitParticle(new Transform3D(Basis.Identity, point), velocity, Colors.White, Colors.White, (uint)Placed);
			}
		}
		if (Snow.Emitting && Timeline.Progress >= Timeline.FadeOutStart) Snow.Emitting = false;
	}
}