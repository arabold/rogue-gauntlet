using Godot;

/// <summary>
/// Read-out of an item's display info: quality-colored name, quality, and one line per stat the item grants. The
/// run menu's Gear and Pack tabs show it under the selected item. Builds its labels in <see cref="SetItem"/>, so it
/// works whether or not it is already in the tree.
/// </summary>
public partial class ItemDetailsView : VBoxContainer
{
	private const int MinWidth = 220;
	private static readonly Color CursedColor = new("e05252");
	private static readonly Color UncursedColor = new(0.7f, 0.7f, 0.7f);
	private static readonly Color SpellColor = new("8fb8ff");

	/// <summary>Whether the first line names the item. Off where the name is shown separately, as in the run menu.</summary>
	[Export] public bool ShowName { get; set; } = true;
	/// <summary>Theme type variation of each line; unset, lines use the default label.</summary>
	[Export] public StringName LineVariation { get; set; } = "";

	/// <param name="viewer">The player the item is shown to, whose stats turn a spell weapon's spell into damage numbers.</param>
	public void SetItem(Item item, Player viewer = null)
	{
		// A floor on width so a short item's lines do not collapse to a sliver.
		CustomMinimumSize = CustomMinimumSize with { X = MinWidth };

		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}

		PopulateDetails(item, viewer);
		this.GetAncestorOrNull<AutoScrollViewport>()?.ResetScroll();
	}

	private void PopulateDetails(Item item, Player viewer)
	{
		if (item == null)
		{
			return;
		}

		Color qualityColor = ItemQualityPalette.Color(item);
		if (ShowName) { AddLine(ItemIdentity.ResolveDisplayName(item), qualityColor); }

		if (item is not EquipableItem equipable)
		{
			return;
		}

		// The quality is never secret; an unidentified type or item (an unworn ring, a Magic sword) hides only its stats.
		AddLine(equipable.Quality.ToString(), qualityColor);
		// The spell is part of the base identity, so it reads the same before identification.
		if (equipable is MagicStaff { BoundSpell: { } spell } staff)
		{
			foreach (string line in spell.Describe(staff.SpellLevel, SpellHit(staff, viewer))) AddLine(line, SpellColor);
		}

		if (ItemIdentity.AreStatsKnown(item))
		{
			AddModifierLines(equipable.AttributeModifiers);
		}
		else
		{
			AddLine("Unidentified");
		}

		// The curse status shows once known, with the penalty only after the curse itself is revealed.
		if (equipable.CurseStatusKnown)
		{
			AddLine(equipable.IsCursed ? "Cursed" : "Uncursed", equipable.IsCursed ? CursedColor : UncursedColor);
			AddModifierLines(equipable.CurseModifiers, CursedColor);
		}
	}

	/// <summary>
	/// The hit a spell weapon's damage lines quote: the wielded weapon's actual hit, or for another the hit it would deal
	/// in its place from what the player knows of it. Null, leaving damage out, without a viewer or while its stats are
	/// unidentified, so the numbers never reveal hidden affixes or curses.
	/// </summary>
	private static AttackStats? SpellHit(MagicStaff staff, Player viewer)
	{
		if (viewer?.Stats == null) return null;
		if (ReferenceEquals(viewer.Inventory?.EquippedWeapon, staff)) return viewer.Stats.ResolveAttack(staff);
		if (!ItemIdentity.AreStatsKnown(staff)) return null;
		return viewer.Stats.PreviewAttack(staff, staff.CurseStatusKnown ? staff.StatModifiers : staff.AttributeModifiers);
	}

	private void AddModifierLines(System.Collections.Generic.IEnumerable<StatModifier> modifiers, Color? color = null)
	{
		foreach (StatModifier modifier in modifiers)
		{
			if (modifier.Value != 0f)
			{
				// The stat's name as words: "Max Health", not the enum's "MaxHealth".
				AddLine($"{modifier.FormatValue()} {modifier.Stat.ToString().Capitalize()}", color);
			}
		}
	}

	/// <summary>Adds a line in the theme's text color, or in <paramref name="color"/> when the color itself says something.</summary>
	private void AddLine(string text, Color? color = null)
	{
		// No autowrap: each entry is a single line, so the view's minimum size is its content (the longest line wide,
		// the lines tall).
		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			ThemeTypeVariation = LineVariation,
		};
		if (color is { } shown)
		{
			label.AddThemeColorOverride("font_color", shown);
		}
		AddChild(label);
	}
}
