using Godot;

/// <summary>
/// Decorates an item's exact runtime model with the aura its first matching rule picks, without replacing its
/// materials: a known curse before a spell weapon's element. Item changes swap or end the aura.
/// </summary>
[GlobalClass]
public partial class ItemAuraPresenter : Node3D
{
	[Export] public WorldEffectHost Host { get; set; }
	/// <summary>Asked in order; the first aura wins.</summary>
	[Export] public ItemAuraRule[] Rules { get; set; } = [];
	[Export] public uint CosmeticSeed { get; set; } = 271;
	private EquipableItem _item;
	private WorldEffectHost _actor;
	private Node3D _model;
	private WorldEffectDefinition _active;

	/// <summary>Initialize before adding to the model tree. The presenter lifetime follows that model.</summary>
	public void Initialize(EquipableItem item, Node3D model, WorldEffectHost actor = null)
	{ _item = item; _model = model; _actor = actor; }
	public override void _Ready()
	{
		VisualHelper.Mark(this);
		if (Host == null || _item == null || _model == null)
		{ GD.PushError($"{Name}: aura presenter requires a runtime item, model and host."); return; }
		if (VisualHelper.MeshBounds(_model, _model) is Aabb box) Position = box.GetCenter();
		this.SubscribeUntilExit(_item, i => i.Changed += Sync, i => i.Changed -= Sync);
		if (_actor != null) this.SubscribeUntilExit(_actor, a => a.GateChanged += Sync, a => a.GateChanged -= Sync);
		Sync();
	}
	private void Sync()
	{
		if (_actor?.IsStopped == true) { Host.StopForDeath(); return; }
		Host.SetOwnerSuppressed(_actor?.IsSuppressed == true);
		WorldEffectDefinition desired = null;
		foreach (var rule in Rules) if ((desired = rule?.Resolve(_item)) != null) break;
		if (desired == _active) return;
		if (_active != null) Host.SetSustained(this, _active, false, CosmeticSeed);
		_active = desired;
		if (desired != null) Host.SetSustained(this, desired, true, CosmeticSeed, instance => instance is ItemAuraEffect aura && aura.Configure(_model));
	}
	public override void _ExitTree()
	{
		if (_active != null && IsInstanceValid(Host)) Host.SetSustained(this, _active, false, CosmeticSeed);
	}
}