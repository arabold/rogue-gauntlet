using Godot;

/// <summary>Moves a body along a timed falling arc and signals its single impact.</summary>
public partial class FallingBodyMotion : Node
{
	[Signal] public delegate void ImpactEventHandler();
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public Node3D Body { get; set; }
	[Export] public Vector3 Start { get; set; } = new(-1.2f, 8, 0);
	[Export] public Vector3 End { get; set; } = new(0, .7f, 0);
	[Export] public float FlightTime { get; set; } = .65f;
	[Export] public float RotationSpeed { get; set; } = 3f;
	[Export] public bool HideOnImpact { get; set; }
	private bool _landed;

	/// <summary>Supplies a procedurally generated body before the first animation frame.</summary>
	public void ConfigureBody(Node3D body) => Body = body;
	public override void _Ready()
	{
		if (Timeline == null || FlightTime <= 0) { GD.PushError("Falling motion requires a timeline and positive flight time."); SetProcess(false); }
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started || Body == null || _landed) return;
		float t = Mathf.Clamp(Timeline.Elapsed / FlightTime, 0, 1);
		Body.Position = new Vector3(Mathf.Lerp(Start.X, End.X, t), Mathf.Lerp(Start.Y, End.Y, t * t), Mathf.Lerp(Start.Z, End.Z, t));
		if (t < 1) { Body.RotateY((float)delta * RotationSpeed); return; }
		_landed = true;
		if (HideOnImpact) Body.Visible = false;
		EmitSignalImpact();
	}
}