using Godot;
using System.Collections.Generic;

/// <summary>
/// Owns the spell nodes of the actor's equipped foci (spell weapons). Binding instantiates the focus's
/// self-contained spell scene as a child; releasing that focus cancels the spell, which ends every effect it still has
/// in the world, and frees it. Each equipped focus has its own bound spell node. The actor's attack controller casts
/// the bound spell at the release time of the cast attack, with the actor as caster.
/// </summary>
[GlobalClass]
public partial class SpellCaster : Node
{
	private readonly Dictionary<GodotObject, ICombatSpell> _boundSpells = [];

	public bool IsBoundTo(GodotObject focus) => focus != null && _boundSpells.ContainsKey(focus);

	public bool TryGetSpell(GodotObject focus, out ICombatSpell spell)
	{
		if (focus != null && _boundSpells.TryGetValue(focus, out spell))
		{
			return true;
		}
		spell = null;
		return false;
	}

	/// <summary>Binds <paramref name="focus"/> to cast <paramref name="spell"/>, releasing any previous spell on that focus.</summary>
	public bool Bind(SpellDefinition spell, GodotObject focus)
	{
		if (focus == null) return false;
		Release(focus);
		if (spell?.Instantiate() is not { } instance)
		{
			GD.PushError($"{Name}: {spell?.ResourcePath ?? "missing spell"} has no ICombatSpell scene.");
			return false;
		}
		AddChild((Node)instance);
		_boundSpells[focus] = instance;
		return true;
	}

	/// <summary>Ends the bound spell for <paramref name="focus"/>; any other item is ignored.</summary>
	public void Release(GodotObject focus)
	{
		if (focus == null || !_boundSpells.TryGetValue(focus, out var spell)) return;
		_boundSpells.Remove(focus);
		spell.Cancel();
		if (spell is Node node && IsInstanceValid(node)) node.QueueFree();
	}

	/// <summary>Releases all bound spells and cancels their world effects.</summary>
	public void Clear()
	{
		foreach (var spell in _boundSpells.Values)
		{
			spell.Cancel();
			if (spell is Node node && IsInstanceValid(node)) node.QueueFree();
		}
		_boundSpells.Clear();
	}

	public override void _ExitTree() => Clear();
}