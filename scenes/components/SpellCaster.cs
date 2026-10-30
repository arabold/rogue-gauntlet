using Godot;

/// <summary>
/// Owns the spell node of the actor's current focus, the spell weapon it wields. Binding instantiates the focus's
/// self-contained spell scene as a child; releasing that focus cancels the spell, which ends every effect it still has
/// in the world, and frees it. Only one focus is bound at a time. The actor's attack controller casts the bound spell
/// at the release time of the cast attack, with the actor as caster.
/// </summary>
[GlobalClass]
public partial class SpellCaster : Node
{
	/// <summary>The bound spell, or null when no focus is bound.</summary>
	public ICombatSpell Current { get; private set; }
	private GodotObject _focus;

	public bool IsBoundTo(GodotObject focus) => focus != null && ReferenceEquals(focus, _focus);

	/// <summary>Replaces any bound focus with <paramref name="focus"/> casting <paramref name="spell"/>.</summary>
	public bool Bind(SpellDefinition spell, GodotObject focus)
	{
		Release(_focus);
		if (spell?.Instantiate() is not { } instance)
		{
			GD.PushError($"{Name}: {spell?.ResourcePath ?? "missing spell"} has no ICombatSpell scene.");
			return false;
		}
		AddChild((Node)instance);
		Current = instance;
		_focus = focus;
		return true;
	}

	/// <summary>Ends the bound spell if <paramref name="focus"/> is the bound focus; any other item is ignored.</summary>
	public void Release(GodotObject focus)
	{
		if (focus == null || !ReferenceEquals(focus, _focus)) return;
		var spell = Current;
		Current = null;
		_focus = null;
		spell.Cancel();
		if (spell is Node node && IsInstanceValid(node)) node.QueueFree();
	}
}