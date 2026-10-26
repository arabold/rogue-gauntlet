using Godot;
using Godot.Collections;

/// <summary>
/// A timed or permanent effect applied to the player. A plain <see cref="Buff"/> with only a
/// <see cref="Condition"/> is a status with no behavior of its own, such as blindness or confusion:
/// the systems that care react to the condition. Subclasses add behavior with a magnitude on apply,
/// tick or removal (healing, poison, stat modifiers, game speed).
/// </summary>
[Tool]
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

    /// <summary>
    /// Starts the effect for one <paramref name="activation"/>. A buff is a shared definition that can be active
    /// several times at once, so register reversible effects under the activation, never under the buff itself.
    /// </summary>
    public virtual void OnApply(ActiveBuff activation)
    { }

    /// <summary>Reverses exactly what <see cref="OnApply"/> registered for <paramref name="activation"/>.</summary>
    public virtual void OnRemove(ActiveBuff activation)
    { }
}
