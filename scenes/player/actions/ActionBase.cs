using Godot;

public abstract partial class ActionBase : Node, IAction
{
	public abstract string Id { get; }
	public abstract float PerformDuration { get; }
	public abstract float CooldownDuration { get; }

	public virtual void Execute(Player player)
	{
	}

	public virtual void ApplyEffect(Player player)
	{
	}

	public virtual void Reset()
	{
	}
}
