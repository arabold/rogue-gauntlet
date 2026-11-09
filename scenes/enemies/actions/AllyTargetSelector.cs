using Godot;
using System.Collections.Generic;

/// <summary>
/// Fellow monsters within the action's range, most injured first, then others before the caster, then creation order so
/// choices stay deterministic. Allies other than the caster must be in sight. Support spells such as regeneration and
/// Rage use it.
/// </summary>
[Tool]
[GlobalClass]
public partial class AllyTargetSelector : MonsterTargetSelector
{
	private static readonly IComparer<Node3D> ByInjury = Comparer<Node3D>.Create((left, right) =>
	{
		int byHealth = HealthFraction(left).CompareTo(HealthFraction(right));
		return byHealth != 0 ? byHealth : left.GetInstanceId().CompareTo(right.GetInstanceId());
	});

	/// <summary>The caster counts as one of its allies.</summary>
	[Export] public bool IncludeSelf { get; set; } = true;

	public override SpellTargetIntent Intent => SpellTargetIntent.Ally;

	public override void CollectCandidates(EnemyContext context, MonsterAction action, List<Node3D> into)
	{
		int start = into.Count;
		foreach (var ally in context.Allies)
		{
			if (ReferenceEquals(ally, context.Actor)) continue;
			if (action.IsInRange(context.Actor.GlobalPosition.HorizontalDistanceTo(ally.GlobalPosition))) into.Add(ally);
		}
		into.Sort(start, into.Count - start, ByInjury);
		// On equal health others come first, so the caster is appended after them where its fraction allows.
		if (!IncludeSelf || !action.IsInRange(0)) return;
		float own = HealthFraction(context.Actor);
		int index = into.Count;
		while (index > start && HealthFraction(into[index - 1]) > own) index--;
		into.Insert(index, context.Actor);
	}

	/// <summary>Allies other than the caster must be in sight.</summary>
	public override bool CanSee(EnemyContext context, Node3D candidate) =>
		ReferenceEquals(candidate, context.Actor) || context.CanSee(candidate);

	private static float HealthFraction(Node3D actor) => actor is IBuffTarget { Health: { } health } ? health.Fraction : 1;
}
