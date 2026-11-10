namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Spell wands through the real player: the secondary action casts the wand's bound spell at its tier for mana, once per
/// press; an unaffordable cast is rejected without spending or firing missiles; interrupting the wind-up keeps the mana
/// spent; and removing the wand ends its spell. Also the spell roll, item names, tooltips and saves.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class SpellWandTest
{
	private const string WandPath = "res://scenes/items/weapons/wand.tres";
	private const string FireNovaPath = "res://scenes/magic/spells/fire_nova.tres";
	private const string MeteorPath = "res://scenes/magic/spells/meteor.tres";
	private const string EmberWardPath = "res://scenes/magic/spells/ember_ward.tres";
	private const string FireballPath = "res://scenes/magic/spells/fireball.tres";
	private const string RuneStaffPath = "res://scenes/items/weapons/staff_rare.tres";

	[TestCase]
	public async Task SecondaryCastsTheBoundSpellOnceForItsManaCostAndNothingWhenUnaffordable()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, wand) = await EquipWand(runner, FireNovaPath);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var mana = harness.Player.ManaComponent;
		float cost = wand.BoundSpell.ManaCost(wand.SpellLevel);
		float shortfall = 0;
		mana.ManaShortfall += refused => shortfall = refused;

		// Short of the cost: rejected without spending, and no missiles instead; the HUD's cue hears why.
		mana.CurrentMana = cost - 1;
		AssertBool(harness.Player.ActionManager.TryPerformSecondary()).IsFalse();
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		AssertFloat(mana.CurrentMana).IsEqual(cost - 1);
		AssertFloat(shortfall).IsEqual(cost);

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

		AssertBool(harness.Player.SpellCaster.TryGetSpell(wand, out _)).IsFalse();
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
		AssertBool(harness.Player.SpellCaster.TryGetSpell(wand, out var combatSpell)).IsTrue();
		var spell = (AreaDamageSpell)combatSpell;
		var certainCritical = new AttackStats(8, 8, 1);

		AssertBool(spell.Cast(TestFactories.CastContext(harness.Player, new SpellPower(1, certainCritical)), new RandomNumberGenerator { Seed = 3 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);

		float scale = spell.Definition.DamageScaleByLevel[0];
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50 - 8 * scale * AttackStats.CriticalMultiplier);
	}

	[TestCase]
	public void DropsRollTheirSpellFromThePoolAndShowItInTheName()
	{
		var definition = TestFactories.Load<MagicStaff>(WandPath);
		var policy = TestFactories.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
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
	public async Task TooltipsQuoteTheSpellDamageTheWeaponDealsOnceWielded()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, wand) = await EquipWand(runner, FireballPath);
		var player = harness.Player;
		var fireballDef = TestFactories.Load<TargetedSpellDefinition>("res://scenes/magic/fire/fireball_combat.tres");
		AssertArray(TooltipLines(wand, player))
			.Contains("Fireball (Level 1)", "10 mana per cast", SpellLevels.Damage(player.Stats.ResolveAttack(wand).Scaled(fireballDef.DamageScaleByLevel[0])) + " per hit");

		// A staff in the backpack quotes the damage it deals once it replaces the wand.
		var staff = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>(RuneStaffPath));
		AssertBool(player.Inventory.TryAdd(staff).Success).IsTrue();
		var quoted = TooltipLines(staff, player);
		AssertArray(quoted).Contains("Chain Lightning (Level 4)", "15 mana per cast");
		player.Inventory.Equip(player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, staff)), EquipmentSlot.WeaponHand);
		AssertBool(player.SpellCaster.IsBoundTo(staff)).IsTrue();
		AssertArray(TooltipLines(staff, player)).IsEqual(quoted);
		var chainDef = TestFactories.Load<TargetedSpellDefinition>("res://scenes/magic/air/chain_lightning_combat.tres");
		AssertArray(quoted).Contains(SpellLevels.Damage(player.Stats.ResolveAttack(staff).Scaled(chainDef.DamageScaleByLevel[3])) + " per hit");

		// No damage without a viewer, or for a weapon whose stats are unidentified.
		var hidden = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>(RuneStaffPath));
		hidden.AttributesKnown = false;
		AssertBool(TooltipLines(wand, null).Any(line => line.EndsWith(" per hit"))).IsFalse();
		AssertBool(TooltipLines(hidden, player).Any(line => line.EndsWith(" per hit"))).IsFalse();
	}

	[TestCase]
	public async Task AnActiveWardKeepsItsRemainingCapacityThroughASave()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, _) = await EquipWand(runner, EmberWardPath);
		var player = harness.Player;
		var attacker = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var wand = (MagicStaff)player.Inventory.EquippedWeapon;
		AssertBool(player.SpellCaster.TryGetSpell(wand, out var boundSpell)).IsTrue();
		var ward = (MagicGuardSpell)boundSpell;
		AssertBool(ward.Cast(TestFactories.CastContext(player, TestFactories.SpellPower(1)), new RandomNumberGenerator { Seed = 1 })).IsTrue();
		float full = ward.RemainingCapacity;
		var contact = new DamageContext { OwnerNode = attacker, SourceNode = attacker, Faction = DamageSourceFlags.Enemy,
			Amount = 4, IncomingDirection = Vector3.Left, DeliveryKind = DamageDeliveryKind.DirectMelee };
		player.ResolveDamage(in contact);
		float remaining = ward.RemainingCapacity;
		AssertFloat(remaining).IsGreater(0).IsLess(full);

		var saved = JsonSerializer.Deserialize<PlayerSaveData>(JsonSerializer.Serialize(PlayerSaveCodec.Capture(player, false)));
		PlayerSaveCodec.Apply(player, saved);
		var restoredWand = (MagicStaff)player.Inventory.EquippedWeapon;
		AssertBool(player.SpellCaster.TryGetSpell(restoredWand, out var restoredSpell)).IsTrue();
		AssertFloat(((MagicGuardSpell)restoredSpell).RemainingCapacity).IsEqual(remaining);

		// A ward with no spell weapon to rise on is rejected before the player changes.
		saved.Inventory.EquippedItems.RemoveAll(entry => entry.Slot == nameof(EquipmentSlot.WeaponHand));
		bool rejected = false;
		try { PlayerSaveCodec.Apply(player, saved); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertBool(player.SpellCaster.TryGetSpell(restoredWand, out var afterFailSpell)).IsTrue();
		AssertFloat(((MagicGuardSpell)afterFailSpell).RemainingCapacity).IsEqual(remaining);
	}

	[TestCase]
	public async Task TheHarnessSpellCommandRebindsTheEquippedStaff()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		AssertBool(harness.ExecuteCommand("equip", "res://scenes/items/weapons/staff_common.tres")).IsTrue();
		var iceLance = harness.Spells.Spells.Spells.Single(spell => spell.DisplayName == "Ice Lance");
		AssertBool(harness.ExecuteCommand("spell", iceLance.ResourcePath)).IsTrue();

		var equipped = (MagicStaff)harness.Player.Inventory.EquippedWeapon;
		AssertString(equipped.BoundSpell.DisplayName).IsEqual("Ice Lance");
		AssertBool(harness.Player.SpellCaster.TryGetSpell(equipped, out var lanceSpell)).IsTrue();
		AssertString(lanceSpell.DisplayName).IsEqual("Ice Lance");
	}

	[TestCase]
	public void SavedWandsRestoreTheirSpellAndRejectAnInvalidOne()
	{
		var wand = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>(WandPath));
		wand.BoundSpell = TestFactories.Load<SpellDefinition>(MeteorPath);
		var data = ItemSaveCodec.Capture(wand, 1);

		var restored = (MagicStaff)ItemSaveCodec.Restore(data);
		AssertString(restored.BoundSpell.ResourcePath).IsEqual(MeteorPath);

		data.SpellPath = "res://scenes/magic/spells/missing.tres";
		bool rejected = false;
		try { ItemSaveCodec.Restore(data); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
	}

	[TestCase]
	public async Task EitherHandWandCastsFromOffhandSlotWithL()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		player.Stats.ManaRegenRate = 0;
		player.ManaComponent.CurrentMana = 50;

		var sword = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword_common.tres"));
		var wand = Wand(TestFactories.Load<SpellDefinition>(FireNovaPath));
		EquipBoth(player, sword, wand);
		AssertObject(player.Inventory.EquippedWeapon).IsEqual(sword);
		AssertObject(player.Inventory.EquippedOffhandWeapon).IsEqual(wand);

		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		float cost = wand.BoundSpell.ManaCost(wand.SpellLevel);

		AssertBool(player.ActionManager.TryPerformOffhand()).IsTrue();
		AssertFloat(player.ManaComponent.CurrentMana).IsEqual(50 - cost);
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.AttackController.IsAttacking);
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);

		AssertString(player.AttackController.LastResult).IsEqual("Cast");
	}

	[TestCase]
	public async Task ReleasedCastHandoffBetweenHandsKeepsEachWandsCooldown()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		var actions = player.ActionManager;
		player.Stats.ManaRegenRate = 0;
		player.ManaComponent.CurrentMana = 100;
		EquipBoth(player, Wand(CooldownSpell(FireballPath, 1.5f)), Wand(CooldownSpell(FireNovaPath, 1.5f)));

		// The off-hand wand (L) casts; the main-hand wand (K) cannot interrupt its wind-up.
		AssertBool(actions.TryPerformOffhand()).IsTrue();
		AssertBool(actions.OffhandActionSlot.IsPerformingAction).IsTrue();
		AssertBool(actions.SecondaryTechniqueSlot.IsPerformingAction).IsFalse();
		AssertBool(actions.TryPerformSecondary()).IsFalse();

		// Once L's spell is released, K takes over while L cools down on its own clock.
		await TestFactories.AwaitPhysicsUntil(runner, () => player.AttackController.Phase == ActorActionPhase.Recovery);
		AssertBool(actions.SecondaryTechniqueSlot.IsOnCooldown).IsFalse();
		AssertBool(actions.TryPerformSecondary()).IsTrue();
		AssertBool(actions.OffhandActionSlot.IsOnCooldown).IsTrue();
		AssertFloat((float)actions.OffhandActionSlot.RemainingTime).IsGreater(0);
		AssertBool(actions.SecondaryTechniqueSlot.IsPerformingAction).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => actions.SecondaryTechniqueSlot.IsOnCooldown);
		AssertBool(actions.OffhandActionSlot.IsOnCooldown).IsTrue();
	}

	[TestCase]
	public async Task WandCooldownPersistsThroughSaveAndLoad()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		player.Stats.ManaRegenRate = 0;
		player.ManaComponent.CurrentMana = 100;

		var wand = Wand(CooldownSpell(FireballPath, 2));
		player.Inventory.TryAdd(wand);
		player.Inventory.Equip(player.Inventory.Items[^1], EquipmentSlot.WeaponHand);

		AssertBool(player.ActionManager.TryPerformSecondary()).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => player.ActionManager.SecondaryTechniqueSlot.IsOnCooldown);
		AssertBool(player.ActionManager.IsWandOnCooldown(wand.InstanceId)).IsTrue();

		var saved = JsonSerializer.Deserialize<PlayerSaveData>(JsonSerializer.Serialize(PlayerSaveCodec.Capture(player, false)));
		PlayerSaveCodec.Apply(player, saved);

		AssertBool(player.ActionManager.IsWandOnCooldown(wand.InstanceId)).IsTrue();
		AssertBool(player.ActionManager.TryPerformSecondary()).IsFalse();
	}

	/// <summary>
	/// A private copy of a spell with a cooldown: authored spells have none, and the cached definition is shared by every
	/// suite. Loading it uncached keeps its path, so saves still resolve it.
	/// </summary>
	private static SpellDefinition CooldownSpell(string path, float cooldown)
	{
		var spell = TestFactories.Load<SpellDefinition>(path, ResourceLoader.CacheMode.Ignore);
		spell.Cooldown = cooldown;
		return spell;
	}

	private static MagicStaff Wand(SpellDefinition spell)
	{
		var wand = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>(WandPath));
		wand.BoundSpell = spell;
		return wand;
	}

	/// <summary>Equips <paramref name="main"/> in the weapon hand and <paramref name="offhand"/> in the shield hand.</summary>
	private static void EquipBoth(Player player, Item main, Item offhand)
	{
		player.Inventory.TryAdd(main);
		player.Inventory.TryAdd(offhand);
		player.Inventory.Equip(player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, main)), EquipmentSlot.WeaponHand);
		player.Inventory.Equip(player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, offhand)), EquipmentSlot.ShieldHand);
		AssertBool(player.SpellCaster.IsBoundTo(offhand as MagicStaff)).IsTrue();
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
		var wand = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>(WandPath));
		wand.BoundSpell = TestFactories.Load<SpellDefinition>(spellPath);
		AssertBool(player.Inventory.TryAdd(wand).Success).IsTrue();
		player.Inventory.Equip(player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, wand)), EquipmentSlot.WeaponHand);
		AssertBool(player.SpellCaster.IsBoundTo(wand)).IsTrue();
		return (harness, wand);
	}

	private static List<string> TooltipLines(Item item, Player viewer)
	{
		var view = new ItemDetailsView();
		view.SetItem(item, viewer);
		var lines = view.GetChildren().OfType<Label>().Select(label => label.Text).ToList();
		view.Free();
		return lines;
	}

	private static async Task<EnemyBase> SpawnTarget(ISceneRunner runner, Vector3 position)
	{
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), position, "Target");
		await runner.SimulateFrames(2, 20);
		TestFactories.PrepareCombatMonster(target);
		return target;
	}
}