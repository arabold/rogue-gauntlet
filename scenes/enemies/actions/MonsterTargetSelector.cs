using Godot;
using System.Collections.Generic;

/// <summary>
/// Chooses who a <see cref="MonsterAction"/> is aimed at. It lists candidates in preference order; the runtime takes the
/// first one the action can start against (position, conditions, usefulness).
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class MonsterTargetSelector : Resource
{
	/// <summary>Hostile actions are attacks: they obey the tactical decision's attack permission and attack slots.</summary>
	public abstract bool IsHostile { get; }
	/// <summary>The relationship a spell cast against the selected target declares to its effect.</summary>
	public abstract SpellTargetIntent Intent { get; }

	/// <summary>Append candidates in preference order. Must not allocate per call beyond <paramref name="into"/>.</summary>
	public abstract void CollectCandidates(EnemyContext context, MonsterAction action, List<Node3D> into);

	/// <summary>
	/// Sight check for one candidate, run after position and conditions so cheap rules reject first; selectors that
	/// already require sight (or need none) keep the default.
	/// </summary>
	public virtual bool CanSee(EnemyContext context, Node3D candidate) => true;

	public virtual void Validate(MonsterValidationContext context) { }
}
