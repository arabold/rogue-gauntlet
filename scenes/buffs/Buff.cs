using Godot;
using Godot.Collections;

/// <summary>
/// A timed or permanent effect applied to the player. A plain <see cref="Buff"/> with only a
/// <see cref="Condition"/> is a status with no behavior of its own, such as blindness or confusion:
/// the systems that care react to the condition. Subclasses add behavior with a magnitude on apply,
/// tick or removal (healing, poison, stat modifiers, game speed).
/// </summary>
[GlobalClass]
public partial class Buff : Resource
{
    [Export] public string Name;
    /// <summary>
    /// Duration of the buff in seconds. A value of 0 means the buff is permanent.
    /// </summary>
    [Export] public float Duration = 1f;
    /// <summary>
    /// Status imposed while this buff is active, e.g. poisoned. Null for buffs without one (most stat buffs).
    /// </summary>
    [Export] public StatusCondition Condition { get; set; }
    /// <summary>
    /// Conditions this buff ends when applied, e.g. healing curing blindness as in Rogue.
    /// </summary>
    [Export] public Array<StatusCondition> CuredConditions { get; set; } = [];

    public virtual void OnApply(Player player)
    { }

    public virtual void OnRemove(Player player)
    { }
}
