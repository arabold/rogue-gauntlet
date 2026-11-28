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
		await TestFactories.AwaitPhysicsFrames(runner, 5);
		ScreenEffectsLayer layer = ((Main)runner.Scene()).ScreenEffects;
		Player player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();

		HealthScreenEffectDriver nearDeathDriver = layer.HealthDrivers.Single();
		StatusScreenEffectDriver poisonDriver = layer.StatusDrivers.Single(driver => driver.Effect.Name == "Poison");
		StatusScreenEffectDriver blindnessDriver = layer.StatusDrivers.Single(driver => driver.Effect.Name == "Blindness");
		CurseScreenEffectDriver curseDriver = layer.CurseDrivers.Single();
		nearDeathDriver.ResponseTime = 0f;
		foreach (StatusScreenEffectDriver driver in layer.StatusDrivers)
		{
			driver.FadeInTime = 0f;
			driver.FadeOutTime = 0f;
		}
		curseDriver.FadeInTime = 0f;
		curseDriver.HoldTime = 0.04f;
		curseDriver.FadeOutTime = 0.04f;
		layer.DarknessDriver.FadeOutTime = 0f;

		ScreenEffect nearDeath = nearDeathDriver.Effect;
		ScreenEffect poison = poisonDriver.Effect;
		ScreenEffect blindness = blindnessDriver.Effect;
		ScreenEffect curse = curseDriver.Effect;
		HealthComponent health = player.HealthComponent;
		try
		{
			// Near death stays off above the threshold and ramps in below it.
			health.SetHealth(health.MaxHealth * 0.5f, health.MaxHealth);
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(nearDeath.Intensity).IsEqual(0f);
			health.SetHealth(health.MaxHealth * 0.1f, health.MaxHealth);
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(nearDeath.Intensity).IsGreater(0.5f);
			health.SetHealth(health.MaxHealth, health.MaxHealth);
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(nearDeath.Intensity).IsEqual(0f);

			// Confusion shows a faint poison overlay; real poison takes it to full.
			player.ApplyBuff(PotionBuff("confusion"));
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(poison.Intensity).IsEqualApprox(0.35f, 0.001f);
			player.ApplyBuff(PotionBuff("poison"));
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(poison.Intensity).IsEqual(1f);

			// Blindness closes vision; healing cures it, as in Rogue, and poison, leaving confusion's faint overlay.
			player.ApplyBuff(PotionBuff("blindness"));
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(blindness.Intensity).IsEqual(1f);
			var lighting = runner.Scene().GetNode<Level>("Level").Lighting;
			lighting.IsDark = true;
			AssertFloat(layer.Darkness.Intensity).IsEqual(1f);
			var lamp = ItemFactory.Create(TestFactories.Load<PortableLightItem>("res://scenes/items/tools/lamp/tool_lamp_oil.tres"));
			AssertBool(player.Inventory.TryAdd(lamp).Success).IsTrue();
			InventoryItemSlot lampSlot = player.Inventory.Items.Single(entry => entry.Item == lamp);
			AssertBool(player.InventoryController.TryEquip(lampSlot, EquipmentSlot.ShieldHand).Success).IsTrue();
			AssertBool(layer.Darkness.Visible).IsFalse();
			AssertFloat(blindness.Intensity).IsEqual(1f);
			AssertBool(player.InventoryController.TryUnequip(lampSlot).Success).IsTrue();
			AssertFloat(layer.Darkness.Intensity).IsEqual(1f);
			// The healing potion is a shared spell; drinking it applies the regeneration buff that cures blindness.
			TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres").PerformAction(player);
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(blindness.Intensity).IsEqual(0f);
			AssertFloat(layer.Darkness.Intensity).IsEqual(1f);
			AssertFloat(poison.Intensity).IsEqualApprox(0.35f, 0.001f);
			lighting.IsDark = false;

			// Rebinding a driver follows the new controller; binding the player again restores it.
			poisonDriver.Buffs = AutoFree(new BuffController());
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(poison.Intensity).IsEqual(0f);
			layer.Bind(player);
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(poison.Intensity).IsEqualApprox(0.35f, 0.001f);

			// Equipping an uncursed item leaves the curse flash off; equipping a cursed item pulses and fades out.
			var policy = TestFactories.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
			var ringTemplate = TestFactories.Load<Item>("res://scenes/items/jewelry/ring/ring_strength_1.tres");
			var cleanRing = (EquipableItem)ItemFactory.Create(ringTemplate);
			AssertBool(cleanRing.IsCursed).IsFalse();
			AssertBool(player.Inventory.TryAdd(cleanRing).Success).IsTrue();
			InventoryItemSlot cleanSlot = player.Inventory.Items.Last();
			AssertBool(player.InventoryController.TryEquip(cleanSlot, EquipmentSlot.LeftRing).Success).IsTrue();
			await TestFactories.AwaitPhysicsFrames(runner, 2);
			AssertFloat(curse.Intensity).IsEqual(0f);

			var cursedRing = (EquipableItem)ItemFactory.Create(ringTemplate);
			LootRoller.ApplyRoll(cursedRing, 12, new RandomNumberGenerator { Seed = 1 }, policy, ItemQuality.Normal, cursed: true);
			AssertBool(cursedRing.IsCursed).IsTrue();
			AssertBool(player.Inventory.TryAdd(cursedRing).Success).IsTrue();
			InventoryItemSlot cursedSlot = player.Inventory.Items.Last();
			AssertBool(player.InventoryController.TryEquip(cursedSlot, EquipmentSlot.RightRing).Success).IsTrue();
			AssertFloat(curse.Intensity).IsEqual(1f);
			AssertBool(curse.Surface.Visible).IsTrue();
			await TestFactories.AwaitPhysicsSeconds(runner, .16f);
			AssertFloat(curse.Intensity).IsEqual(0f);
			AssertBool(curse.Surface.Visible).IsFalse();

			// Slowness slows the whole game until it ends. Checked without simulating frames: the scene runner resets
			// the time scale whenever it simulates.
			player.ApplyBuff(PotionBuff("slowness"));
			AssertFloat((float)Engine.TimeScale).IsLess(1f);
			player.BuffController.ClearBuffs();
			AssertFloat((float)Engine.TimeScale).IsEqual(1f);
			await TestFactories.AwaitPhysicsFrames(runner, 2);
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