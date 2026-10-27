namespace RogueGauntlet.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Uses registered physics colliders to verify contact independently of animation and VFX.</summary>
[TestSuite]
[RequireGodotRuntime]
public class MeleeContactTest
{
	private static async Task<(ISceneRunner Runner, CombatHarness Harness)> Load()
	{
		var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		return (runner, harness);
	}

	private static AttackController Start(CombatHarness harness, string profile = "slash_1h", uint mask = 8, bool effects = false,
		Action<AttackDefinition> configure = null)
	{
		var controller = harness.Player.AttackController;
		controller.CancelAttack();
		controller.EffectsEnabled = effects;
		var definition = new AttackDefinition { AttackStyle = "sword.1h.slash",
			MeleeContact = GD.Load<MeleeAttackProfile>($"res://scenes/attacks/melee_profiles/{profile}.tres") };
		configure?.Invoke(definition);
		AssertBool(controller.TryPrepareAttack(definition, "", 1, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, 10, 10, 0, mask)).IsTrue();
		controller.SetPhysicsProcess(false);
		return controller;
	}

	private static HealthComponent Health(CombatHarness harness) => harness.PracticeTarget.GetNode<HealthComponent>("HealthComponent");

	[TestCase]
	public async Task BodyContactIsTimedAndCanBeEnteredAfterTheWindowOpens()
	{
		var (runner, harness) = await Load();
		harness.PracticeTarget.Position = new Vector3(0, 0, 1.3f); // touching the rear of the hero's physical body
		await runner.SimulateFrames(2);
		var controller = Start(harness);
		controller._PhysicsProcess(.05);
		AssertInt(controller.ContactCount).IsEqual(0);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		harness.PracticeTarget.Position = new Vector3(0, 0, 5);
		await runner.SimulateFrames(2);
		controller._PhysicsProcess(.35); // window opens while the target is absent
		AssertInt(controller.ContactCount).IsEqual(0);
		harness.PracticeTarget.Position = new Vector3(0, 0, 1.3f);
		await runner.SimulateFrames(2);
		controller._PhysicsProcess(.1);
		AssertInt(controller.ContactCount).IsEqual(1);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		controller._PhysicsProcess(.1);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		// The body envelope obeys the same wall test as the forward swing.
		Health(harness).SetHealth(1000, 1000);
		var wall = new StaticBody3D { Position = new Vector3(0, 1, .65f), CollisionLayer = 2 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3, 2, .1f) } });
		runner.Scene().AddChild(wall);
		await runner.SimulateFrames(2);
		controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Obstructed");
	}

	[TestCase]
	public async Task MeleeVolumesCoverTheirFullWidthAtTheAttackersCenter()
	{
		var (runner, harness) = await Load();
		var shape = new CapsuleShape3D { Radius = .08f, Height = .2f };
		harness.PracticeTarget.GetNode<CollisionShape3D>("CollisionShape3D").Shape = shape;
		harness.PracticeTarget.GetNode<CollisionShape3D>("HurtBoxComponent/CollisionShape3D").Shape = shape;
		foreach (string profileName in new[] { "slash_1h", "chop_1h", "thrust", "spin" })
		{
			var profile = GD.Load<MeleeAttackProfile>($"res://scenes/attacks/melee_profiles/{profileName}.tres");
			foreach (float side in new[] { -1f, 0, 1f })
			{
				harness.PracticeTarget.Position = new Vector3(side * profile.Width * .4f, 0, -.03f);
				Health(harness).SetHealth(1000, 1000);
				await runner.SimulateFrames(2);
				var controller = Start(harness, profileName);
				controller._PhysicsProcess(2);
				AssertInt(controller.ContactCount).OverrideFailureMessage($"{profileName}, side {side}: close contact").IsEqual(1);
				AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
			}
		}
	}

	[TestCase]
	public async Task AllMeleeFootprintsHitLowAndFlyingTargetsWithoutHeightAiming()
	{
		var (runner, harness) = await Load();
		var bodyShape = harness.PracticeTarget.GetNode<CollisionShape3D>("CollisionShape3D");
		var hurtbox = harness.PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent");
		bodyShape.Shape = new CapsuleShape3D { Radius = .15f, Height = .3f };
		hurtbox.GetNode<CollisionShape3D>("CollisionShape3D").Shape = bodyShape.Shape;
		bodyShape.Position = Vector3.Up * .15f;
		hurtbox.Position = Vector3.Up * .15f;
		foreach (string profile in new[] { "slash_1h", "chop_1h", "thrust", "spin" })
		{
			foreach (float height in new[] { -.1f, 0, 5, 24 })
			{
				Health(harness).SetHealth(1000, 1000);
				harness.PracticeTarget.Position = new Vector3(0, height, -1.8f);
				await runner.SimulateFrames(2);
				var controller = Start(harness, profile);
				controller._PhysicsProcess(2);
				AssertInt(controller.ContactCount).IsEqual(1);
				AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
			}
		}
		// Vertical tolerance must not extend the horizontal reach or hit behind a slash.
		foreach (var position in new[] { new Vector3(0, 12, -5), new Vector3(0, 12, 2) })
		{
			Health(harness).SetHealth(1000, 1000);
			harness.PracticeTarget.Position = position;
			await runner.SimulateFrames(2);
			Start(harness)._PhysicsProcess(2);
			AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		}
	}

	[TestCase]
	public async Task VisualElevationCannotBypassWallsAndMeleeAimAssistUsesHorizontalRange()
	{
		var (runner, harness) = await Load();
		harness.Player.Position += Vector3.Up * 8;
		harness.PracticeTarget.Position = new Vector3(0, 12, -2);
		var wall = new StaticBody3D { Position = new Vector3(0, 1, -1), CollisionLayer = 2 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3, 2, .2f) } });
		runner.Scene().AddChild(wall);
		await runner.SimulateFrames(3);
		AssertObject(harness.Player.AttackController.FindMeleeAimAssistTarget()).IsSame(harness.PracticeTarget);
		var controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Obstructed");
		wall.QueueFree();
		await runner.SimulateFrames(2);
		Start(harness)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
	}

	[TestCase]
	public async Task ExistingOverlapHitsImmediatelyAndOnlyOnceAcrossBodyAndHurtbox()
	{
		var (_, harness) = await Load();
		var controller = Start(harness);
		controller._PhysicsProcess(.4);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertInt(controller.ContactCount).IsEqual(1);
		controller._PhysicsProcess(.1);
		controller._PhysicsProcess(.6);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertString(controller.LastResult).IsEqual("Damaged");
	}

	[TestCase]
	public async Task AWholeSkippedContactWindowStillHits()
	{
		var (_, harness) = await Load();
		var controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertBool(controller.IsAttacking).IsFalse();
	}

	[TestCase]
	public async Task ActualTargetEdgesCountButBehindAndOutOfReachDoNot()
	{
		var (runner, harness) = await Load();
		foreach (var position in new[] { new Vector3(0, 0, 2), new Vector3(0, 0, -4) })
		{
			harness.PracticeTarget.Position = position;
			await runner.SimulateFrames(3);
			var controller = Start(harness);
			controller._PhysicsProcess(2);
			AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
			AssertString(controller.LastResult).IsEqual("No contact");
		}
		harness.PracticeTarget.Position = new Vector3(0, 0, -2.7f);
		await runner.SimulateFrames(3);
		Start(harness)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
	}

	[TestCase]
	public async Task ContactUsesActorFacingAndCommitsItForTheRestOfTheSwing()
	{
		var (runner, harness) = await Load();
		harness.Player.LookAt(harness.Player.GlobalPosition + Vector3.Right);
		harness.PracticeTarget.Position = new Vector3(2, 0, 0);
		await runner.SimulateFrames(3);
		var controller = Start(harness);
		controller._PhysicsProcess(.4);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertBool(controller.CommittedFacing.IsEqualApprox(Vector3.Right)).IsTrue();
		Health(harness).SetHealth(1000, 1000);
		harness.PracticeTarget.Position = new Vector3(0, 0, -4);
		harness.Player.LookAt(harness.Player.GlobalPosition + Vector3.Forward);
		await runner.SimulateFrames(3);
		controller = Start(harness);
		controller._PhysicsProcess(.4);
		harness.Player.LookAt(harness.Player.GlobalPosition + Vector3.Back);
		harness.PracticeTarget.Position = new Vector3(0, 0, -2);
		await runner.SimulateFrames(3);
		controller._PhysicsProcess(.1);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertBool(controller.CommittedFacing.IsEqualApprox(Vector3.Forward)).IsTrue();
	}

	[TestCase]
	public async Task WallsBlockContactAndProduceAnObstructionOutcome()
	{
		var (runner, harness) = await Load();
		var wall = new StaticBody3D { Position = new Vector3(0, 1, -1), CollisionLayer = 2 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3, 2, .2f) } });
		runner.Scene().AddChild(wall);
		await runner.SimulateFrames(3);
		var controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Obstructed");
	}

	[TestCase]
	public async Task SmallQueryBatchesDoNotTruncateCrowdedAttacks()
	{
		var (runner, harness) = await Load();
		var scene = GD.Load<PackedScene>("res://scenes/debug/combat/combat_practice_target.tscn");
		var targets = Enumerable.Range(0, 12).Select(_ => scene.Instantiate<Node3D>()).ToArray();
		foreach (var target in targets) { target.Position = new Vector3(0, 0, -2); runner.Scene().AddChild(target); }
		await runner.SimulateFrames(3);
		harness.Player.GetNode<MeleeHitDetector>("MeleeHitDetector").QueryBatchSize = 8;
		var controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertInt(controller.ContactCount).IsEqual(13);
		foreach (var target in targets) AssertFloat(target.GetNode<HealthComponent>("HealthComponent").CurrentHealth).IsEqual(990);
	}

	[TestCase]
	public async Task WrongFactionAndCancelledAttacksCannotDamageTargets()
	{
		var (_, harness) = await Load();
		var controller = Start(harness, mask: 4);
		controller._PhysicsProcess(2);
		AssertInt(controller.ContactCount).IsEqual(0);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		controller = Start(harness);
		controller.CancelAttack();
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Interrupted");
	}

	[TestCase]
	public async Task SwooshEnabledAndDisabledHaveIdenticalDamageAndThrustHasNoSwoosh()
	{
		var (_, harness) = await Load();
		Start(harness)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		Health(harness).SetHealth(1000, 1000);
		Start(harness, effects: true)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertObject(GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/thrust.tres").SwooshScene).IsNull();
	}

	[TestCase]
	public async Task NativeMonsterMeleeDamagesHeroThroughTheSameDetector()
	{
		var (_, harness) = await Load();
		harness.SpawnMonster();
		var monster = harness.Player.GetParent().GetChildren().OfType<EnemyBase>().Single();
		monster.GlobalPosition = harness.Player.GlobalPosition + Vector3.Forward * 1.8f;
		harness.Player.HurtBoxComponent.Armor = 0;
		float health = harness.Player.HealthComponent.CurrentHealth;
		harness.MonsterAttack();
		var controller = monster.GetNode<AttackController>("AttackController");
		controller.SetPhysicsProcess(false);
		AssertBool(controller.IsAttacking).IsTrue();
		// The minion's chop connects at 52-66% of its one-second swing, when the blade visibly comes down.
		controller._PhysicsProcess(.45);
		AssertInt(controller.ContactCount).IsEqual(0);
		controller._PhysicsProcess(.15);
		AssertBool(harness.Player.HealthComponent.CurrentHealth < health).IsTrue();
		AssertInt(controller.ContactCount).IsEqual(1);
		AssertString(controller.LastResult).IsEqual("Damaged");
	}

	[TestCase]
	public async Task DamageResolutionDistinguishesArmorAndImmunityFromMisses()
	{
		var (_, harness) = await Load();
		var hurtBox = harness.PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent");
		using var random = new RandomNumberGenerator { Seed = 42 };
		hurtBox.Invulnerable = true;
		AssertBool(hurtBox.ResolveDamage(10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Invulnerable).IsTrue();
		hurtBox.Invulnerable = false;
		AssertBool(hurtBox.ResolveDamage(0, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Absorbed).IsTrue();
		hurtBox.DamageFilter = DamageSourceFlags.None;
		AssertBool(hurtBox.ResolveDamage(10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Filtered).IsTrue();
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
	}

	[TestCase]
	public async Task ChopThrustAndRadialProfilesUseTheirAuthoredFootprints()
	{
		var (runner, harness) = await Load();
		foreach (var profile in new[] { "chop_1h", "thrust", "spin" })
		{
			Health(harness).SetHealth(1000, 1000);
			harness.PracticeTarget.Position = new Vector3(0, 0, profile == "spin" ? 2 : -2);
			await runner.SimulateFrames(3);
			Start(harness, profile)._PhysicsProcess(2);
			AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		}
		harness.PracticeTarget.Position = new Vector3(1.6f, 0, -2);
		Health(harness).SetHealth(1000, 1000);
		await runner.SimulateFrames(3);
		Start(harness, "thrust")._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
	}

	[TestCase]
	public void AuthoredMeleeProfilesAreValidAndOnlySwingProfilesRequireEffects()
	{
		string folder = ProjectSettings.GlobalizePath("res://scenes/attacks/melee_profiles");
		foreach (string file in Directory.EnumerateFiles(folder, "*.tres"))
		{
			var profile = GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/" + Path.GetFileName(file));
			AssertBool(profile.IsValid).IsTrue();
			using var shape = profile.CreateShape();
			AssertObject(shape).IsNotNull();
		}
	}

	[TestCase]
	public async Task AxeBroadswordAndHalberdTrailsFollowTheirAnimatedBladeMarkers()
	{
		var (_, harness) = await Load();
		foreach (int weaponIndex in new[] { 1, 2, 4 })
		{
			harness.EquipPreview(weaponIndex);
			var weapon = harness.Weapons[weaponIndex];
			var controller = harness.Player.AttackController;
			controller.EffectsEnabled = true;
			var definition = new AttackDefinition { AttackStyle = weapon.AttackStyle };
			AssertBool(controller.TryPrepareAttack(definition, weapon.AttackStyle, 1, out var attack, weapon.MeleeContact)).IsTrue();
			AssertBool(controller.StartAttack(attack, 10, 10, 0, 8)).IsTrue();
			controller.SetPhysicsProcess(false);
			controller._PhysicsProcess(.42);
			var swoosh = harness.Player.GetParent().GetChildren().OfType<MeleeSwoosh>().Last();
			AssertBool(swoosh.FollowsWeapon).IsTrue();
			AssertInt(swoosh.TrailSampleCount).IsGreater(2);
			var manager = harness.Player.GetNode<BoneAttachmentManager>("BoneAttachmentManager");
			var mount = manager.AttachmentNodes[weapon.IsTwoHanded ? AttachmentType.TwoHandedWeapon : AttachmentType.OneHandedWeapon];
			var tip = (Marker3D)mount.FindChild("TrailTip", true, false);
			var bladeBase = (Marker3D)mount.FindChild("TrailBase", true, false);
			AssertBool(swoosh.LastBladeTip.IsEqualApprox(tip.GlobalPosition)).IsTrue();
			AssertBool(swoosh.LastBladeBase.IsEqualApprox(bladeBase.GlobalPosition)).IsTrue();
			var firstTip = swoosh.LastBladeTip;
			controller._PhysicsProcess(.12);
			AssertBool(firstTip.DistanceTo(swoosh.LastBladeTip) > .01f).IsTrue();
			AssertBool(swoosh.LastBladeTip.IsEqualApprox(tip.GlobalPosition)).IsTrue();
			controller.CancelAttack();
		}
	}

	[TestCase]
	public async Task SpecialSpinAttackSwooshFollowsWeaponBladeTrail()
	{
		var (runner, harness) = await Load();
		foreach (int weaponIndex in new[] { 0, 1, 2 })
		{
			harness.EquipPreview(weaponIndex);
			var controller = harness.Player.AttackController;
			controller.EffectsEnabled = true;
			var weapon = (Weapon)harness.Player.Inventory.EquippedItems[EquipmentSlot.WeaponHand].Item;
			AssertBool(controller.TryPrepareWeaponAttack(weapon, out var attack, isSpecial: true)).IsTrue();
			AssertBool(controller.StartPreparedWeaponAttack(attack, weapon, isSpecial: true)).IsTrue();
			controller.SetPhysicsProcess(false);
			controller._PhysicsProcess(.25);
			var swoosh = harness.Player.GetParent().GetChildren().OfType<MeleeSwoosh>().Last();
			AssertBool(swoosh.FollowsWeapon).IsTrue();
			AssertInt(swoosh.TrailSampleCount).IsGreater(2);
			var manager = harness.Player.GetNode<BoneAttachmentManager>("BoneAttachmentManager");
			var mount = manager.AttachmentNodes[weapon.IsTwoHanded ? AttachmentType.TwoHandedWeapon : AttachmentType.OneHandedWeapon];
			var tip = (Marker3D)mount.FindChild("TrailTip", true, false);
			var bladeBase = (Marker3D)mount.FindChild("TrailBase", true, false);
			AssertBool(swoosh.LastBladeTip.IsEqualApprox(tip.GlobalPosition)).IsTrue();
			AssertBool(swoosh.LastBladeBase.IsEqualApprox(bladeBase.GlobalPosition)).IsTrue();
			controller.CancelAttack();
		}
	}

	[TestCase]
	public async Task SkippedContactWindowStillBuildsTheBladeTrailAndPoolingClearsIt()
	{
		var (_, harness) = await Load();
		var controller = Start(harness, effects: true);
		controller._PhysicsProcess(2);
		var swoosh = harness.Player.GetParent().GetChildren().OfType<MeleeSwoosh>().Last();
		AssertBool(swoosh.FollowsWeapon).IsTrue();
		AssertInt(swoosh.TrailSampleCount).IsGreater(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		swoosh.OnDespawnedToPool();
		swoosh.OnSpawnedFromPool();
		swoosh.Initialize(Transform3D.Identity, 2);
		swoosh.Sample(Transform3D.Identity, .5f);
		AssertInt(swoosh.TrailSampleCount).IsEqual(0);
		AssertBool(swoosh.FollowsWeapon).IsFalse();
	}

	[TestCase]
	public void SwingWeaponScenesExposeEditableBladeMarkers()
	{
		var roots = Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/items/weapons"), "*.tres", SearchOption.AllDirectories)
			.Select(file => GD.Load<Resource>("res://" + Path.GetRelativePath(ProjectSettings.GlobalizePath("res://"), file))).ToArray();
		foreach (var weapon in roots.OfType<Weapon>().Where(w => w.AttackStyle.ToString().EndsWith(".slash") || w.AttackStyle.ToString().EndsWith(".chop")))
		{
			var visual = weapon.Scene.Instantiate<Node3D>();
			var bladeBase = visual.FindChild("TrailBase", true, false) as Marker3D;
			var tip = visual.FindChild("TrailTip", true, false) as Marker3D;
			AssertObject(bladeBase).OverrideFailureMessage(weapon.ResourcePath).IsNotNull();
			AssertObject(tip).OverrideFailureMessage(weapon.ResourcePath).IsNotNull();
			AssertBool(tip.Position.DistanceTo(bladeBase.Position) > .05f).IsTrue();
			visual.Free();
		}
		GC.KeepAlive(roots);
	}

	[TestCase]
	public void ProfileValidationAndMonsterEngagementUseTheSameReach()
	{
		using var contact = new MeleeAttackProfile { Reach = 2, EngagementMargin = .4f };
		using var attack = new AttackDefinition { MeleeContact = contact };
		using var enemy = new MeleeAttackStrategy { EngagementDistance = 8, Attack = attack };
		AssertFloat(enemy.EffectiveRange).IsEqualApprox(1.6, .0001);
		contact.Reach = float.NaN;
		AssertBool(contact.IsValid).IsFalse();
	}
	[TestCase]
	public void SecondaryAttackDerivesCorrectArchetypeStyleAndContactProfile()
	{
		using var sword1h = new Weapon { AttackStyle = "sword.1h.slash" };
		AssertString(sword1h.GetEffectiveSecondaryStyle().ToString()).IsEqual("sword.1h.special");
		AssertString(sword1h.GetEffectiveSecondaryMeleeContact().ResourcePath).EndsWith("slash_2h.tres");

		using var sword2h = new Weapon { AttackStyle = "sword.2h.slash" };
		AssertString(sword2h.GetEffectiveSecondaryStyle().ToString()).IsEqual("sword.2h.special");
		AssertString(sword2h.GetEffectiveSecondaryMeleeContact().ResourcePath).EndsWith("spin.tres");

		using var axe1h = new Weapon { AttackStyle = "axe.1h.chop" };
		AssertString(axe1h.GetEffectiveSecondaryStyle().ToString()).IsEqual("axe.1h.special");
		AssertString(axe1h.GetEffectiveSecondaryMeleeContact().ResourcePath).EndsWith("slash_2h.tres");

		using var axe2h = new Weapon { AttackStyle = "axe.2h.chop" };
		AssertString(axe2h.GetEffectiveSecondaryStyle().ToString()).IsEqual("axe.2h.special");
		AssertString(axe2h.GetEffectiveSecondaryMeleeContact().ResourcePath).EndsWith("slash_2h.tres");

		using var spear2h = new Weapon { AttackStyle = "spear.2h.thrust" };
		AssertString(spear2h.GetEffectiveSecondaryStyle().ToString()).IsEqual("spear.2h.special");
		AssertString(spear2h.GetEffectiveSecondaryMeleeContact().ResourcePath).EndsWith("slash_2h.tres");

		using var blunt2h = new Weapon { AttackStyle = "blunt.2h.chop" };
		AssertString(blunt2h.GetEffectiveSecondaryStyle().ToString()).IsEqual("blunt.2h.special");
		AssertString(blunt2h.GetEffectiveSecondaryMeleeContact().ResourcePath).EndsWith("spin.tres");

		using var ranged = new RangedWeapon { AttackStyle = "crossbow.shoot" };
		AssertString(ranged.GetEffectiveSecondaryStyle().ToString()).IsEqual("crossbow.shoot.special");
		AssertObject(ranged.GetEffectiveSecondaryMeleeContact()).IsNull();
		AssertFloat(ranged.SecondaryDamageMultiplier).IsEqual(2.0f);
	}

	[TestCase]
	public void MartialTechniqueActionDerivesWeaponSecondaryProperties()
	{
		using var weapon = new Weapon
		{
			AttackStyle = "axe.1h.chop",
			SecondaryAnimationId = "custom_anim",
			SecondaryDelay = 0.2f,
			SecondaryPerformDuration = 0.7f,
			SecondaryCooldownDuration = 1.4f,
			SecondaryDamageMultiplier = 1.8f
		};

		var action = new MartialTechniqueAction(weapon);
		AssertString(action.AnimationId).IsEqual("custom_anim");
		AssertFloat(action.Delay).IsEqual(0.2f);
		AssertFloat(action.PerformDuration).IsEqual(0.7f);
		AssertFloat(action.CooldownDuration).IsEqual(1.4f);
		AssertString(action.CooldownKey).IsEqual("axe.1h.special");
	}

	[TestCase]
	public async Task SecondaryAttackAppliesSecondaryDamageMultiplier()
	{
		var (_, harness) = await Load();
		harness.Player.Inventory.Unequip(EquipmentSlot.WeaponHand);
		harness.Player.Stats.BaseMinDamage = 0;
		harness.Player.Stats.BaseMaxDamage = 0;
		harness.Player.Stats.BaseCritChance = 0;
		var controller = harness.Player.AttackController;

		using var def = new Weapon
		{
			AttackStyle = "axe.1h.chop",
			DamageMin = 10,
			DamageMax = 10,
			ValidSlots = ValidSlots.WeaponHand,
			SecondaryDamageMultiplier = 2.0f,
			MeleeContact = GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_1h.tres")
		};
		AssertBool(harness.Player.Inventory.TryAdd(def).Success).IsTrue();
		var slot = harness.Player.Inventory.Items.Last();
		var weapon = (Weapon)slot.Item;
		harness.Player.Inventory.Equip(slot);

		Health(harness).SetHealth(1000, 1000);
		AssertBool(controller.TryPrepareWeaponAttack(weapon, out var primaryAttack, isSpecial: false)).IsTrue();
		AssertBool(controller.StartPreparedWeaponAttack(primaryAttack, weapon, isSpecial: false)).IsTrue();
		controller.SetPhysicsProcess(false);
		controller._PhysicsProcess(2);
		float primaryDamage = 1000 - Health(harness).CurrentHealth;
		AssertFloat(primaryDamage).IsEqual(11.0f);

		Health(harness).SetHealth(1000, 1000);
		var action = new MartialTechniqueAction(weapon);
		AssertBool(action.TryPrepare(harness.Player, out var secondaryAttack)).IsTrue();
		AssertBool(action.StartPrepared(harness.Player, secondaryAttack)).IsTrue();
		controller._PhysicsProcess(2);
		float secondaryDamage = 1000 - Health(harness).CurrentHealth;
		AssertFloat(secondaryDamage).IsEqual(22.0f);
		AssertFloat(secondaryDamage).IsEqualApprox(primaryDamage * 2.0f, 0.001f);

		using var crossbow = new RangedWeapon
		{
			AttackStyle = "crossbow.shoot",
			Range = 20.0f,
			ProjectileSpeed = 10.0f,
			SecondaryDamageMultiplier = 2.0f
		};
		AssertBool(controller.TryPrepareWeaponAttack(crossbow, out var primaryRanged, isSpecial: false)).IsTrue();
		AssertFloat(primaryRanged.Definition.ProjectileSpeed).IsEqual(10.0f);
		AssertBool(controller.TryPrepareWeaponAttack(crossbow, out var secondaryRanged, isSpecial: true)).IsTrue();
		AssertFloat(secondaryRanged.Definition.ProjectileSpeed).IsEqual(12.5f);
	}

	/// <summary>A facing lock lead commits the swing early: turning afterwards cannot redirect it.</summary>
	[TestCase(true)]
	[TestCase(false)]
	public async Task FacingLockLeadCommitsTheSwingBeforeContact(bool locked)
	{
		var (runner, harness) = await Load();
		harness.Player.Rotation = Vector3.Zero;
		harness.PracticeTarget.Position = new Vector3(0, 0, -2);
		await runner.SimulateFrames(2);
		// The slash's contact starts at 0.3 s; a 0.3 s lead commits on the first tick.
		var controller = Start(harness, configure: d => d.FacingLockLead = locked ? .3f : 0);
		controller._PhysicsProcess(.01);
		AssertBool(controller.HasCommittedFacing).IsEqual(locked);
		harness.Player.Rotation = new Vector3(0, Mathf.Pi, 0);
		controller._PhysicsProcess(.4);
		AssertBool(controller.CommittedFacing.IsEqualApprox(locked ? Vector3.Forward : Vector3.Back)).IsTrue();
		AssertInt(controller.ContactCount).IsEqual(locked ? 1 : 0);
	}

	[TestCase]
	public async Task HitStopPausesOnlyTheAttackersClockOncePerSwing()
	{
		var (runner, harness) = await Load();
		harness.Player.Rotation = Vector3.Zero;
		harness.PracticeTarget.Position = new Vector3(0, 0, -2);
		await runner.SimulateFrames(2);
		int pauses = 0;
		var controller = Start(harness, configure: d => d.HitStopDuration = .1f);
		controller.HitStopStarted += _ => pauses++;
		controller._PhysicsProcess(.4);
		AssertInt(controller.ContactCount).IsEqual(1);
		float remaining = controller.RecoveryRemaining;
		controller._PhysicsProcess(.05);
		controller._PhysicsProcess(.06);
		AssertFloat(controller.RecoveryRemaining).IsEqual(remaining);
		controller._PhysicsProcess(.1);
		AssertFloat(controller.RecoveryRemaining).IsEqualApprox(remaining - .1f, .0001);
		AssertInt(pauses).IsEqual(1);
	}

	[TestCase]
	public async Task HyperArmorFollowsTheNormalizedWindowAndEndsWithTheAttack()
	{
		var (_, harness) = await Load();
		var gate = harness.Player.ActionGate;
		var controller = Start(harness, configure: d => { d.HyperArmorStart = 0; d.HyperArmorEnd = .5f; });
		AssertBool(gate.IsHyperArmored).IsTrue();
		controller._PhysicsProcess(.6);
		AssertBool(gate.IsHyperArmored).IsFalse();
		controller = Start(harness, configure: d => { d.HyperArmorStart = 0; d.HyperArmorEnd = 1; });
		AssertBool(gate.IsHyperArmored).IsTrue();
		controller.CancelAttack();
		AssertBool(gate.IsHyperArmored).IsFalse();
	}

	[TestCase]
	public async Task PreparationRejectsInvalidImpactAndTracking()
	{
		var (_, harness) = await Load();
		var controller = harness.Player.AttackController;
		bool Prepare(Action<AttackDefinition> configure)
		{
			var definition = new AttackDefinition { AttackStyle = "sword.1h.slash",
				MeleeContact = GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_1h.tres") };
			configure(definition);
			return controller.TryPrepareAttack(definition, "", 1, out _);
		}
		AssertBool(Prepare(d => d.FacingLockLead = .3f)).IsTrue();
		AssertBool(Prepare(d => d.FacingLockLead = .31f)).IsFalse();
		AssertBool(Prepare(d => { d.IsRanged = true; d.FacingLockLead = .1f; })).IsFalse();
		AssertBool(Prepare(d => { d.HyperArmorStart = .6f; d.HyperArmorEnd = .4f; })).IsFalse();
		AssertBool(Prepare(d => d.PoiseDamage = -1)).IsFalse();
		AssertBool(Prepare(d => d.Knockback = float.NaN)).IsFalse();
		AssertBool(Prepare(d => d.WindUpTurnRate = -1)).IsFalse();
		AssertBool(Prepare(d => d.ThreatTier = AttackThreatTier.Heavy)).IsTrue();
		AssertBool(Prepare(d => { d.ThreatTier = AttackThreatTier.Heavy; d.DefenseTraits = AttackDefenseTraits.Dodgeable; })).IsFalse();
		AssertBool(Prepare(d => d.ThreatTier = AttackThreatTier.Unblockable)).IsFalse();
		AssertBool(Prepare(d => { d.ThreatTier = AttackThreatTier.Unblockable; d.DefenseTraits = AttackDefenseTraits.Dodgeable; })).IsTrue();
	}

	/// <summary>An AI wind-up's capped turn cannot snap onto a target that circles it.</summary>
	[TestCase]
	public async Task CappedFacingRequestsTurnNoFasterThanTheirRate()
	{
		var (_, harness) = await Load();
		var movement = harness.Player.MovementComponent;
		movement.SetLookAtDirection(Vector3.Forward);
		movement.FaceDirection(Vector3.Right, 90);
		movement._PhysicsProcess(.1);
		AssertFloat(Mathf.RadToDeg(movement.LookAtDirection.AngleTo(Vector3.Forward))).IsEqualApprox(9, .1);
		movement.FaceDirection(Vector3.Right);
		movement._PhysicsProcess(.1);
		AssertFloat(Mathf.RadToDeg(movement.LookAtDirection.AngleTo(Vector3.Right))).IsLess(1);
	}
}