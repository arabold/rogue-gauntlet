using Godot;

[GlobalClass]
public partial class PlayerStats : ObservableResource
{
	// Player's stats
	public float Health { get; set => SetValue(ref field, value); } = 100;
	public int Xp { get; set => SetValue(ref field, value); } = 0;
	public int XpLevel { get; set => SetValue(ref field, value); } = 1;
	public int Gold { get; set => SetValue(ref field, value); } = 0;
	public int DungeonDepth { get; set => SetValue(ref field, value); } = 0;

	// Base stats (without any item or buff modifiers applied)
	public float BaseSpeed { get; set => SetValue(ref field, value); } = 10;
	public float BaseMaxHealth { get; set => SetValue(ref field, value); } = 100;

	// Attack and defense stats (without any modifiers applied)
	public float BaseAccuracy { get; set => SetValue(ref field, value); } = 1.0f;
	public float BaseMinDamage { get; set => SetValue(ref field, value); } = 0;
	public float BaseMaxDamage { get; set => SetValue(ref field, value); } = 2;
	public float BaseCritChance { get; set => SetValue(ref field, value); } = 0;
	public float BaseArmor { get; set => SetValue(ref field, value); } = 0;
	public float BaseEvasion { get; set => SetValue(ref field, value); } = 0;

	// Multipliers for the player's stats
	public float SpeedModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float HealthModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float XpModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float GoldModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float DamageModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float CritModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float ArmorModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float AccuracyModifier { get; set => SetValue(ref field, value); } = 1.0f;

	// Player's current stats (including items and buffs)
	public float Speed => BaseSpeed * SpeedModifier;
	public float MaxHealth => BaseMaxHealth * HealthModifier;

	// Attack Stats
	public float Accuracy => BaseAccuracy * AccuracyModifier;
	public float MinDamage => BaseMinDamage * DamageModifier;
	public float MaxDamage => BaseMaxDamage * DamageModifier;
	public float CritChance => BaseCritChance * CritModifier;

	// Defense Stats
	public float Armor => BaseArmor * ArmorModifier;
	public float Evasion => BaseEvasion;

	// Method to increase the experience points
	public void AddXp(int xp)
	{
		if (xp <= 0)
		{
			return;
		}
		Xp += xp;
		GD.Print($"XP Updated: {Xp}");
		EmitChanged();
	}

	public void LevelUp()
	{
		XpLevel++;
		GD.Print($"Level Up: {XpLevel}");
		EmitChanged();
	}

	// Method to increase the gold
	public void AddGold(int gold)
	{
		if (gold <= 0)
		{
			return;
		}
		Gold += gold;
		GD.Print($"Gold Updated: {Gold}");
		EmitChanged();
	}

	public void PayGold(int gold)
	{
		if (gold <= 0)
		{
			return;
		}
		Gold -= gold;
		GD.Print($"Gold Updated: {Gold}");
		EmitChanged();
	}
}
