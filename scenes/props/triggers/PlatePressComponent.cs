using Godot;

/// <summary>
/// Sinks a raised plate a little while its trigger output is on and lets it rise again once off, so the hero sees what
/// they stepped on give way. It moves only <see cref="Target"/>; what the plate sets off is its output's business.
/// </summary>
[GlobalClass]
public partial class PlatePressComponent : Node
{
	[Export] public TriggerOutputComponent Output { get; set; }
	/// <summary>The part that moves: the plate's slab.</summary>
	[Export] public Node3D Target { get; set; }
	[Export(PropertyHint.Range, "0,0.3,0.005,suffix:m")] public float Depth { get; set; } = .1f;
	[Export(PropertyHint.Range, "0.01,1,0.01,suffix:s")] public float SinkSeconds { get; set; } = .08f;
	[Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float RiseSeconds { get; set; } = .3f;

	private float _rest;
	private Tween _tween;

	public override void _Ready()
	{
		if (Output == null || Target == null)
		{
			GD.PushError($"{GetPath()}: assign the plate's Output and the Target it sinks.");
			return;
		}
		_rest = Target.Position.Y;
		this.SubscribeUntilExit(Output, output => output.Switched += OnSwitched, output => output.Switched -= OnSwitched);
	}

	private void OnSwitched(bool on, Node3D activator)
	{
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(Target, "position:y", on ? _rest - Depth : _rest, on ? SinkSeconds : RiseSeconds)
			.SetTrans(Tween.TransitionType.Quad).SetEase(on ? Tween.EaseType.Out : Tween.EaseType.InOut);
	}
}