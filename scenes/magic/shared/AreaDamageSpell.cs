using Godot;
using System.Collections.Generic;

/// <summary>Scene-composed damage-only area cast, with a separately authored visual.</summary>
public partial class AreaDamageSpell : Node, ICombatSpell
{
	[Export] public AreaDamageDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	public string DisplayName => Definition?.SpellName ?? "";
	public int LevelCount => Definition?.LevelCount ?? 0;
	private readonly AreaCastLifecycle _cast = new();
	private readonly List<PendingContact> _pending = [];
	private sealed record PendingContact(AreaCastLifecycle.Cast Cast, float Damage, AttackDefenseTraits Traits, DamageElement Element, double Due);
	private double _clock;

	public override void _Ready() => SetPhysicsProcess(false);
	public override void _PhysicsProcess(double delta)
	{
		_clock += delta;
		while (_pending.Count > 0 && _pending[0].Due <= _clock)
		{
			var contact = _pending[0]; _pending.RemoveAt(0);
			_cast.Resolve(contact.Cast, contact.Damage, contact.Traits, element: contact.Element);
		}
		if (_pending.Count == 0) SetPhysicsProcess(false);
	}

	/// <summary>Resolve one contact per visible hostile actor; empty areas still show the visual.</summary>
	public bool Cast(Node3D caster, int level, RandomNumberGenerator random)
	{
		if (Definition?.IsValidLevel(level) != true || VisualScene == null) return false;
		float radius = Definition.RadiusByLevel[level - 1], damage = Definition.DamageByLevel[level - 1];
		AttackDefenseTraits defense = Definition.DefenseTraits;
		if (!AreaCastLifecycle.TryCapture(this, caster, VisualRoot, random, radius, out var cast)) return false;
		var node = VisualScene.Instantiate();
		if (node is not Node3D visual || node is not IAreaSpellVisual area || !area.ConfigureArea(level, radius))
		{ node?.Free(); return false; }
		var visibility = AreaVisibility.Capture(cast.World, cast.Origin, radius);
		if (node is IAreaTargetVisual targetVisual)
			targetVisual.ConfigureTargets(System.Array.ConvertAll(System.Array.FindAll(cast.Targets,
				target => AreaSpellTargets.IsVisible(target, cast.World, cast.Origin)), target => target.GlobalPosition));
		VisualRoot.AddChild(visual); visual.GlobalPosition = cast.Origin; area.SetAreaVisibility(visibility);
		_cast.Track(visual);
		if (Definition.ContactDelay <= 0) _cast.Resolve(cast, damage, defense, element: Definition.Element);
		else
		{
			_pending.Add(new PendingContact(cast, damage, defense, Definition.Element, _clock + Definition.ContactDelay));
			_pending.Sort((a, b) => a.Due.CompareTo(b.Due));
			SetPhysicsProcess(true);
		}
		return true;
	}

	/// <summary>Invalidate later contacts and remove this focus's active visuals.</summary>
	public void Cancel()
	{
		_pending.Clear(); SetPhysicsProcess(false);
		AreaCastLifecycle.ClearVisuals(_cast.Cancel());
	}
	public override void _ExitTree() => Cancel();
}
