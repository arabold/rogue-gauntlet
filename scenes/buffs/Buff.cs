using Godot;

[GlobalClass]
public abstract partial class Buff : Resource
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

    public virtual void OnApply(Player player)
    { }

    public virtual void OnRemove(Player player)
    { }
}
