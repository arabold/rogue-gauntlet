using Godot;

/// <summary>Hides <see cref="Target"/> until fog of war uncovers the tile under this node. Tall world effects such as
/// torch flames rise above the black fog caps, so depth alone cannot conceal them; hiding the subtree also stops its
/// lights. Place the gate at the effect itself (e.g. the flame, not the wall plane it is mounted on), since that point
/// is tested. Standalone previews have no fog and show immediately. Explored tiles never re-fog, so polling stops on reveal.</summary>
[GlobalClass]
public partial class ExplorationGateComponent : Node3D
{
	/// <summary>Subtree hidden while undiscovered; its authored visibility is restored on reveal.</summary>
	[Export] public Node3D Target { get; set; }
	private ExplorationVisibility _exploration;
	private bool _authoredVisible;
	public override void _Ready()
	{
		if (Target == null) { GD.PushError($"{Name}: exploration gate requires an assigned Target."); SetProcess(false); return; }
		_exploration = new ExplorationVisibility(this);
		_authoredVisible = Target.Visible;
		Target.Visible = false;
		TryOpen();
	}
	public override void _Process(double delta) => TryOpen();
	private void TryOpen()
	{
		if (!_exploration.IsRevealed(GlobalPosition)) return;
		Target.Visible = _authoredVisible;
		SetProcess(false);
	}
}