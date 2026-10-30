using Godot;

/// <summary>Known curses decorate the exact runtime model without replacing its materials or revealing unknown curses.</summary>
[GlobalClass]
public partial class ItemCurseWorldEffectPresenter : Node3D
{
	[Export] public WorldEffectHost Host { get; set; }
	[Export] public WorldEffectDefinition Effect { get; set; }
	[Export] public uint CosmeticSeed { get; set; } = 271;
	private EquipableItem _item;
	private WorldEffectHost _actor;
	private Node3D _model;
	private bool _active;

	/// <summary>Initialize before adding to the model tree. The presenter lifetime follows that model.</summary>
	public void Initialize(EquipableItem item, Node3D model, WorldEffectHost actor = null)
	{ _item = item; _model = model; _actor = actor; }
	public override void _Ready()
	{
		VisualHelper.Mark(this);
		if (Host == null || Effect == null || _item == null || _model == null)
		{ GD.PushError($"{Name}: curse presenter requires a runtime item, model, host and effect."); return; }
		if (VisualHelper.MeshBounds(_model, _model) is Aabb box)
		{
			Position = box.GetCenter();

		}
		this.SubscribeUntilExit(_item, i => i.Changed += Sync, i => i.Changed -= Sync);
		if (_actor != null) this.SubscribeUntilExit(_actor, a => a.GateChanged += Sync, a => a.GateChanged -= Sync);
		Sync();
	}
	private void Sync()
	{
		if (_actor?.IsStopped == true) { Host.StopForDeath(); return; }
		Host.SetOwnerSuppressed(_actor?.IsSuppressed == true);
		bool desired = _item.IsCursed && _item.CurseStatusKnown;
		if (_active == desired) return;
		_active = desired; Host.SetSustained(this, Effect, desired, CosmeticSeed, instance => instance is ItemCurseEffect curse && curse.Configure(_model));
	}
	public override void _ExitTree()
	{
		if (_active && IsInstanceValid(Host)) Host.SetSustained(this, Effect, false, CosmeticSeed);
	}
}