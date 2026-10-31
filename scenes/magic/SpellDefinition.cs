using Godot;
using System.Collections.Generic;

/// <summary>
/// One castable spell as a spell weapon carries it: the self-contained spell scene, the cast animation and the mana it
/// costs per level. A wand or staff binds one; its tier picks the level. The authored <c>.tres</c> file is the
/// identity: reference it as an external resource so <c>ItemFactory</c>'s deep duplicate keeps it shared and saves can
/// store its path.
/// </summary>
[Tool]
[GlobalClass]
public partial class SpellDefinition : Resource
{
	/// <summary>The player-facing spell name, e.g. "Meteor".</summary>
	[Export] public string DisplayName { get; set; } = "";
	/// <summary>The item-name tail of a weapon that carries the spell, e.g. "of Meteors".</summary>
	[Export] public string ItemTitle { get; set; } = "";
	/// <summary>The spell's element, which also colors the carrying weapon's missiles.</summary>
	[Export] public MagicElement Element { get; set; }
	/// <summary>A self-contained scene whose root implements <see cref="ICombatSpell"/>.</summary>
	[Export] public PackedScene SpellScene { get; set; }
	/// <summary>Mana per cast, one entry per level; the spell has as many levels as entries.</summary>
	[Export] public float[] ManaCostByLevel { get; set; } = [];
	/// <summary>Seconds before the secondary action can cast again; 0 leaves the spell limited by mana alone.</summary>
	[Export(PropertyHint.Range, "0,10,0.05")] public float Cooldown { get; set; }
	/// <summary>Cast timing and animation; it carries no projectile, since the spell releases at its contact time.</summary>
	[Export] public AttackDefinition Cast { get; set; }

	public int LevelCount => System.Math.Min(SpellLevels.Max, ManaCostByLevel?.Length ?? 0);

	/// <summary>Whether the definition can cast at <paramref name="level"/>; the spell scene validates its own levels on cast.</summary>
	public bool IsValidLevel(int level) => level >= 1 && level <= LevelCount && SpellScene != null && Cast != null
		&& float.IsFinite(ManaCostByLevel[level - 1]) && ManaCostByLevel[level - 1] >= 0 && float.IsFinite(Cooldown) && Cooldown >= 0;

	public float ManaCost(int level) => ManaCostByLevel[level - 1];

	/// <summary>
	/// Tooltip lines for a weapon casting the spell at <paramref name="level"/>: name and level, mana cost, then what
	/// the spell scene says a cast with <paramref name="hit"/> does, which a null hit (unknown stats) leaves out. The
	/// scene is instantiated briefly, outside the tree, to read its definition.
	/// </summary>
	public List<string> Describe(int level, AttackStats? hit)
	{
		List<string> lines = [$"{DisplayName} (Level {level})"];
		if (!IsValidLevel(level)) return lines;
		lines.Add($"{ManaCost(level):0} mana per cast");
		if (hit is { } known && Instantiate() is { } spell)
		{
			lines.AddRange(spell.DescribeLevel(new SpellPower(level, known)));
			((Node)spell).Free();
		}
		return lines;
	}

	/// <summary>A new instance of the spell scene, or null (with nothing left behind) when its root is not a spell.</summary>
	public ICombatSpell Instantiate()
	{
		Node node = SpellScene?.Instantiate();
		if (node is ICombatSpell spell) return spell;
		node?.Free();
		return null;
	}
}