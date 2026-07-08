using Godot;

/// <summary>
/// Central switch for noisy runtime diagnostics that are useful while debugging but costly during combat.
/// </summary>
public static class GameDebug
{
	public static bool CombatLogsEnabled { get; set; } = false;
	public static bool AiLogsEnabled { get; set; } = false;

	/// <summary>
	/// See-through-wall x-ray silhouettes. Each is a play/accessibility aid, on by
	/// default and toggleable from the in-game debug menu. The silhouettes still only
	/// render where occluded and (for monsters/loot) only in discovered rooms.
	/// </summary>
	public static bool DoorXrayEnabled { get; set; } = true;
	public static bool MonsterXrayEnabled { get; set; } = true;
	public static bool LootXrayEnabled { get; set; } = true;

	public static void Combat(string message)
	{
		if (CombatLogsEnabled)
		{
			GD.Print(message);
		}
	}

	public static void Ai(string message)
	{
		if (AiLogsEnabled)
		{
			GD.Print(message);
		}
	}
}
