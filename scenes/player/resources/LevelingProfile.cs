using Godot;

/// <summary>
/// The experience ladder every class climbs: how much XP each level takes and the health a level adds. The ladder is
/// fitted by the balance model so a hero who clears each floor arrives on a depth at about that level; its values come
/// from <c>scripts/balance/params.py</c> through <c>simulate.py derive</c>. Attribute growth per level is the class's
/// (<see cref="StatProfile"/>).
/// </summary>
[Tool]
[GlobalClass]
public partial class LevelingProfile : Resource
{
	/// <summary>XP to advance from each level to the next, starting at level 1; the last entry reaches <see cref="LevelCap"/>.</summary>
	[Export] public int[] XpToNextLevel { get; set; } = [];
	/// <summary>Max health a level adds before Vitality scales it.</summary>
	[Export(PropertyHint.Range, "0,100,0.1")] public float HealthPerLevel { get; set; } = 7;

	public int LevelCap => XpToNextLevel.Length + 1;

	/// <summary>Total XP that reaches <paramref name="level"/> from level 1; unreachable beyond the cap.</summary>
	public int XpToReach(int level)
	{
		if (level > LevelCap) return int.MaxValue;
		int total = 0;
		for (int i = 0; i < level - 1; i++) total += XpToNextLevel[i];
		return total;
	}
}
