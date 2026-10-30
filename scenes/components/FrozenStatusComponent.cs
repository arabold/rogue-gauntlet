using Godot;

/// <summary>
/// Connects a frozen condition to actor control, pose and the shared visual host. The pose holds by disabling the
/// animation tree's processing, not its <c>Active</c> flag: reactivating a tree restarts its state machine from
/// <c>Start</c>, which would replay a monster's spawn clip on thaw.
/// </summary>
public partial class FrozenStatusComponent : Node
{
	[Export] public BuffController Statuses { get; set; }
	[Export] public StatusCondition Condition { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }
	[Export] public AnimationTree Animation { get; set; }
	[Export] public WorldEffectHost Effects { get; set; }
	[Export] public WorldEffectDefinition Appearance { get; set; }
	[Export] public HealthComponent Health { get; set; }
	private bool _frozen;
	private ProcessModeEnum _animationProcessMode;
	public bool IsFrozen => Health?.IsDead != true && Statuses?.HasCondition(Condition) == true;
	/// <summary>Toggle this component's persistent source without removing spell-owned freezes.</summary>
	public void SetFrozen(bool frozen) => Statuses?.SetPersistentStatus(Condition, this, frozen);

	public override void _Ready()
	{
		if (Statuses == null || Condition == null || ActionGate == null || Animation == null || Health == null)
		{
			GD.PushError($"{Name}: frozen status requires explicitly assigned status, action, animation and health dependencies.");
			return;
		}
		if ((Effects == null) != (Appearance == null)) GD.PushWarning($"{Name}: assign both effect host and frozen appearance to enable visuals.");
		this.SubscribeUntilExit(Statuses, s => s.BuffsChanged += Sync, s => s.BuffsChanged -= Sync);
		this.SubscribeUntilExit(Health, h => h.Died += Sync, h => h.Died -= Sync);
		Sync();
	}

	private void Sync()
	{
		bool frozen = IsFrozen;
		if (_frozen == frozen) return;
		_frozen = frozen;
		if (frozen)
		{
			ActionGate.SetStatusLock(this, true);
			_animationProcessMode = Animation.ProcessMode;
			Animation.ProcessMode = ProcessModeEnum.Disabled;
			if (Effects != null && Appearance != null) Effects.SetSustained(this, Appearance, true, 0);
		}
		else
		{
			ActionGate.SetStatusLock(this, false);
			Animation.ProcessMode = _animationProcessMode;
			if (Effects != null && Appearance != null) Effects.SetSustained(this, Appearance, false, 0);
		}
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Statuses) && Statuses.IsInsideTree()) Statuses.SetPersistentStatus(Condition, this, false);
		if (!_frozen) return;
		ActionGate.SetStatusLock(this, false);
		Animation.ProcessMode = _animationProcessMode;
		if (Appearance != null && IsInstanceValid(Effects) && Effects.IsInsideTree()) Effects.SetSustained(this, Appearance, false, 0, immediate: true);
		_frozen = false;
	}
}
