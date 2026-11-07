using Godot;

/// <summary>
/// Hides an actor taller than the fog caps until the ground it stands on is revealed, so its art never peeks over
/// unexplored space. Outside a generated level (previews, harnesses) the actor always shows. Ordinary enemies are
/// short enough not to need it.
/// </summary>
[GlobalClass]
public partial class TallActorVisibilityComponent : Node
{
	[Export] public EnemyBase Actor { get; set; }
	private Level _level;
	private ExplorationVisibility _exploration;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready() => _exploration = new ExplorationVisibility(this);

	public override void _Process(double delta)
	{
		// Fog caps are four units high; this actor is taller and must not peek over them.
		if (_level?.Floor == null || Actor == null) return;
		bool revealed = _exploration.IsRevealed(Actor.GlobalPosition);
		if (Actor.Visible != revealed) Actor.Visible = revealed;
	}
}