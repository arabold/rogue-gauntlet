namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Runs the real gameplay scene and checks that its authored wiring follows the spawned player: near death
/// from health, the poison, confusion and blindness overlays from the real potions' conditions, healing curing
/// blindness, and slowness on the game clock. Fade times are zeroed so a few frames settle each effect
/// deterministically; component details are unit-tested elsewhere and the shader look is checked in the harness.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScreenEffectWiringTest
{
	private static Buff PotionBuff(string kind) =>
		TestFactories.Load<BuffedItem>($"res://scenes/items/potions/potion_{kind}_small.tres").Buff;

	[TestCase]
	public async Task GameplaySceneDrivesEffectsFromThePlayer()
	{
		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5, 20);
		ScreenEffectsLayer layer = ((Main)runner.Scene()).ScreenEffects;
		Player player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();

		HealthScreenEffectDriver nearDeathDriver = layer.HealthDrivers.Single();
		StatusScreenEffectDriver poisonDriver = layer.StatusDrivers.Single(driver => driver.Effect.Name == "Poison");
		StatusScreenEffectDriver blindnessDriver = layer.StatusDrivers.Single(driver => driver.Effect.Name == "Blindness");
		nearDeathDriver.ResponseTime = 0f;
		foreach (StatusScreenEffectDriver driver in layer.StatusDrivers)
		{
			driver.FadeInTime = 0f;
			driver.FadeOutTime = 0f;
		}

		ScreenEffect nearDeath = nearDeathDriver.Effect;
		ScreenEffect poison = poisonDriver.Effect;
		ScreenEffect blindness = blindnessDriver.Effect;
		HealthComponent health = player.HealthComponent;
		try
		{
			// Near death stays off above the threshold and ramps in below it.
			health.SetHealth(health.MaxHealth * 0.5f, health.MaxHealth);
			await runner.SimulateFrames(2, 20);
			AssertFloat(nearDeath.Intensity).IsEqual(0f);
			health.SetHealth(health.MaxHealth * 0.1f, health.MaxHealth);
			await runner.SimulateFrames(2, 20);
			AssertFloat(nearDeath.Intensity).IsGreater(0.5f);
			health.SetHealth(health.MaxHealth, health.MaxHealth);
			await runner.SimulateFrames(2, 20);
			AssertFloat(nearDeath.Intensity).IsEqual(0f);

			// Confusion shows a faint poison overlay; real poison takes it to full.
			player.ApplyBuff(PotionBuff("confusion"));
			await runner.SimulateFrames(2, 20);
			AssertFloat(poison.Intensity).IsEqualApprox(0.35f, 0.001f);
			player.ApplyBuff(PotionBuff("poison"));
			await runner.SimulateFrames(2, 20);
			AssertFloat(poison.Intensity).IsEqual(1f);

			// Blindness closes vision; healing cures it, as in Rogue.
			player.ApplyBuff(PotionBuff("blindness"));
			await runner.SimulateFrames(2, 20);
			AssertFloat(blindness.Intensity).IsEqual(1f);
			// The healing potion is a shared spell; drinking it applies the regeneration buff that cures blindness.
			TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres").PerformAction(player);
			await runner.SimulateFrames(2, 20);
			AssertFloat(blindness.Intensity).IsEqual(0f);

			// Rebinding a driver follows the new controller; binding the player again restores it.
			poisonDriver.Buffs = AutoFree(new BuffController());
			await runner.SimulateFrames(2, 20);
			AssertFloat(poison.Intensity).IsEqual(0f);
			layer.Bind(player);
			await runner.SimulateFrames(2, 20);
			AssertFloat(poison.Intensity).IsEqual(1f);

			// Slowness slows the whole game until it ends. Checked without simulating frames: the scene runner resets
			// the time scale whenever it simulates.
			player.ApplyBuff(PotionBuff("slowness"));
			AssertFloat((float)Engine.TimeScale).IsLess(1f);
			player.BuffController.ClearBuffs();
			AssertFloat((float)Engine.TimeScale).IsEqual(1f);
			await runner.SimulateFrames(2, 20);
			AssertFloat(poison.Intensity).IsEqual(0f);
		}
		finally
		{
			// Slowness changes the engine's time scale, which every later suite shares.
			if (GodotObject.IsInstanceValid(player)) player.BuffController.ClearBuffs();
			Engine.TimeScale = 1;
		}
	}
}