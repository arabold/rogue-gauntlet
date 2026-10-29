using Godot;

/// <summary>Observes the actor's invisible condition and presents it through the shared effect host.</summary>
[GlobalClass]
public partial class InvisibilityComponent : Node
{
	[Signal] public delegate void ChangedEventHandler();
	[Export] public BuffController Statuses { get; set; }
	[Export] public StatusCondition Condition { get; set; }
	[Export] public WorldEffectHost Host { get; set; }
	[Export] public WorldEffectDefinition Effect { get; set; }
	public Node3D Target => Host?.SurfaceController?.Target;
	public bool IsInvisible => Statuses?.Health?.IsDead != true && Statuses?.HasCondition(Condition) == true;
	public float Amount => Host?.SurfaceController?.CloakAmount ?? 0;
	private bool _state, _shown, _immediate;

	public override void _Ready()
	{
		if (Statuses == null || Condition == null)
		{ GD.PushError($"{Name}: assign the actor's status controller and invisible condition."); return; }
		if ((Host == null) != (Effect == null)) GD.PushWarning($"{Name}: assign both effect host and invisibility appearance to enable visuals.");
		this.SubscribeUntilExit(Statuses, s => s.BuffsChanged += Sync, s => s.BuffsChanged -= Sync);
		if (Statuses.Health != null) this.SubscribeUntilExit(Statuses.Health, h => h.Died += Sync, h => h.Died -= Sync);
		if (Host != null && !Host.IsNodeReady()) this.SubscribeUntilExit(Host, h => h.Ready += Sync, h => h.Ready -= Sync);
		Sync();
	}

	private void Sync()
	{
		bool invisible = IsInvisible;
		bool changed = invisible != _state;
		_state = invisible;
		ApplyAppearance(_immediate);
		if (changed) EmitSignalChanged();
	}

	private void ApplyAppearance(bool immediate)
	{
		if (Host == null || Effect == null || !Host.IsNodeReady() || (!immediate && _shown == _state)) return;
		var result = Host.SetSustained(this, Effect, _state, 0, immediate: immediate);
		if (result != WorldEffectResult.Rejected) _shown = _state;
	}

	/// <summary>Toggle this component's persistent source; other invisibility sources remain active.</summary>
	public void SetInvisible(bool invisible, bool immediate = false)
	{
		if (Statuses == null || Condition == null || !IsInsideTree()) return;
		_immediate = immediate;
		try
		{
			Statuses.SetPersistentStatus(Condition, this, invisible);
			ApplyAppearance(immediate);
		}
		finally { _immediate = false; }
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Statuses) && Statuses.IsInsideTree()) Statuses.SetPersistentStatus(Condition, this, false);
		if (Effect != null && IsInstanceValid(Host) && Host.IsInsideTree()) Host.SetSustained(this, Effect, false, 0);
		_shown = false;
	}
}
