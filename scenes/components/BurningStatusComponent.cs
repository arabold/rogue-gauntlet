using Godot;

/// <summary>Shows the shared fire effect while any source imposes the burning condition.</summary>
public partial class BurningStatusComponent : Node
{
	[Export] public BuffController Statuses { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public WorldEffectHost Effects { get; set; }
	[Export] public StatusCondition Condition { get; set; }
	[Export] public WorldEffectDefinition Appearance { get; set; }
	private bool _active;
	public bool IsBurning => Health?.IsDead != true && Statuses?.HasCondition(Condition) == true;
	public void SetBurning(bool enabled) => Statuses?.SetPersistentStatus(Condition, this, enabled);
	public override void _Ready()
	{
		if (Statuses == null || Health == null || Effects == null || Condition == null || Appearance == null)
		{ GD.PushError($"{Name}: burning status requires assigned status, health, effects, condition and appearance."); return; }
		this.SubscribeUntilExit(Statuses, statuses => statuses.BuffsChanged += Sync, statuses => statuses.BuffsChanged -= Sync);
		this.SubscribeUntilExit(Health, health => health.Died += Sync, health => health.Died -= Sync);
		Sync();
	}
	private void Sync()
	{
		bool active = IsBurning;
		if (_active == active) return;
		_active = active; Effects.SetSustained(this, Appearance, active, 0);
	}
	public override void _ExitTree()
	{
		if (IsInstanceValid(Statuses) && Statuses.IsInsideTree()) Statuses.RemoveStatuses(this);
		if (_active && IsInstanceValid(Effects) && Effects.IsInsideTree()) Effects.SetSustained(this, Appearance, false, 0, immediate: true);
		_active = false;
	}
}
