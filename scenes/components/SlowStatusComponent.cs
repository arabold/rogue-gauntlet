using Godot;

/// <summary>
/// Maps source-owned slow conditions to the actor's movement without blocking actions, and shows the optional
/// chilled appearance while any slow applies. Leave the effects host or appearance unassigned to show nothing.
/// </summary>
public partial class SlowStatusComponent : Node
{
	[Export] public BuffController Statuses { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public StatusCondition[] Conditions { get; set; } = [];
	[Export] public float[] SpeedFactors { get; set; } = [.8f, .65f, .5f];
	[Export] public WorldEffectHost Effects { get; set; }
	[Export] public WorldEffectDefinition Appearance { get; set; }
	private bool _shown;
	public bool IsSlowed => Health?.IsDead != true && Statuses != null && System.Array.Exists(Conditions, Statuses.HasCondition);
	public void SetSlowed(bool enabled)
	{
		if (Conditions.Length > 0) Statuses?.SetPersistentStatus(Conditions[0], this, enabled);
	}
	public override void _Ready()
	{
		if (Statuses == null || Movement == null || Health == null || Conditions.Length == 0 || Conditions.Length != SpeedFactors.Length
			|| System.Array.Exists(Conditions, condition => condition == null)
			|| System.Array.Exists(SpeedFactors, factor => !float.IsFinite(factor) || factor <= 0 || factor > 1))
		{ GD.PushError($"{Name}: slow status needs assigned conditions, factors, status, movement and health."); return; }
		if ((Effects == null) != (Appearance == null)) GD.PushWarning($"{Name}: assign both effects and appearance to show the slow.");
		this.SubscribeUntilExit(Statuses, status => status.BuffsChanged += Sync, status => status.BuffsChanged -= Sync);
		this.SubscribeUntilExit(Health, health => health.Died += Sync, health => health.Died -= Sync);
		Sync();
	}
	private void Sync()
	{
		float factor = 1;
		if (Health?.IsDead != true)
			for (int i = 0; i < Conditions.Length; i++)
				if (Statuses.HasCondition(Conditions[i])) factor = Mathf.Min(factor, SpeedFactors[i]);
		Movement.SetStatusSpeedFactor(this, factor);
		bool shown = factor < 1;
		if (Effects == null || Appearance == null || _shown == shown) return;
		_shown = shown; Effects.SetSustained(this, Appearance, shown, 0);
	}
	public override void _ExitTree()
	{
		if (IsInstanceValid(Statuses) && Statuses.IsInsideTree()) Statuses.RemoveStatuses(this);
		if (IsInstanceValid(Movement)) Movement.SetStatusSpeedFactor(this, 1);
		if (_shown && IsInstanceValid(Effects) && Effects.IsInsideTree()) Effects.SetSustained(this, Appearance, false, 0, immediate: true);
		_shown = false;
	}
}
