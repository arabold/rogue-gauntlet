using Godot;
using System.Collections.Generic;

/// <summary>The monster's current hostile target, the player it is fighting.</summary>
[Tool]
[GlobalClass]
public partial class HostileTargetSelector : MonsterTargetSelector
{
	/// <summary>Require an unobstructed line of sight, so nothing is aimed through walls.</summary>
	[Export] public bool RequireLineOfSight { get; set; } = true;

	public override SpellTargetIntent Intent => SpellTargetIntent.Hostile;

	public override void CollectCandidates(EnemyContext context, MonsterAction action, List<Node3D> into)
	{
		var target = context.Target;
		if (!GodotObject.IsInstanceValid(target) || (RequireLineOfSight && !context.HasLineOfSightToTarget())) return;
		into.Add(target);
	}
}
