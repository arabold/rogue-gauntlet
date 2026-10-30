namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Spell wands through the real player: the secondary action casts the wand's bound spell at its tier for mana, once per
/// press; an unaffordable cast is rejected without spending or firing missiles; interrupting the wind-up keeps the mana
/// spent; and removing the wand ends its spell. Also the spell roll, item names and saves.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class SpellWandTest
{
	private const string WandPath = "res://scenes/items/weapons/wand.tres";
	private const string FireNovaPath = "res://scenes/magic/spells/fire_nova.tres";
	private const string MeteorPath = "res://scenes/magic/spells/meteor.tres";

	[TestCase]
	public async Task SecondaryCastsTheBoundSpellOnceForItsManaCostAndNothingWhenUnaffordable()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, wand) = await EquipWand(runner, FireNovaPath);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var mana = harness.Player.ManaComponent;
		float cost = wand.BoundSpell.ManaCost(wand.SpellLevel);

		// Short of the cost: rejected without spending, and no missiles instead.
		mana.CurrentMana = cost - 1;
		AssertBool(harness.Player.ActionManager.TryPerformSecondary()).IsFalse();
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		AssertFloat(mana.CurrentMana).IsEqual(cost - 1);

		mana.CurrentMana = 50;
		AssertBool(harness.Player.ActionManager.TryPerformSecondary()).IsTrue();
		AssertFloat(mana.CurrentMana).IsEqual(50 - cost);
		AssertBool(harness.Player.ActionManager.TryPerformSecondary()).IsFalse();
		await TestFactories.AwaitPhysicsUntil(runner, () => !harness.Player.AttackController.IsAttacking);
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);

		AssertString(harness.Player.AttackController.LastResult).IsEqual("Cast");
		AssertFloat(mana.CurrentMana).IsEqual(50 - cost);
	}

	[TestCase]
	public async Task RemovingTheWandDuringTheWindUpCancelsTheCastAndKeepsTheManaSpent()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, wand) = await EquipWand(runner, FireNovaPath);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var mana = harness.Player.ManaComponent;
		mana.CurrentMana = 50;

		AssertBool(harness.Player.ActionManager.TryPerformSecondary()).IsTrue();
		harness.Player.Inventory.Unequip(EquipmentSlot.WeaponHand);
		await TestFactories.AwaitPhysicsSeconds(runner, 1.5f);

		AssertObject(harness.Player.SpellCaster.Current).IsNull();
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(mana.CurrentMana).IsEqual(50 - wand.BoundSpell.ManaCost(wand.SpellLevel));
	}

	[TestCase]
	public async Task SpellContactsScaleTheCapturedHitAndRollCriticals()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, wand) = await EquipWand(runner, FireNovaPath);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var spell = (AreaDamageSpell)harness.Player.SpellCaster.Current;
		var certainCritical = new AttackStats(8, 8, 1);

		AssertBool(spell.Cast(harness.Player, new SpellPower(1, certainCritical), new RandomNumberGenerator { Seed = 3 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);

		float scale = spell.Definition.DamageScaleByLevel[0];
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50 - 8 * scale * AttackStats.CriticalMultiplier);
	}

	[TestCase]
	public void DropsRollTheirSpellFromThePoolAndShowItInTheName()
	{
		var definition = GD.Load<MagicStaff>(WandPath);
		var policy = GD.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
		var rolled = new HashSet<SpellDefinition>();
		for (ulong seed = 1; seed <= 30; seed++)
		{
			var wand = (MagicStaff)ItemFactory.Create(definition, policy, 2, new RandomNumberGenerator { Seed = seed });
			AssertBool(definition.SpellPool.Spells.Contains(wand.BoundSpell)).IsTrue();
			rolled.Add(wand.BoundSpell);
			// The same seed always rolls the same spell.
			if (seed <= 3) AssertObject(((MagicStaff)ItemFactory.Create(definition, policy, 2, new RandomNumberGenerator { Seed = seed })).BoundSpell).IsSame(wand.BoundSpell);
		}
		AssertInt(rolled.Count).IsGreater(definition.SpellPool.Spells.Length / 2);

		var fixedWand = (MagicStaff)ItemFactory.Create(definition);
		AssertString(fixedWand.BoundSpell.ResourcePath).IsEqual("res://scenes/magic/spells/fireball.tres");
		AssertString(ItemIdentity.ComposeDisplayName(fixedWand, null)).IsEqual("Wand of Fireball");
		fixedWand.Quality = ItemQuality.Magic;
		fixedWand.AttributesKnown = false;
		AssertString(ItemIdentity.ComposeDisplayName(fixedWand, null)).IsEqual("Magic Wand of Fireball");
	}

	[TestCase]
	public async Task TheHarnessSpellSelectorRebindsTheEquippedStaff()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		int staff = Array.FindIndex(harness.Weapons, weapon => weapon is MagicStaff { IsTwoHanded: true });
		harness.EquipPreview(staff);
		var selector = harness.GetNode<OptionButton>("%Controls/Spell");
		int iceLance = Array.FindIndex(harness.SpellPool.Spells, spell => spell.DisplayName == "Ice Lance");
		selector.Select(iceLance);
		selector.EmitSignal(OptionButton.SignalName.ItemSelected, iceLance);

		var equipped = (MagicStaff)harness.Player.Inventory.EquippedWeapon;
		AssertString(equipped.BoundSpell.DisplayName).IsEqual("Ice Lance");
		AssertBool(harness.Player.SpellCaster.IsBoundTo(equipped)).IsTrue();
		AssertString(harness.Player.SpellCaster.Current.DisplayName).IsEqual("Ice Lance");
	}

	[TestCase]
	public void SavedWandsRestoreTheirSpellAndRejectAnInvalidOne()
	{
		var wand = (MagicStaff)ItemFactory.Create(GD.Load<Item>(WandPath));
		wand.BoundSpell = GD.Load<SpellDefinition>(MeteorPath);
		var data = ItemSaveCodec.Capture(wand, 1);

		var restored = (MagicStaff)ItemSaveCodec.Restore(data);
		AssertString(restored.BoundSpell.ResourcePath).IsEqual(MeteorPath);

		data.SpellPath = "res://scenes/magic/spells/missing.tres";
		bool rejected = false;
		try { ItemSaveCodec.Restore(data); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
	}

	/// <summary>Equips a fresh tier-1 wand bound to <paramref name="spellPath"/> on a harness hero held at the origin.</summary>
	private static async Task<(CombatHarness, MagicStaff)> EquipWand(ISceneRunner runner, string spellPath)
	{
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		player.GlobalPosition = Vector3.Zero;
		// No regeneration, so mana assertions read only what casting spent.
		player.Stats.ManaRegenRate = 0;
		var wand = (MagicStaff)ItemFactory.Create(GD.Load<Item>(WandPath));
		wand.BoundSpell = GD.Load<SpellDefinition>(spellPath);
		AssertBool(player.Inventory.TryAdd(wand).Success).IsTrue();
		player.Inventory.Equip(player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, wand)), EquipmentSlot.WeaponHand);
		AssertBool(player.SpellCaster.IsBoundTo(wand)).IsTrue();
		return (harness, wand);
	}

	private static async Task<EnemyBase> SpawnTarget(ISceneRunner runner, Vector3 position)
	{
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), position, "Target");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(target);
		return target;
	}
}