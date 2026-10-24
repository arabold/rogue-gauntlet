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
		GD.Load<BuffedItem>($"res://scenes/items/potions/potion_{kind}_small.tres").Buff;

	[TestCase]
	public async Task GameplaySceneDrivesEffectsFromThePlayer()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(10);
		ScreenEffectsLayer layer = ((Main)runner.Scene()).ScreenEffects;
		Player player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();

		// Main binds the spawned player to every driver.
		HealthScreenEffectDriver nearDeathDriver = layer.HealthDrivers.Single();
		StatusScreenEffectDriver poisonDriver = layer.StatusDrivers.Single(driver => driver.Effect.Name == "Poison");
		StatusScreenEffectDriver blindnessDriver = layer.StatusDrivers.Single(driver => driver.Effect.Name == "Blindness");
		AssertObject(layer.Blindness.Focus).IsSame(player);
		AssertObject(nearDeathDriver.Health).IsSame(player.HealthComponent);
		AssertBool(layer.StatusDrivers.All(driver => driver.Buffs == player.BuffController)).IsTrue();
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

		// Near death stays off above the threshold and ramps in below it.
		health.SetHealth(health.MaxHealth * 0.5f, health.MaxHealth);
		await runner.SimulateFrames(3);
		AssertFloat(nearDeath.Intensity).IsEqual(0f);
		health.SetHealth(health.MaxHealth * 0.1f, health.MaxHealth);
		await runner.SimulateFrames(3);
		AssertFloat(nearDeath.Intensity).IsGreater(0.5f);
		health.SetHealth(health.MaxHealth, health.MaxHealth);
		await runner.SimulateFrames(3);
		AssertFloat(nearDeath.Intensity).IsEqual(0f);

		// Confusion shows a faint poison overlay; real poison takes it to full.
		player.ApplyBuff(PotionBuff("confusion"));
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqualApprox(0.35f, 0.001f);
		player.ApplyBuff(PotionBuff("poison"));
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(1f);

		// Blindness closes vision; healing cures it, as in Rogue.
		player.ApplyBuff(PotionBuff("blindness"));
		await runner.SimulateFrames(3);
		AssertFloat(blindness.Intensity).IsEqual(1f);
		player.ApplyBuff(PotionBuff("healing"));
		await runner.SimulateFrames(3);
		AssertFloat(blindness.Intensity).IsEqual(0f);

		// Slowness slows the whole game until it ends.
		player.ApplyBuff(PotionBuff("slowness"));
		AssertFloat((float)Engine.TimeScale).IsLess(1f);

		// Rebinding a driver follows the new controller; binding the player again restores it.
		poisonDriver.Buffs = AutoFree(new BuffController());
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(0f);
		layer.Bind(player);
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(1f);

		player.BuffController.ClearBuffs();
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(0f);
		AssertFloat((float)Engine.TimeScale).IsEqual(1f);
	}
}