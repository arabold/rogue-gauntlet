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
		Aabb? bounds = null; Measure(_model, ref bounds);
		if (bounds is Aabb box)
		{
			Position = box.GetCenter();

		}
		this.SubscribeUntilExit(_item, i => i.Changed += Sync, i => i.Changed -= Sync);
		if (_actor != null) this.SubscribeUntilExit(_actor, a => a.GateChanged += Sync, a => a.GateChanged -= Sync);
		Sync();
	}
	private void Measure(Node node, ref Aabb? bounds)
	{
		if (node == this || VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			var box = (_model.GlobalTransform.AffineInverse() * mesh.GlobalTransform) * mesh.GetAabb();
			bounds = bounds.HasValue ? bounds.Value.Merge(box) : box;
		}
		foreach (Node child in node.GetChildren()) Measure(child, ref bounds);
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