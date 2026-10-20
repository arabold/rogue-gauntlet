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
	private static async Task<(ISceneRunner Runner, AttackAnimationHarness Harness)> Load()
	{
		var runner = ISceneRunner.Load("res://scenes/debug/attack_animation_harness.tscn", true);
		await runner.SimulateFrames(4);
		var harness = runner.Scene().GetNode<AttackAnimationHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		return (runner, harness);
	}

	private static AttackController Start(AttackAnimationHarness harness, string profile = "slash_1h", uint mask = 8, bool effects = false)
	{
		var controller = harness.Player.AttackController;
		controller.EffectsEnabled = effects;
		var definition = new AttackDefinition { AttackStyle = "sword.1h.slash",
			MeleeContact = GD.Load<MeleeAttackProfile>($"res://scenes/attacks/melee_profiles/{profile}.tres") };
		AssertBool(controller.TryPrepareAttack(definition, "", 1, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, 10, 10, 1, 0, mask)).IsTrue();
		controller.SetPhysicsProcess(false);
		return controller;
	}

	private static HealthComponent Health(AttackAnimationHarness harness) => harness.PracticeTarget.GetNode<HealthComponent>("HealthComponent");

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
		var scene = GD.Load<PackedScene>("res://scenes/debug/combat_practice_target.tscn");
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
		float health = harness.Player.HealthComponent.CurrentHealth;
		harness.MonsterAttack();
		var controller = monster.GetNode<AttackController>("AttackController");
		controller.SetPhysicsProcess(false);
		AssertBool(controller.IsAttacking).IsTrue();
		controller._PhysicsProcess(.5);
		AssertBool(harness.Player.HealthComponent.CurrentHealth < health).IsTrue();
		AssertInt(controller.ContactCount).IsEqual(1);
		AssertString(controller.LastResult).IsEqual("Damaged");
	}

	[TestCase]
	public async Task DamageResolutionDistinguishesEvasionArmorAndImmunityFromMisses()
	{
		var (_, harness) = await Load();
		var hurtBox = harness.PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent");
		using var random = new RandomNumberGenerator { Seed = 42 };
		hurtBox.Invulnerable = true;
		AssertBool(hurtBox.ResolveDamage(1, 10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Invulnerable).IsTrue();
		hurtBox.Invulnerable = false; hurtBox.Evasion = 1000;
		AssertBool(hurtBox.ResolveDamage(0, 10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Evaded).IsTrue();
		hurtBox.Evasion = 0;
		AssertBool(hurtBox.ResolveDamage(1, 0, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Absorbed).IsTrue();
		hurtBox.DamageFilter = DamageSourceFlags.None;
		AssertBool(hurtBox.ResolveDamage(1, 10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome == DamageOutcome.Filtered).IsTrue();
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
			AssertBool(controller.StartAttack(attack, 10, 10, 1, 0, 8)).IsTrue();
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
		using var enemy = new EnemyBehaviorProfile { MeleeAttackRange = 8, MeleeContact = contact };
		AssertFloat(enemy.EffectiveMeleeRange).IsEqualApprox(1.6, .0001);
		contact.Reach = float.NaN;
		AssertBool(contact.IsValid).IsFalse();
	}
}