using Godot;

/// <summary>Connects one actor condition to a sustained visual and an optional persistent manual source.</summary>
public partial class StatusAppearanceComponent : Node
{
	[Export] public BuffController Statuses { get; set; }
	[Export] public StatusCondition Condition { get; set; }
	[Export] public WorldEffectHost Host { get; set; }
	[Export] public WorldEffectDefinition Effect { get; set; }
	public bool IsActive => Statuses?.Health?.IsDead != true && Statuses?.HasCondition(Condition) == true;
	private bool _shown;

	public override void _Ready()
	{
		if (Statuses == null || Condition == null)
		{ GD.PushError($"{Name}: assign the actor's status controller and condition."); return; }
		if ((Host == null) != (Effect == null)) GD.PushWarning($"{Name}: assign both effect host and appearance to enable visuals.");
		this.SubscribeUntilExit(Statuses, s => s.BuffsChanged += Sync, s => s.BuffsChanged -= Sync);
		if (Statuses.Health != null) this.SubscribeUntilExit(Statuses.Health, h => h.Died += Sync, h => h.Died -= Sync);
		if (Host != null && !Host.IsNodeReady()) this.SubscribeUntilExit(Host, h => h.Ready += Sync, h => h.Ready -= Sync);
		Sync();
	}

	private void Sync()
	{
		bool active = IsActive;
		if (Host == null || Effect == null || !Host.IsNodeReady() || _shown == active) return;
		if (Host.SetSustained(this, Effect, active, 0) != WorldEffectResult.Rejected) _shown = active;
	}

	/// <summary>Toggle this component's persistent source without disturbing other condition sources.</summary>
	public void SetActive(bool active) => Statuses?.SetPersistentStatus(Condition, this, active);

	public override void _ExitTree()
	{
		if (IsInstanceValid(Statuses) && Statuses.IsInsideTree()) Statuses.SetPersistentStatus(Condition, this, false);
		if (Effect != null && IsInstanceValid(Host) && Host.IsInsideTree()) Host.SetSustained(this, Effect, false, 0);
		_shown = false;
	}
}
