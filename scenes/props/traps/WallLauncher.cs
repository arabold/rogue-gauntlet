using Godot;

/// <summary>
/// A wall-mounted mechanism a trigger sets off, the receiver of a plate trap: a pulse fires one fast shot through its
/// <see cref="Shot"/>, and while active it fires every <see cref="Interval"/>. Each shot leaves <see cref="Delay"/>
/// after a click (<see cref="ClickEffect"/>), too short to react to: unlike a <see cref="WallSpout"/> it draws no lane,
/// because its warning is its plate, which an attentive hero finds and walks around.
/// </summary>
[Tool, GlobalClass]
public partial class WallLauncher : Trap, ITriggerable
{
	/// <summary>How long the click effect stays up when the delay is shorter.</summary>
	private const float ClickShown = .35f;

	[Export] public TrapShotComponent Shot { get; set; }
	/// <summary>Optional: shown from the click until a moment after the shot (dust, a glow in the mouth).</summary>
	[Export] public Node3D ClickEffect { get; set; }
	/// <summary>Seconds from the click to the shot.</summary>
	[Export(PropertyHint.Range, "0,0.5,0.01,suffix:s")] public float Delay { get; set; } = .1f;
	/// <summary>Seconds between shots while active.</summary>
	[Export(PropertyHint.Range, "0.2,5,0.05,suffix:s")] public float Interval { get; set; } = 1f;

	[Signal] public delegate void FiredEventHandler(Node3D shot);

	public int ShotsFired { get; private set; }
	/// <summary>Held on by its trigger: fires every interval until turned off.</summary>
	public bool IsActive { get; private set; }

	private bool _valid;
	/// <summary>Seconds until the armed shot; negative while none is.</summary>
	private float _untilShot = -1;
	private bool _clicked;
	private float _clickLeft;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_valid = Shot is { IsValid: true } && Interval > Delay;
		if (!_valid) GD.PushError($"{GetPath()}: a wall launcher needs a valid Shot and an Interval longer than its Delay.");
		ProcessPhysicsPriority = CombatPhysics.Damage;
		SetClickEffect(false);
		SetPhysicsProcess(false);
	}

	/// <summary>One shot, unless one is already on its way.</summary>
	public void Pulse(in TriggerActivation activation)
	{
		if (_untilShot < 0) Arm(Delay);
	}

	/// <summary>
	/// On fires a shot now and every interval after; a restored one only arms the interval, so loading a floor never
	/// shoots. Off stops the stream, though a shot already clicked still leaves.
	/// </summary>
	public void SetActive(bool active, in TriggerActivation activation)
	{
		IsActive = active;
		if (active && _untilShot < 0) Arm(activation.Restored ? Interval : Delay);
		else if (!active && !_clicked) _untilShot = -1;
	}

	public override void _PhysicsProcess(double delta)
	{
		float step = (float)delta;
		if ((_clickLeft -= step) <= 0) SetClickEffect(false);
		if (_untilShot >= 0)
		{
			_untilShot -= step;
			if (!_clicked && _untilShot <= Delay) Click();
			if (_untilShot <= 0) Fire();
		}
		if (_untilShot < 0 && _clickLeft <= 0) SetPhysicsProcess(false);
	}

	private void Arm(float seconds)
	{
		if (!_valid) return;
		_untilShot = seconds;
		_clicked = false;
		if (seconds <= Delay) Click();
		SetPhysicsProcess(true);
	}

	private void Click()
	{
		_clicked = true;
		_clickLeft = Mathf.Max(Delay, ClickShown);
		SetClickEffect(true);
	}

	private void Fire()
	{
		ShotsFired++;
		var shot = Shot.Fire(this, CurrentDamage, DeriveSeed("trap-shot", $":{ShotsFired}"),
			new AttackIdentity(GetInstanceId(), (ulong)ShotsFired));
		if (IsActive) Arm(Interval);
		else
		{
			_untilShot = -1;
			_clicked = false;
		}
		EmitSignalFired(shot);
	}

	private void SetClickEffect(bool shown)
	{
		if (ClickEffect != null) ClickEffect.Visible = shown;
	}
}
