using Godot;

[GlobalClass]
public partial class HealingBuff : PeriodicBuff
{
	[Export] public int HealthPointsPerTick = 0;

	public override void OnTick(Player player)
	{
		if (HealthPointsPerTick != 0)
		{
			player.Heal(HealthPointsPerTick);
		}
	}
}
