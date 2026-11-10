using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Casts any spell straight from the player at a chosen level, without a wand, mana, cooldown or wind-up, so a spell can be
/// tested on its own.
/// </summary>
[GlobalClass]
public partial class SpellCastTool : DebugTool
{
	/// <summary>Spells the submenu lists; without a pool the tool is unavailable.</summary>
	[Export] public SpellPool Spells { get; set; }

	/// <summary>What each entry casts. Ids index this list.</summary>
	private readonly List<(SpellDefinition Spell, int Level)> _castables = [];

	public override bool IsAvailable => Spells != null;

	/// <summary>One submenu per spell, listing every level it can be cast at.</summary>
	public override void BuildMenu(PopupMenu menu)
	{
		foreach (SpellDefinition spell in Spells.Spells.Where(spell => spell != null))
		{
			var levelMenu = new PopupMenu();
			for (int level = 1; level <= spell.LevelCount; level++)
			{
				if (!spell.IsValidLevel(level)) { continue; }
				_castables.Add((spell, level));
				levelMenu.AddItem($"Level {level}", _castables.Count - 1);
			}

			levelMenu.IdPressed += OnCastPressed;
			menu.AddChild(levelMenu);
			menu.AddSubmenuNodeItem(spell.DisplayName, levelMenu);
		}
	}

	/// <summary>
	/// Casts <paramref name="spell"/> from the player at once at <paramref name="level"/>, with the equipped weapon's hit
	/// (unarmed without one), the way a wand of that tier casts it at release. Returns whether the spell was cast.
	/// </summary>
	public bool Cast(SpellDefinition spell, int level)
	{
		Player player = Menu?.Player;
		if (player?.SpellCaster == null || player.IsDead)
		{
			GD.PrintErr("Cannot cast a spell without a living player.");
			return false;
		}
		if (spell == null || !spell.IsValidLevel(level))
		{
			GD.PrintErr($"{spell?.DisplayName ?? "A missing spell"} has no level {level}.");
			return false;
		}

		var power = new SpellPower(level, player.Stats.ResolveAttack(player.Inventory?.EquippedWeapon));
		var context = new SpellCastContext(player, null, SpellTargetIntent.Hostile, power);
		if (player.SpellCaster.CastImmediate(spell, this, in context))
		{
			return true;
		}

		GD.Print($"{spell.DisplayName} (Level {level}) could not be cast here.");
		return false;
	}

	/// <summary>
	/// The player's innate instance of <paramref name="spell"/>, the one <see cref="Cast"/> releases, created on first use;
	/// null without a living player or for a spell without a scene. Tests cast it directly with their own context.
	/// </summary>
	public ICombatSpell Resolve(SpellDefinition spell)
	{
		SpellCaster caster = Menu?.Player?.SpellCaster;
		return caster != null && caster.TryResolve(spell, null, 1, out ICombatSpell instance) ? instance : null;
	}

	private void OnCastPressed(long id)
	{
		if (id < 0 || id >= _castables.Count)
		{
			return;
		}

		var (spell, level) = _castables[(int)id];
		Cast(spell, level);
	}
}
