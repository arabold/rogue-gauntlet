using Godot;
using System.Collections.Generic;

/// <summary>The monster itself: self heals, self buffs. Needs no hostile target and no sight.</summary>
[Tool]
[GlobalClass]
public partial class SelfTargetSelector : MonsterTargetSelector
{
	public override SpellTargetIntent Intent => SpellTargetIntent.Self;

	public override void CollectCandidates(EnemyContext context, MonsterAction action, List<Node3D> into) => into.Add(context.Actor);
}
