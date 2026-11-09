using Godot;
using System.Collections.Generic;

/// <summary>Owns spells bound to equipped foci and reusable innate spell instances for one actor.</summary>
[GlobalClass]
public partial class SpellCaster : Node
{
	private sealed record BoundSpell(SpellDefinition Definition, ICombatSpell Spell);

	private readonly Dictionary<GodotObject, BoundSpell> _boundSpells = [];
	private readonly Dictionary<ulong, ICombatSpell> _innate = [];
	private readonly RandomNumberGenerator _random = new();

	public void ConfigureRandom(ulong seed) => _random.Seed = seed;

	public bool IsBoundTo(GodotObject focus) => focus != null && _boundSpells.ContainsKey(focus);

	public bool TryGetSpell(GodotObject focus, out ICombatSpell spell)
	{
		if (focus != null && _boundSpells.TryGetValue(focus, out var bound))
		{
			spell = bound.Spell;
			return true;
		}
		spell = null;
		return false;
	}

	/// <summary>Binds <paramref name="focus"/> to its own instance of <paramref name="spell"/>.</summary>
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
		_boundSpells[focus] = new BoundSpell(spell, instance);
		return true;
	}

	/// <summary>Prepares bound, item or innate activations through the same scene-composed spell and target rules.</summary>
	public bool TryPrepare(SpellDefinition definition, GodotObject source, in SpellCastContext context, out SpellRelease release)
	{
		release = null;
		if (definition?.IsValidLevel(context.Power.Level) != true) return false;
		ICombatSpell spell = source != null && _boundSpells.TryGetValue(source, out var bound)
			&& ReferenceEquals(definition, bound.Definition) ? bound.Spell : GetOrCreateInnate(definition);
		if (spell == null || spell.LevelCount < context.Power.Level || !spell.CanCast(in context)) return false;
		release = new SpellRelease(spell, context, source);
		return true;
	}

	/// <summary>Releases an item or other non-combat activation at its existing source-owned commit boundary.</summary>
	public bool CastImmediate(SpellDefinition definition, GodotObject source, in SpellCastContext context)
	{
		if (!TryPrepare(definition, source, in context, out var release)) return false;
		var preparedContext = release.Context;
		return release.Spell.Cast(in preparedContext, _random);
	}

	/// <summary>Returns the actor-local spell instance, creating at most one per definition used by this actor.</summary>
	private ICombatSpell GetOrCreateInnate(SpellDefinition definition)
	{
		ulong id = definition.GetInstanceId();
		if (_innate.TryGetValue(id, out var existing) && existing is GodotObject current && GodotObject.IsInstanceValid(current))
			return existing;
		if (definition.Instantiate() is not { } spell) return null;
		AddChild((Node)spell);
		_innate[id] = spell;
		return spell;
	}

	/// <summary>Cancels cached innate spell effects when their actor dies.</summary>
	public void CancelInnate()
	{
		foreach (var spell in _innate.Values)
		{
			if (spell is not GodotObject node || !GodotObject.IsInstanceValid(node)) continue;
			spell.Cancel();
			if (node is Node scene) scene.QueueFree();
		}
		_innate.Clear();
	}

	/// <summary>Ends the bound spell for <paramref name="focus"/>; other foci are unaffected.</summary>
	public void Release(GodotObject focus)
	{
		if (focus == null || !_boundSpells.TryGetValue(focus, out var bound)) return;
		_boundSpells.Remove(focus);
		bound.Spell.Cancel();
		if (bound.Spell is Node node && IsInstanceValid(node)) node.QueueFree();
	}

	/// <summary>Releases every bound spell and cancels all actor-owned spell effects.</summary>
	public void Clear()
	{
		foreach (var bound in _boundSpells.Values)
		{
			bound.Spell.Cancel();
			if (bound.Spell is Node node && IsInstanceValid(node)) node.QueueFree();
		}
		_boundSpells.Clear();
		CancelInnate();
	}

	public override void _ExitTree() => Clear();
}
