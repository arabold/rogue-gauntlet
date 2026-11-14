using Godot;
using System;
using System.Collections.Generic;

/// <summary>Scene-composed damage-only area cast, with a separately authored visual.</summary>
public partial class AreaDamageSpell : Node, ICombatSpell
{
	[Export] public AreaDamageDefinition Definition { get; set; }
	[Export] public PackedScene VisualScene { get; set; }
	[Export] public Node3D VisualRoot { get; set; }
	public string DisplayName => Definition?.SpellName ?? "";
	public int LevelCount => Definition?.LevelCount ?? 0;
	public DamageDeliveryKind? Delivery => DamageDeliveryKind.AreaImpact;
	public IEnumerable<string> DescribeLevel(in SpellPower power) => Definition?.DescribeLevel(power) ?? [];
	private readonly AreaCastLifecycle _cast = new();
	public bool CanCast(in SpellCastContext context) => GodotObject.IsInstanceValid(context.Caster)
		&& Definition?.IsValidLevel(context.Power.Level) == true && VisualScene != null;

	public override void _Ready() { ProcessPhysicsPriority = CombatPhysics.Damage; SetPhysicsProcess(false); }
	public override void _PhysicsProcess(double delta)
	{
		if (!_cast.Advance(delta)) SetPhysicsProcess(false);
	}

	/// <summary>Resolve one contact per visible hostile actor as the visual reaches it; empty areas still show the visual.</summary>
	public bool Cast(in SpellCastContext context, RandomNumberGenerator random)
	{
		Node3D caster = context.Caster;
		SpellPower power = context.Power;
		int level = power.Level;
		if (!CanCast(in context)) return false;
		float radius = Definition.RadiusByLevel[level - 1];
		AttackStats hit = power.Hit.Scaled(Definition.DamageScaleByLevel[level - 1]);
		AttackDefenseTraits defense = Definition.DefenseTraits;
		if (!AreaCastLifecycle.TryCapture(this, in context, VisualRoot, random, radius, out var cast)) return false;
		var node = VisualScene.Instantiate();
		if (node is not Node3D visual || node is not IAreaSpellVisual area || !area.ConfigureArea(level, radius))
		{ node?.Free(); return false; }
		var visibility = AreaVisibility.Capture(cast.World, cast.Origin, radius);
		if (node is IAreaTargetVisual targetVisual)
			targetVisual.ConfigureTargets(System.Array.ConvertAll(System.Array.FindAll(cast.Targets,
				target => AreaSpellTargets.IsVisible(target, cast.World, cast.Origin)), target => target.GlobalPosition));
		VisualRoot.AddChild(visual); visual.GlobalPosition = cast.Origin; area.SetAreaVisibility(visibility);
		_cast.Track(visual);
		var shake = Definition.CameraShake;
		Action<Node3D, DamageResolution> afterContact = shake != null ? (_, _) => shake.EmitAt(cast.Origin) : null;
		_cast.Schedule(cast, Definition.ContactDelay, area.Front, hit, defense, Definition.PushAt(level), afterContact, Definition.Element);
		return true;
	}

	/// <summary>Invalidate later contacts and remove this focus's active visuals.</summary>
	public void Cancel()
	{
		SetPhysicsProcess(false);
		AreaCastLifecycle.ClearVisuals(_cast.Cancel());
	}
	public override void _ExitTree() => Cancel();
}
