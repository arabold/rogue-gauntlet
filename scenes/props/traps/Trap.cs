using Godot;

/// <summary>
/// Anything that hurts as a trap: damage that grows with depth (<see cref="TrapDamage"/>), hits in the trap faction
/// through its <c>trap</c> group, which monsters ignore, and rolls seeded by its place in the level. A tool script
/// with an editor guard, because room emission configures traps in editor previews too; traps act only in a game.
/// </summary>
[Tool, GlobalClass]
public abstract partial class Trap : Node3D
{
	private static readonly StringName TrapGroup = "trap";

	/// <summary>Damage on the first depth; the balance model derives it.</summary>
	[Export] public int Damage { get; set; } = 4;
	/// <summary>Damage added per depth below the first, so a trap keeps its share of the hero's growing health.</summary>
	[Export] public float DamagePerDepth { get; set; }

	/// <summary>What a hit deals on its level's depth (the first outside a level), at least 1.</summary>
	public int CurrentDamage => TrapDamage.At(Damage, DamagePerDepth, _level?.Depth ?? 1);

	private Level _level;

	public override void _EnterTree()
	{
		if (Engine.IsEditorHint()) return;
		_level = this.GetAncestorOrNull<Level>();
		AddToGroup(TrapGroup);
	}

	/// <summary>A seed for this trap's own stream in <paramref name="domain"/>, distinct per <paramref name="source"/>.</summary>
	protected ulong DeriveSeed(string domain, string source = "") =>
		GenerationRandom.DeriveSeed(_level?.Seed ?? RunState.DefaultSeed, domain, $"{GetPath()}{source}");
}