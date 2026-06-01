using Godot;

/// <summary>
/// Central switch for noisy runtime diagnostics that are useful while debugging but costly during combat.
/// </summary>
public static class GameDebug
{
	public static bool CombatLogsEnabled { get; set; } = false;
	public static bool AiLogsEnabled { get; set; } = false;

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
