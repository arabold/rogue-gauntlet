using Godot;

[GlobalClass]
public partial class ConsumableItem : BuffedItem, IPlayerAction
{
	[Export] public string AnimationId { get; protected set => SetValue(ref field, value); } = "drink_potion";
	[Export] public float Delay { get; protected set => SetValue(ref field, value); } = 0f;
	[Export] public float PerformDuration { get; protected set => SetValue(ref field, value); } = 0.5f;
	[Export] public float CooldownDuration { get; protected set => SetValue(ref field, value); } = 0f;

	public virtual void PerformAction(Player player)
	{
		GD.Print($"Consuming {Name}");
		ApplyBuff(player);
	}
}
