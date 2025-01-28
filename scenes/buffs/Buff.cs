using Godot;

[GlobalClass]
public abstract partial class Buff : Resource
{
    [Export] public string Name;
    [Export] public float Duration = 1f;
    [Export] public float TicksPerSecond = 1;

    public void ApplyTo(Player player)
    {
        player.ApplyBuff(this);
    }

    /// <summary>
    /// Callback to override for custom behavior when 
    /// the buff is applied to a player.
    /// </summary>
    public virtual void OnTick(Player player)
    { }
}
