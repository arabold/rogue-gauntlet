using Godot;

/// <summary>Scene-composed damage-only area cast, with a separately authored visual.</summary>
public partial class AreaDamageSpell : Node, ICombatSpell
{
	[Export] public AreaDamageDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	public string DisplayName => Definition?.SpellName ?? "";
	public int LevelCount => Definition?.LevelCount ?? 0;
	private readonly AreaCastLifecycle _cast = new();

	public override void _Ready() { ProcessPhysicsPriority = CombatPhysics.Damage; SetPhysicsProcess(false); }
	public override void _PhysicsProcess(double delta)
	{
		if (!_cast.Advance(delta)) SetPhysicsProcess(false);
	}

	/// <summary>Resolve one contact per visible hostile actor as the visual reaches it; empty areas still show the visual.</summary>
	public bool Cast(Node3D caster, in SpellPower power, RandomNumberGenerator random)
	{
		int level = power.Level;
		if (Definition?.IsValidLevel(level) != true || VisualScene == null) return false;
		float radius = Definition.RadiusByLevel[level - 1];
		AttackStats hit = power.Hit.Scaled(Definition.DamageScaleByLevel[level - 1]);
		AttackDefenseTraits defense = Definition.DefenseTraits;
		if (!AreaCastLifecycle.TryCapture(this, caster, VisualRoot, random, radius, out var cast)) return false;
		var node = VisualScene.Instantiate();
		if (node is not Node3D visual || node is not IAreaSpellVisual area || !area.ConfigureArea(level, radius))
		{ node?.Free(); return false; }
		var visibility = AreaVisibility.Capture(cast.World, cast.Origin, radius);
		if (node is IAreaTargetVisual targetVisual)
			targetVisual.ConfigureTargets(System.Array.ConvertAll(System.Array.FindAll(cast.Targets,
				target => AreaSpellTargets.IsVisible(target, cast.World, cast.Origin)), target => target.GlobalPosition));
		VisualRoot.AddChild(visual); visual.GlobalPosition = cast.Origin; area.SetAreaVisibility(visibility);
		_cast.Track(visual);
		_cast.Schedule(cast, Definition.ContactDelay, area.Front, hit, defense, Definition.PushAt(level), element: Definition.Element);
		return true;
	}

	/// <summary>Invalidate later contacts and remove this focus's active visuals.</summary>
	public void Cancel()
	{
		SetPhysicsProcess(false);
		AreaCastLifecycle.ClearVisuals(_cast.Cancel());
	}
	public override void _ExitTree() => Cancel();
}
