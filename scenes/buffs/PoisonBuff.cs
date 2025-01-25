using Godot;

[GlobalClass]
public partial class PoisonBuff : Buff
{
	[Export] public int DamagePointsPerTick = 0;

	public override void OnTick(Player player)
	{
		if (DamagePointsPerTick != 0)
		{
			player.TakeDamage(DamagePointsPerTick);
		}
	}
}
