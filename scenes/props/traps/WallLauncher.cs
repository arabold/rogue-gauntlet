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
	/// <summary>Seconds between shots while active; longer than any delay, so every shot clicks first.</summary>
	[Export(PropertyHint.Range, "0.6,5,0.05,suffix:s")] public float Interval { get; set; } = 1f;

	public int ShotsFired { get; private set; }
	/// <summary>Held on by its trigger: fires every interval until turned off.</summary>
	public bool IsActive { get; private set; }

	private bool _valid;
	/// <summary>Seconds until the armed shot, which has clicked once under <see cref="Delay"/>; negative while none is armed.</summary>
	private float _untilShot = -1;
	/// <summary>Seconds the click effect still shows.</summary>
	private float _clickLeft;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_valid = Shot is { IsValid: true };
		if (!_valid) GD.PushError($"{GetPath()}: a wall launcher needs a valid Shot.");
		ProcessPhysicsPriority = CombatPhysics.Damage;
		SetClickEffect(false);
		SetPhysicsProcess(false);
	}

	/// <summary>One shot, unless one is already on its way.</summary>
	public void Pulse(in TriggerActivation activation)
	{
		if (_untilShot < 0) Arm(Delay);
	}

	/// <summary>On fires a shot now and every interval after. Off stops the stream, though a shot already clicked still leaves.</summary>
	public void SetActive(bool active, in TriggerActivation activation)
	{
		IsActive = active;
		if (active && _untilShot < 0) Arm(Delay);
		else if (!active && _untilShot > Delay) _untilShot = -1;
	}

	public override void _PhysicsProcess(double delta)
	{
		float step = (float)delta;
		if (_clickLeft > 0 && (_clickLeft -= step) <= 0) SetClickEffect(false);
		if (_untilShot >= 0)
		{
			bool clicked = _untilShot <= Delay;
			_untilShot -= step;
			if (!clicked && _untilShot <= Delay) Click();
			if (_untilShot <= 0) Fire();
		}
		if (_untilShot < 0 && _clickLeft <= 0) SetPhysicsProcess(false);
	}

	private void Arm(float seconds)
	{
		if (!_valid) return;
		_untilShot = seconds;
		if (seconds <= Delay) Click();
		SetPhysicsProcess(true);
	}

	private void Click()
	{
		_clickLeft = Mathf.Max(Delay, ClickShown);
		SetClickEffect(true);
	}

	private void Fire()
	{
		ShotsFired++;
		Shot.Fire(this, CurrentDamage, DeriveSeed("trap-shot", $":{ShotsFired}"), new AttackIdentity(GetInstanceId(), (ulong)ShotsFired));
		if (IsActive) Arm(Interval);
		else _untilShot = -1;
	}

	private void SetClickEffect(bool shown)
	{
		if (ClickEffect != null) ClickEffect.Visible = shown;
	}
}