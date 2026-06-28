using Godot;

/// <summary>
/// Reusable read-out of an item's display info — rarity-colored name, rarity, and one line
/// per rolled affix modifier. Shared by the hover tooltip (via <see cref="ItemSlotButton"/>)
/// and the right-click context menu so both show the same base information. Builds its labels
/// in <see cref="SetItem"/>, so it works whether or not it is already in the tree.
/// </summary>
public partial class ItemDetailsView : VBoxContainer
{
	private const int MinWidth = 220;

	public void SetItem(Item item)
	{
		// A floor on width so the hover tooltip doesn't collapse to a sliver and wrap text one
		// character per line; long names still wrap within this width.
		CustomMinimumSize = new Vector2(MinWidth, 0);

		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}

		if (item == null)
		{
			return;
		}

		Color rarityColor = RarityPalette.TextColor(item);
		AddLine(ItemIdentity.ResolveDisplayName(item), rarityColor);

		if (item is not EquipableItem equipable)
		{
			return;
		}

		AddLine(equipable.Rarity.ToString(), rarityColor);
		if (equipable.Affixes == null)
		{
			return;
		}

		foreach (RolledAffix affix in equipable.Affixes)
		{
			if (affix?.Modifiers == null)
			{
				continue;
			}

			foreach (StatModifier modifier in affix.Modifiers)
			{
				AddLine(FormatModifier(modifier), Colors.White);
			}
		}
	}

	private void AddLine(string text, Color color)
	{
		// No autowrap: each entry is a single line, so the view's minimum size is its content
		// (the longest line wide, the lines tall) and both surfaces size to fit automatically.
		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		label.AddThemeColorOverride("font_color", color);
		AddChild(label);
	}

	private static string FormatModifier(StatModifier modifier)
	{
		string sign = modifier.Value < 0 ? "" : "+";
		return modifier.Op == ModifierOp.Percent
			? $"{sign}{modifier.Value * 100:0}% {modifier.Stat}"
			: $"{sign}{modifier.Value:0.##} {modifier.Stat}";
	}
}
