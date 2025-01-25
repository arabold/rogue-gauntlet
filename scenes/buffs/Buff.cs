using Godot;

[GlobalClass]
public abstract partial class Buff : Resource
{
    [Export] public string Name;
    [Export] public float Duration = 1f;
    [Export] public int TicksPerSecond = 1;

    public void ApplyTo(Player player)
    {
        player.ApplyBuff(this);
    }

    public virtual void OnTick(Player player)
    { }
}
