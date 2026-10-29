using Godot;
using System.Linq;

/// <summary>Composes the storm's clipped field with the workbench lightning and ground cracks.</summary>
public partial class ThunderstormVisual : ScaledAreaVisual, IAreaTargetVisual
{
	[Export] public PackedScene ArcScene { get; set; }
	[Export] public PackedScene CracksScene { get; set; }
	private Vector3[] _targets = [];
	private int _level;
	private float _radius;
	public override bool ConfigureArea(int level, float radius)
	{
		if (ArcScene == null || CracksScene == null || !base.ConfigureArea(level, radius)) return false;
		_level = level; _radius = radius; Timeline.FreeOnCompletion = false; return true;
	}
	public void ConfigureTargets(Vector3[] positions) => _targets = positions ?? [];
	public override void SetAreaVisibility(AreaVisibility visibility)
	{
		base.SetAreaVisibility(visibility);
		var random = new RandomNumberGenerator { Seed = GetInstanceId() * 7919UL };
		int count = 3 + _level * 2;
		for (int i = 0; i < count; i++)
		{
			float angle = random.RandfRange(0, Mathf.Tau), distance = random.RandfRange(.5f, _radius);
			Vector3 point = i < _targets.Length ? _targets[i] : GlobalPosition + new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
			if (!visibility.Contains(point)) continue;
			if (ArcScene.Instantiate() is LightningArc arc)
			{
				arc.TopLevel = true; arc.Configure(point + Vector3.Up * 5, point + Vector3.Up * .15f, random.Randi());
				arc.Width *= .75f + .2f * _level; arc.Timeline.StartDelay = i * .1f;
				AddChild(arc); arc.Timeline.SetVisibility(visibility);
			}
			if (CracksScene.Instantiate() is VisualEffectInstance cracks)
			{
				cracks.TopLevel = true; cracks.Timeline.StartDelay = i * .1f;
				AddChild(cracks); cracks.GlobalPosition = point; cracks.SetVisibility(visibility);
			}
		}
	}
	public override void _Process(double delta)
	{
		if (Timeline?.FreeOnCompletion == false && Timeline.Progress >= 1
			&& !GetChildren().Any(child => child is LightningArc or VisualEffectInstance)) QueueFree();
	}
}
