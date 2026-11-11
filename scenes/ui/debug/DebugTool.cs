using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// One debug capability hosted by a <see cref="DebugMenu"/>. It fills its own submenu, whose entries only call the tool's
/// public methods, so menu clicks, harness scenarios and tests run the same code. A tool acts on the menu's player and
/// world, never on a scene layout of its own. Entries are added with <see cref="AddAction"/>, <see cref="AddToggle"/>,
/// <see cref="AddRadio"/> and <see cref="AddSubmenu"/>, which assign ids and keep checks current.
/// </summary>
[GlobalClass]
public abstract partial class DebugTool : Node
{
	/// <summary>Label of this tool's submenu.</summary>
	[Export] public string Title { get; set; } = "";

	/// <summary>The host this tool acts through; set before the submenu is built, even while the menu is hidden.</summary>
	public DebugMenu Menu { get; internal set; }

	/// <summary>
	/// This tool's submenu, built the first time it opens or is read, so loading a scene never pays for menus nobody
	/// opens. Null while the debug menu is hidden (release builds) or the tool is unavailable.
	/// </summary>
	public PopupMenu Popup
	{
		get
		{
			EnsureBuilt();
			return _popup;
		}
		internal set => _popup = value;
	}

	/// <summary>Whether this scene supports the tool; an unavailable tool gets no submenu.</summary>
	public virtual bool IsAvailable => true;

	private PopupMenu _popup;
	private bool _built;
	private readonly Dictionary<PopupMenu, List<Action>> _actions = [];
	private readonly List<(PopupMenu Menu, int Index, Func<bool> IsChecked)> _checks = [];

	/// <summary>Fills this tool's submenu once, with the entry helpers.</summary>
	protected abstract void BuildMenu(PopupMenu menu);

	/// <summary>Updates what the entry helpers cannot: labels and disabled entries. Runs after every check is current.</summary>
	protected virtual void OnRefresh() { }

	/// <summary>Builds the submenu the first time, then brings every check, radio choice and label up to date.</summary>
	public void Refresh()
	{
		EnsureBuilt();
		foreach (var (menu, index, isChecked) in _checks)
		{
			menu.SetItemChecked(index, isChecked());
		}
		OnRefresh();
	}

	/// <summary>Adds an entry that runs <paramref name="action"/>; returns its id within <paramref name="menu"/>.</summary>
	protected int AddAction(PopupMenu menu, string label, Action action)
	{
		int id = Register(menu, action);
		menu.AddItem(label, id);
		return id;
	}

	/// <summary>Adds a check entry that shows <paramref name="get"/> and flips it through <paramref name="set"/>.</summary>
	protected int AddToggle(PopupMenu menu, string label, Func<bool> get, Action<bool> set)
	{
		int id = Register(menu, () => set(!get()));
		menu.AddCheckItem(label, id);
		_checks.Add((menu, menu.ItemCount - 1, get));
		return id;
	}

	/// <summary>Adds a radio entry, checked while <paramref name="isSelected"/>, that runs <paramref name="select"/>.</summary>
	protected int AddRadio(PopupMenu menu, string label, Func<bool> isSelected, Action select)
	{
		int id = Register(menu, select);
		menu.AddRadioCheckItem(label, id);
		_checks.Add((menu, menu.ItemCount - 1, isSelected));
		return id;
	}

	/// <summary>Adds a submenu entry labelled <paramref name="label"/> and returns the new, empty submenu.</summary>
	protected static PopupMenu AddSubmenu(PopupMenu parent, string label, string name = null)
	{
		var submenu = new PopupMenu();
		if (name != null) { submenu.Name = name; }
		parent.AddChild(submenu);
		parent.AddSubmenuNodeItem(label, submenu);
		return submenu;
	}

	/// <summary>Disables or enables the entry with <paramref name="id"/>, if <paramref name="menu"/> has one.</summary>
	protected static void SetDisabled(PopupMenu menu, int id, bool disabled)
	{
		int index = menu.GetItemIndex(id);
		if (index >= 0)
		{
			menu.SetItemDisabled(index, disabled);
		}
	}

	/// <summary>Kills an actor through its health, as lethal damage would; a dead or missing one is left alone.</summary>
	protected static void Kill(HealthComponent health)
	{
		if (health != null && !health.IsDead)
		{
			health.TakeDamage(health.CurrentHealth);
		}
	}

	private void EnsureBuilt()
	{
		if (_popup == null || _built)
		{
			return;
		}
		_built = true;
		BuildMenu(_popup);
	}

	/// <summary>Ids index each submenu's action list; the first entry of a submenu connects its press handler.</summary>
	private int Register(PopupMenu menu, Action action)
	{
		if (!_actions.TryGetValue(menu, out List<Action> actions))
		{
			_actions[menu] = actions = [];
			menu.IdPressed += id =>
			{
				actions[(int)id]();
				Refresh();
			};
		}
		actions.Add(action);
		return actions.Count - 1;
	}
}
