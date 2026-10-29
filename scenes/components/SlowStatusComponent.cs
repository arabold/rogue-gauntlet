using Godot;

/// <summary>Maps source-owned slow conditions to the actor's movement without blocking actions.</summary>
public partial class SlowStatusComponent : Node
{
	[Export] public BuffController Statuses { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public StatusCondition[] Conditions { get; set; } = [];
	[Export] public float[] SpeedFactors { get; set; } = [.8f, .65f, .5f];
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
	}
	public override void _ExitTree()
	{
		if (IsInstanceValid(Statuses) && Statuses.IsInsideTree()) Statuses.RemoveStatuses(this);
		if (IsInstanceValid(Movement)) Movement.SetStatusSpeedFactor(this, 1);
	}
}
