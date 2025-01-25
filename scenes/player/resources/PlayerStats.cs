using Godot;

[GlobalClass]
public partial class PlayerStats : Resource
{
	[Export] public int Health { get; private set; } = 100;
	[Export] public int MaxHealth { get; private set; } = 100;
	[Export] public int Xp { get; private set; } = 0;
	[Export] public int XpLevel { get; private set; } = 1;
	[Export] public int Gold { get; private set; } = 0;
	[Export] public int DungeonLevel { get; private set; } = 0;

	// Method to increase the experience points
	public void AddXp(int xp)
	{
		Xp += xp;
		SignalBus.EmitXpUpdated(Xp);
		GD.Print($"XP Updated: {Xp}");
	}

	public void LevelUp()
	{
		XpLevel++;
		SignalBus.EmitLevelUp(XpLevel);
		GD.Print($"Level Up: {XpLevel}");
	}

	// Method to increase the gold
	public void AddGold(int gold)
	{
		Gold += gold;
		SignalBus.EmitGoldUpdated(Gold);
		GD.Print($"Gold Updated: {Gold}");
	}

	public void PayGold(int gold)
	{
		Gold -= gold;
		SignalBus.EmitGoldUpdated(Gold);
		GD.Print($"Gold Updated: {Gold}");
	}

	/// <summary>
	/// Updates the player's health and emits the HealthChanged signal.
	/// This method is used internally by the Player class. Do not call
	/// this method directly from other classes. Instead use the `Heal`
	/// and `TakeDamage` methods to change the player's health.
	/// </summary>
	public void UpdateHealth(int health, int maxHealth)
	{
		Health = health;
		SignalBus.EmitHealthChanged(health, maxHealth);
		GD.Print($"Health changed: {health}");
	}
}
