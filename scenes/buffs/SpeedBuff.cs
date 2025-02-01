using Godot;

[GlobalClass]
public partial class SpeedBuff : Buff
{
	[Export] public float SpeedModifier = 1f;

	public override void OnApply(Player player)
	{
		if (SpeedModifier != 0)
		{
			player.Stats.SpeedModifier *= SpeedModifier;
		}
	}

	public override void OnRemove(Player player)
	{
		if (SpeedModifier != 0)
		{
			player.Stats.SpeedModifier /= SpeedModifier;
		}
	}
}
