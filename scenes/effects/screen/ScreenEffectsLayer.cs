using Godot;
using Godot.Collections;

/// <summary>
/// Root of <c>screen_effects.tscn</c>: the full-screen status overlays drawn above the 3D world and below the HUD.
/// The layer is the only adapter between the player and the effect drivers. The hosting scene (normally
/// <see cref="Main"/>) calls <see cref="Bind"/> when the player spawns, and the layer passes the player's
/// components to its authored drivers. Drivers therefore stay reusable and depend only on the component they observe.
/// </summary>
[GlobalClass]
public partial class ScreenEffectsLayer : CanvasLayer
{
	/// <summary>Effect whose remaining sight is centered on the bound player. Optional.</summary>
	[Export] public BlindnessScreenEffect Blindness { get; set; }

	/// <summary>Drivers that observe the bound player's <see cref="HealthComponent"/>, e.g. near death.</summary>
	[Export] public Array<HealthScreenEffectDriver> HealthDrivers { get; set; } = [];

	/// <summary>Drivers that observe the bound player's <see cref="BuffController"/>, e.g. poison.</summary>
	[Export] public Array<StatusScreenEffectDriver> StatusDrivers { get; set; } = [];

	/// <summary>
	/// Injects <paramref name="player"/> as runtime context. Binding the same player again changes nothing; binding
	/// another player replaces the previous one, and every driver immediately syncs its effect to the new player's state.
	/// </summary>
	public void Bind(Player player)
	{
		if (Blindness != null)
		{
			Blindness.Focus = player;
		}

		foreach (HealthScreenEffectDriver driver in HealthDrivers)
		{
			if (driver != null)
			{
				driver.Health = player?.HealthComponent;
			}
		}

		foreach (StatusScreenEffectDriver driver in StatusDrivers)
		{
			if (driver != null)
			{
				driver.Buffs = player?.BuffController;
			}
		}
	}
}