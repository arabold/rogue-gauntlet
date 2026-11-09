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
	public SpellDefinition BoundDefinition => _boundDefinition;
	private GodotObject _focus;
	private SpellDefinition _boundDefinition;
	private readonly System.Collections.Generic.Dictionary<ulong, ICombatSpell> _innate = [];
	private readonly RandomNumberGenerator _random = new();

	public void ConfigureRandom(ulong seed) => _random.Seed = seed;

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
		_boundDefinition = spell;
		return true;
	}

	/// <summary>Prepare an external or innate activation through the same scene-composed spell and target rules.</summary>
	public bool TryPrepare(SpellDefinition definition, GodotObject source, in SpellCastContext context, out SpellRelease release)
	{
		release = null;
		if (definition?.IsValidLevel(context.Power.Level) != true) return false;
		ICombatSpell spell = source != null && ReferenceEquals(source, _focus) && ReferenceEquals(definition, _boundDefinition)
			? Current : GetOrCreateInnate(definition);
		if (spell == null || spell.LevelCount < context.Power.Level || !spell.CanCast(in context)) return false;
		release = new SpellRelease(spell, context, source);
		return true;
	}

	/// <summary>Release an item or other non-combat activation at its existing source-owned commit boundary.</summary>
	public bool CastImmediate(SpellDefinition definition, GodotObject source, in SpellCastContext context)
	{
		if (!TryPrepare(definition, source, in context, out var release)) return false;
		var preparedContext = release.Context;
		return release.Spell.Cast(in preparedContext, _random);
	}

	/// <summary>Return the actor-local spell instance, creating at most one instance per definition used by this actor.</summary>
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

	/// <summary>Cancel cached innate spell effects when their actor dies.</summary>
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

	/// <summary>Ends the bound spell if <paramref name="focus"/> is the bound focus; any other item is ignored.</summary>
	public void Release(GodotObject focus)
	{
		if (focus == null || !ReferenceEquals(focus, _focus) || Current == null) return;
		var spell = Current;
		Current = null;
		_focus = null;
		_boundDefinition = null;
		spell.Cancel();
		if (spell is Node node && IsInstanceValid(node)) node.QueueFree();
	}

	public override void _ExitTree()
	{
		Release(_focus);
		CancelInnate();
	}
}
