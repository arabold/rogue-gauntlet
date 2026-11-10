using Godot;

/// <summary>
/// One debug capability hosted by a <see cref="DebugMenu"/>. It fills its own submenu, whose entries only call the tool's
/// public methods, so menu clicks, harness scenarios and tests run the same code. A tool acts on the menu's player and
/// world, never on a scene layout of its own.
/// </summary>
[GlobalClass]
public abstract partial class DebugTool : Node
{
	/// <summary>Label of this tool's submenu.</summary>
	[Export] public string Title { get; set; } = "";

	/// <summary>The host this tool acts through; set before <see cref="BuildMenu"/>, even while the menu is hidden.</summary>
	public DebugMenu Menu { get; internal set; }

	/// <summary>The submenu this tool filled; null while the debug menu is hidden (release builds) or the tool unavailable.</summary>
	public PopupMenu Popup { get; internal set; }

	/// <summary>Whether this scene supports the tool; an unavailable tool gets no submenu.</summary>
	public virtual bool IsAvailable => true;

	/// <summary>Fills this tool's submenu once. Entry ids only need to be unique within it.</summary>
	public abstract void BuildMenu(PopupMenu menu);

	/// <summary>Updates checks, labels and disabled entries each time the debug menu opens.</summary>
	public virtual void RefreshMenu(PopupMenu menu) { }

	/// <summary>Sets the check of the entry with <paramref name="id"/>, if <paramref name="menu"/> has one.</summary>
	protected static void SetChecked(PopupMenu menu, long id, bool isChecked)
	{
		int index = menu.GetItemIndex((int)id);
		if (index >= 0)
		{
			menu.SetItemChecked(index, isChecked);
		}
	}

	/// <summary>Disables or enables the entry with <paramref name="id"/>, if <paramref name="menu"/> has one.</summary>
	protected static void SetDisabled(PopupMenu menu, long id, bool disabled)
	{
		int index = menu.GetItemIndex((int)id);
		if (index >= 0)
		{
			menu.SetItemDisabled(index, disabled);
		}
	}

	/// <summary>Checks the radio entry <paramref name="id"/> of <paramref name="menu"/> and unchecks its other radio entries.</summary>
	protected static void CheckRadio(PopupMenu menu, long id)
	{
		for (int i = 0; i < menu.ItemCount; i++)
		{
			if (menu.IsItemRadioCheckable(i))
			{
				menu.SetItemChecked(i, menu.GetItemId(i) == id);
			}
		}
	}
}
