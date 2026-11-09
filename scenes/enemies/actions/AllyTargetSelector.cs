using Godot;
using System.Collections.Generic;

/// <summary>
/// Fellow monsters within the action's range and sight, most injured first (ties by scene path, so choices stay
/// deterministic). Support spells such as regeneration and Rage use it.
/// </summary>
[Tool]
[GlobalClass]
public partial class AllyTargetSelector : MonsterTargetSelector
{
	/// <summary>The caster counts as one of its allies.</summary>
	[Export] public bool IncludeSelf { get; set; } = true;

	public override bool IsHostile => false;
	public override SpellTargetIntent Intent => SpellTargetIntent.Ally;

	public override void CollectCandidates(EnemyContext context, MonsterAction action, List<Node3D> into)
	{
		foreach (var ally in context.AlliesByInjury)
		{
			if (!IncludeSelf && ReferenceEquals(ally, context.Actor)) continue;
			into.Add(ally);
		}
	}

	/// <summary>Allies other than the caster must be in sight.</summary>
	public override bool CanSee(EnemyContext context, Node3D candidate) =>
		ReferenceEquals(candidate, context.Actor) || context.CanSee(candidate);
}
