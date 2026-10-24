namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Runs the real gameplay scene and checks that its screen effects follow the spawned player: near death from
/// health, poison from the poisoned condition, and rebinding semantics. Fade times are zeroed so a few frames
/// settle each effect deterministically; the shader look itself is checked in the harness.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScreenEffectWiringTest
{
	[TestCase]
	public async Task GameplaySceneDrivesEffectsFromThePlayer()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(10);
		ScreenEffectsLayer layer = ((Main)runner.Scene()).ScreenEffects;
		Player player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();

		// Main binds the spawned player to every driver.
		HealthScreenEffectDriver nearDeathDriver = layer.HealthDrivers.Single();
		StatusScreenEffectDriver poisonDriver = layer.StatusDrivers.Single();
		AssertObject(layer.Blindness.Focus).IsSame(player);
		AssertObject(nearDeathDriver.Health).IsSame(player.HealthComponent);
		AssertObject(poisonDriver.Buffs).IsSame(player.BuffController);

		nearDeathDriver.ResponseTime = 0f;
		poisonDriver.FadeInTime = 0f;
		poisonDriver.FadeOutTime = 0f;
		ScreenEffect nearDeath = nearDeathDriver.Effect;
		ScreenEffect poison = poisonDriver.Effect;
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

		// The poison overlay follows the poisoned condition, not the potion.
		Buff potionPoison = GD.Load<BuffedItem>("res://scenes/items/potions/poison_potion.tres").Buff;
		player.ApplyBuff(potionPoison);
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(1f);

		// Rebinding syncs to the new controller and stops listening to the old one.
		var otherBuffs = AutoFree(new BuffController());
		poisonDriver.Buffs = otherBuffs;
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(0f);
		player.BuffController.ClearBuffs();
		player.ApplyBuff(potionPoison);
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(0f);

		layer.Bind(player);
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(1f);
		player.BuffController.ClearBuffs();
		await runner.SimulateFrames(3);
		AssertFloat(poison.Intensity).IsEqual(0f);
	}
}