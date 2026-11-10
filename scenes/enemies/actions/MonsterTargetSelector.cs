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
	/// <summary>The relationship to the selected target; a spell cast declares it to its effect.</summary>
	public abstract SpellTargetIntent Intent { get; }
	/// <summary>Hostile actions are attacks: they obey the tactical decision's attack permission and attack slots.</summary>
	public bool IsHostile => Intent == SpellTargetIntent.Hostile;

	/// <summary>Append candidates in preference order; <paramref name="action"/> lets a selector skip candidates out of its range.</summary>
	public abstract void CollectCandidates(EnemyContext context, MonsterAction action, List<Node3D> into);

	/// <summary>
	/// Sight check for one candidate, run after position and conditions so cheap rules reject first; selectors that
	/// already require sight (or need none) keep the default.
	/// </summary>
	public virtual bool CanSee(EnemyContext context, Node3D candidate) => true;

	public virtual void Validate(MonsterValidationContext context) { }
}
