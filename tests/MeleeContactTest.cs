namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
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
	private const string Weapons = "res://scenes/items/weapons/";

	/// <summary>Settles a freshly loaded harness with the hero held still, so only the test moves it.</summary>
	private static async Task<CombatHarness> Settle(ISceneRunner runner)
	{
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		return harness;
	}

	private static readonly Dictionary<string, MeleeAttackProfile> Profiles = [];

	/// <summary>An authored melee profile, loaded once per run: every load first settles the resource cache.</summary>
	private static MeleeAttackProfile Profile(string name) => Profiles.TryGetValue(name, out var profile)
		? profile : Profiles[name] = TestFactories.Load<MeleeAttackProfile>($"res://scenes/attacks/melee_profiles/{name}.tres");

	/// <summary>Normalized contact start of a player style, so timing tests follow the rig profile's strike.</summary>
	private static float ContactStart(CombatHarness harness, StringName style)
	{
		AssertBool(harness.Player.AttackController.AnimationComponent.Profile.TryGetBinding(style, out var binding)).IsTrue();
		return binding.ContactStart;
	}

	private static AttackController Start(CombatHarness harness, string profile = "slash_1h", uint mask = 8, bool effects = false,
		Action<AttackDefinition> configure = null)
	{
		var controller = harness.Player.AttackController;
		controller.CancelAttack();
		controller.EffectsEnabled = effects;
		var definition = new AttackDefinition { AttackStyle = "sword.1h.slash", MeleeContact = Profile(profile) };
		configure?.Invoke(definition);
		AssertBool(controller.TryPrepareAttack(definition, "", 1, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, new AttackStats(10, 10, 0), mask)).IsTrue();
		controller.SetPhysicsProcess(false);
		return controller;
	}

	private static HealthComponent Health(CombatHarness harness) => harness.PracticeTarget.GetNode<HealthComponent>("HealthComponent");

	/// <summary>Moves a fresh practice target to <paramref name="position"/> and lets the physics space register it.</summary>
	private static async Task PlaceTarget(ISceneRunner runner, CombatHarness harness, Vector3 position)
	{
		TestFactories.ResetPracticeTarget(harness);
		harness.PracticeTarget.Position = position;
		await TestFactories.AwaitPhysicsFrames(runner);
	}

	[TestCase]
	public async Task BodyContactIsTimedAndCanBeEnteredAfterTheWindowOpens()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		harness.PracticeTarget.Position = new Vector3(0, 0, .85f); // touching the rear of the hero's physical body (radius .5)
		await TestFactories.AwaitPhysicsFrames(runner);
		var controller = Start(harness);
		controller._PhysicsProcess(.05);
		AssertInt(controller.ContactCount).IsEqual(0);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		harness.PracticeTarget.Position = new Vector3(0, 0, 5);
		await TestFactories.AwaitPhysicsFrames(runner);
		controller._PhysicsProcess(.35); // window opens while the target is absent
		AssertInt(controller.ContactCount).IsEqual(0);
		harness.PracticeTarget.Position = new Vector3(0, 0, .85f);
		await TestFactories.AwaitPhysicsFrames(runner);
		controller._PhysicsProcess(.1);
		AssertInt(controller.ContactCount).IsEqual(1);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		controller._PhysicsProcess(.1);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		// The body envelope obeys the same wall test as the forward swing.
		TestFactories.ResetPracticeTarget(harness);
		var wall = new StaticBody3D { Position = new Vector3(0, 1, .65f), CollisionLayer = 2 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3, 2, .1f) } });
		runner.Scene().AddChild(wall);
		await TestFactories.AwaitPhysicsFrames(runner);
		controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Obstructed");
	}

	/// <summary>
	/// Each authored footprint reaches what it covers and nothing else: a target's near edge, chop and thrust lanes and a
	/// spin's rear; never behind a forward swing, beyond reach or beside a thrust. Every footprint covers its full width
	/// at the attacker's centre and reaches low and flying targets in its lane, while that vertical tolerance never extends
	/// the horizontal reach.
	/// </summary>
	[TestCase]
	public async Task MeleeFootprintsHitWhatTheyCoverAndNothingElse()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		string[] profiles = ["slash_1h", "chop_1h", "thrust", "spin"];
		async Task Expect(Vector3 position, string profile, bool hit)
		{
			await PlaceTarget(runner, harness, position);
			var controller = Start(harness, profile);
			controller._PhysicsProcess(2);
			string where = $"{profile} at {position}";
			AssertInt(controller.ContactCount).OverrideFailureMessage($"{where}: contacts").IsEqual(hit ? 1 : 0);
			AssertFloat(Health(harness).CurrentHealth).OverrideFailureMessage($"{where}: health").IsEqual(hit ? 990 : 1000);
			if (!hit) AssertString(controller.LastResult).OverrideFailureMessage(where).IsEqual("No contact");
		}

		// The arena's practice target, at its authored size.
		await Expect(new Vector3(0, 0, -2.7f), "slash_1h", true);
		await Expect(new Vector3(0, 0, 2), "slash_1h", false);
		await Expect(new Vector3(0, 0, -4), "slash_1h", false);
		await Expect(new Vector3(0, 0, -2), "chop_1h", true);
		await Expect(new Vector3(0, 0, -2), "thrust", true);
		await Expect(new Vector3(1.6f, 0, -2), "thrust", false);
		await Expect(new Vector3(0, 0, 2), "spin", true);

		// A tiny target at the attacker's centre, across each footprint's width.
		var body = harness.PracticeTarget.GetNode<CollisionShape3D>("CollisionShape3D");
		var hurtbox = harness.PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent");
		body.Shape = hurtbox.GetNode<CollisionShape3D>("CollisionShape3D").Shape = new CapsuleShape3D { Radius = .08f, Height = .2f };
		foreach (string profile in profiles)
		{
			float width = Profile(profile).Width;
			foreach (float side in new[] { -1f, 0, 1f })
				await Expect(new Vector3(side * width * .4f, 0, -.03f), profile, true);
		}

		// A small target on the floor and far above it: no footprint needs height aiming.
		body.Shape = hurtbox.GetNode<CollisionShape3D>("CollisionShape3D").Shape = new CapsuleShape3D { Radius = .15f, Height = .3f };
		body.Position = hurtbox.Position = Vector3.Up * .15f;
		foreach (string profile in profiles)
			foreach (float height in new[] { -.1f, 0, 5, 24 })
				await Expect(new Vector3(0, height, -1.8f), profile, true);
		await Expect(new Vector3(0, 12, -5), "slash_1h", false);
		await Expect(new Vector3(0, 12, 2), "slash_1h", false);
	}

	/// <summary>A wall between attacker and target obstructs the swing, also when elevation lifts both above the wall.</summary>
	[TestCase]
	public async Task WallsObstructContactAtAnyElevationAndMeleeAimAssistUsesHorizontalRange()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		var wall = new StaticBody3D { Position = new Vector3(0, 1, -1), CollisionLayer = 2 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3, 2, .2f) } });
		runner.Scene().AddChild(wall);
		await PlaceTarget(runner, harness, new Vector3(0, 0, -2));
		var controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Obstructed");

		harness.Player.Position += Vector3.Up * 8;
		await PlaceTarget(runner, harness, new Vector3(0, 12, -2));
		AssertObject(harness.Player.AttackController.FindMeleeAimAssistTarget()).IsSame(harness.PracticeTarget);
		controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(1000);
		AssertString(controller.LastResult).IsEqual("Obstructed");

		wall.QueueFree();
		await TestFactories.AwaitPhysicsFrames(runner);
		Start(harness)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
	}

	[TestCase]
	public async Task ExistingOverlapHitsImmediatelyAndOnlyOnceAcrossBodyAndHurtbox()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
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
	public async Task ContactUsesActorFacingAndCommitsItForTheRestOfTheSwing()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		harness.Player.LookAt(harness.Player.GlobalPosition + Vector3.Right);
		await PlaceTarget(runner, harness, new Vector3(2, 0, 0));
		var controller = Start(harness);
		controller._PhysicsProcess(.4);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertBool(controller.CommittedFacing.IsEqualApprox(Vector3.Right)).IsTrue();
		harness.Player.LookAt(harness.Player.GlobalPosition + Vector3.Forward);
		await PlaceTarget(runner, harness, new Vector3(0, 0, -4));
		controller = Start(harness);
		controller._PhysicsProcess(.4);
		harness.Player.LookAt(harness.Player.GlobalPosition + Vector3.Back);
		harness.PracticeTarget.Position = new Vector3(0, 0, -2);
		await TestFactories.AwaitPhysicsFrames(runner);
		controller._PhysicsProcess(.1);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertBool(controller.CommittedFacing.IsEqualApprox(Vector3.Forward)).IsTrue();
	}

	[TestCase]
	public async Task SmallQueryBatchesDoNotTruncateCrowdedAttacks()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		var scene = TestFactories.Load<PackedScene>("res://scenes/debug/combat/combat_practice_target.tscn");
		var targets = Enumerable.Range(0, 12).Select(_ => scene.Instantiate<Node3D>()).ToArray();
		foreach (var target in targets) { target.Position = new Vector3(0, 0, -2); runner.Scene().AddChild(target); }
		await TestFactories.AwaitPhysicsFrames(runner);
		harness.Player.GetNode<MeleeHitDetector>("MeleeHitDetector").QueryBatchSize = 8;
		var controller = Start(harness);
		controller._PhysicsProcess(2);
		AssertInt(controller.ContactCount).IsEqual(13);
		foreach (var target in targets) AssertFloat(target.GetNode<HealthComponent>("HealthComponent").CurrentHealth).IsEqual(990);
	}

	[TestCase]
	public async Task WrongFactionAndCancelledAttacksCannotDamageTargets()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
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

	/// <summary>Effects are presentation only: a swing deals the same damage with its swoosh on or off.</summary>
	[TestCase]
	public async Task SwooshEnabledAndDisabledHaveIdenticalDamage()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		Start(harness)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		TestFactories.ResetPracticeTarget(harness);
		Start(harness, effects: true)._PhysicsProcess(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
	}

	/// <summary>Damage resolution reports why a contact dealt nothing: immunity, nothing left after mitigation, or the wrong faction.</summary>
	[TestCase]
	public void DamageResolutionReportsImmunityAbsorptionAndFilteringWithoutDamage()
	{
		var health = AutoFree(new HealthComponent());
		health.SetHealth(1000, 1000);
		var hurtBox = AutoFree(new HurtBoxComponent { HealthComponent = health, DamageFilter = DamageSourceFlags.All });
		using var random = new RandomNumberGenerator { Seed = 42 };
		hurtBox.Invulnerable = true;
		AssertObject(hurtBox.ResolveDamage(10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome).IsEqual(DamageOutcome.Invulnerable);
		hurtBox.Invulnerable = false;
		AssertObject(hurtBox.ResolveDamage(0, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome).IsEqual(DamageOutcome.Absorbed);
		hurtBox.DamageFilter = DamageSourceFlags.None;
		AssertObject(hurtBox.ResolveDamage(10, Vector3.Forward, attackerFaction: DamageSourceFlags.Player, random: random).Outcome).IsEqual(DamageOutcome.Filtered);
		AssertFloat(health.CurrentHealth).IsEqual(1000);
	}

	[TestCase]
	public void AuthoredMeleeProfilesAreValidAndBuildTheirShape()
	{
		string folder = ProjectSettings.GlobalizePath("res://scenes/attacks/melee_profiles");
		foreach (string file in System.IO.Directory.EnumerateFiles(folder, "*.tres"))
		{
			var profile = TestFactories.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/" + System.IO.Path.GetFileName(file));
			AssertBool(profile.IsValid).OverrideFailureMessage(file).IsTrue();
			using var shape = profile.CreateShape();
			AssertObject(shape).IsNotNull();
		}
	}

	/// <summary>A primary swing's and a special attack's swoosh trail the weapon's animated blade markers through the contact window.</summary>
	[TestCase]
	public async Task SwooshTrailsFollowTheWeaponsAnimatedBladeMarkers()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		var controller = harness.Player.AttackController;
		var manager = harness.Player.GetNode<BoneAttachmentManager>("BoneAttachmentManager");
		(MeleeSwoosh Swoosh, Marker3D Tip, Marker3D Base) Trail(Weapon weapon)
		{
			var swoosh = harness.Player.GetParent().GetChildren().OfType<MeleeSwoosh>().Last();
			AssertBool(swoosh.FollowsWeapon).OverrideFailureMessage(weapon.ResourcePath).IsTrue();
			AssertInt(swoosh.TrailSampleCount).IsGreater(2);
			var mount = manager.AttachmentNodes[weapon.IsTwoHanded ? AttachmentType.TwoHandedWeapon : AttachmentType.OneHandedWeapon];
			var tip = (Marker3D)mount.FindChild("TrailTip", true, false);
			var bladeBase = (Marker3D)mount.FindChild("TrailBase", true, false);
			AssertBool(swoosh.LastBladeTip.IsEqualApprox(tip.GlobalPosition)).IsTrue();
			AssertBool(swoosh.LastBladeBase.IsEqualApprox(bladeBase.GlobalPosition)).IsTrue();
			return (swoosh, tip, bladeBase);
		}

		// Axe, broadsword and halberd primaries: two steps inside the weapon's contact window, which follows its strike.
		foreach (string path in new[] { Weapons + "axe_common.tres", Weapons + "broadsword_common.tres", Weapons + "polearm/halberd_steel.tres" })
		{
			AssertBool(harness.ExecuteCommand("equip", path)).IsTrue();
			var weapon = (Weapon)harness.Player.Inventory.EquippedItems[EquipmentSlot.WeaponHand].Item;
			controller.EffectsEnabled = true;
			var definition = new AttackDefinition { AttackStyle = weapon.AttackStyle };
			AssertBool(controller.TryPrepareAttack(definition, weapon.AttackStyle, 1, out var attack, weapon.MeleeContact)).IsTrue();
			AssertBool(controller.StartAttack(attack, new AttackStats(10, 10, 0), 8)).IsTrue();
			controller.SetPhysicsProcess(false);
			float start = attack.Definition.HitWindowStart, step = (attack.Definition.HitWindowEnd - start) * .35f;
			controller._PhysicsProcess(start + step);
			var (swoosh, tip, _) = Trail(weapon);
			var firstTip = swoosh.LastBladeTip;
			controller._PhysicsProcess(step);
			AssertBool(firstTip.DistanceTo(swoosh.LastBladeTip) > .01f).IsTrue();
			AssertBool(swoosh.LastBladeTip.IsEqualApprox(tip.GlobalPosition)).IsTrue();
			controller.CancelAttack();
		}

		// Sword, axe and broadsword specials, sampled late in their window.
		foreach (string path in new[] { Weapons + "sword_common.tres", Weapons + "axe_common.tres", Weapons + "broadsword_common.tres" })
		{
			AssertBool(harness.ExecuteCommand("equip", path)).IsTrue();
			controller.EffectsEnabled = true;
			var weapon = (Weapon)harness.Player.Inventory.EquippedItems[EquipmentSlot.WeaponHand].Item;
			AssertBool(controller.TryPrepareWeaponAttack(weapon, out var attack, isSpecial: true)).IsTrue();
			AssertBool(controller.StartPreparedWeaponAttack(attack, weapon, isSpecial: true)).IsTrue();
			controller.SetPhysicsProcess(false);
			controller._PhysicsProcess(Mathf.Lerp(attack.Definition.HitWindowStart, attack.Definition.HitWindowEnd, .6f));
			Trail(weapon);
			controller.CancelAttack();
		}
	}

	[TestCase]
	public async Task SkippedContactWindowStillBuildsTheBladeTrailAndPoolingClearsIt()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		var controller = Start(harness, effects: true);
		controller._PhysicsProcess(2);
		var swoosh = harness.Player.GetParent().GetChildren().OfType<MeleeSwoosh>().Last();
		AssertBool(swoosh.FollowsWeapon).IsTrue();
		AssertInt(swoosh.TrailSampleCount).IsGreater(2);
		AssertFloat(Health(harness).CurrentHealth).IsEqual(990);
		AssertBool(controller.IsAttacking).IsFalse();
		swoosh.OnDespawnedToPool();
		swoosh.OnSpawnedFromPool();
		swoosh.Initialize(Transform3D.Identity, 2);
		swoosh.Sample(Transform3D.Identity, .5f);
		AssertInt(swoosh.TrailSampleCount).IsEqual(0);
		AssertBool(swoosh.FollowsWeapon).IsFalse();
	}

	[TestCase]
	public void ProfileValidationAndMonsterEngagementUseTheSameReach()
	{
		using var contact = new MeleeAttackProfile { Reach = 2, EngagementMargin = .4f };
		using var attack = new AttackDefinition { MeleeContact = contact };
		using var enemy = new MonsterAttackAction { MaximumRange = 8, Attack = attack, Duration = 1, Style = "melee" };
		AssertFloat(enemy.EffectiveMaximumRange).IsEqualApprox(1.6, .0001);
		contact.Reach = float.NaN;
		AssertBool(contact.IsValid).IsFalse();
	}

	[TestCase]
	public async Task SecondaryAttackAppliesSecondaryDamageMultiplier()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
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
			MeleeContact = Profile("slash_1h")
		};
		AssertBool(harness.Player.Inventory.TryAdd(def).Success).IsTrue();
		var slot = harness.Player.Inventory.Items.Last();
		var weapon = (Weapon)slot.Item;
		harness.Player.Inventory.Equip(slot);

		AssertBool(controller.TryPrepareWeaponAttack(weapon, out var primaryAttack, isSpecial: false)).IsTrue();
		AssertBool(controller.StartPreparedWeaponAttack(primaryAttack, weapon, isSpecial: false)).IsTrue();
		controller.SetPhysicsProcess(false);
		controller._PhysicsProcess(2);
		float primaryDamage = 1000 - Health(harness).CurrentHealth;
		AssertFloat(primaryDamage).IsEqual(11.0f);

		TestFactories.ResetPracticeTarget(harness);
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
	[TestCase]
	public async Task FacingLockLeadCommitsTheSwingBeforeContact()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		harness.Player.Rotation = Vector3.Zero;
		await PlaceTarget(runner, harness, new Vector3(0, 0, -2));
		// A lead as long as the slash's wind-up commits on the first tick.
		float contactStart = ContactStart(harness, "sword.1h.slash");
		var controller = Start(harness, configure: d => d.FacingLockLead = contactStart);
		controller._PhysicsProcess(.01);
		AssertBool(controller.HasCommittedFacing).IsTrue();
		harness.Player.Rotation = new Vector3(0, Mathf.Pi, 0);
		controller._PhysicsProcess(.4);
		AssertBool(controller.CommittedFacing.IsEqualApprox(Vector3.Forward)).IsTrue();
		AssertInt(controller.ContactCount).IsEqual(1);
	}

	[TestCase]
	public async Task HitStopPausesOnlyTheAttackersClockOncePerSwing()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		harness.Player.Rotation = Vector3.Zero;
		await PlaceTarget(runner, harness, new Vector3(0, 0, -2));
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
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
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

	/// <summary>Preparation rejects an attack whose timing, impact, tracking or projectile flight cannot play out, before it starts.</summary>
	[TestCase]
	public async Task PreparationRejectsInvalidImpactTrackingAndFlight()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await Settle(runner);
		var controller = harness.Player.AttackController;
		var slash = Profile("slash_1h");
		bool Prepare(Action<AttackDefinition> configure)
		{
			using var definition = new AttackDefinition { AttackStyle = "sword.1h.slash", MeleeContact = slash };
			configure(definition);
			bool accepted = controller.TryPrepareAttack(definition, "", 1, out var attack);
			AssertBool(accepted || attack == null).IsTrue();
			return accepted;
		}
		// One-second preparation: the lead may equal the wind-up but not exceed it.
		float contactStart = ContactStart(harness, "sword.1h.slash");
		AssertBool(Prepare(d => d.FacingLockLead = contactStart)).IsTrue();
		AssertBool(Prepare(d => d.FacingLockLead = contactStart + .01f)).IsFalse();
		AssertBool(Prepare(d => { d.IsRanged = true; d.FacingLockLead = .1f; })).IsFalse();
		AssertBool(Prepare(d => { d.HyperArmorStart = .6f; d.HyperArmorEnd = .4f; })).IsFalse();
		AssertBool(Prepare(d => d.PoiseDamage = -1)).IsFalse();
		AssertBool(Prepare(d => d.Knockback = float.NaN)).IsFalse();
		AssertBool(Prepare(d => d.WindUpTurnRate = -1)).IsFalse();

		// Projectile flight: crossbow.shoot releases at 0.45 of the second, so three shots 0.3 s apart outlast the attack.
		AssertBool(Prepare(d => { d.AttackStyle = "crossbow.shoot"; d.IsRanged = true; d.ProjectilePattern = ProjectilePattern.Spread;
			d.ProjectileCount = 3; d.SpreadAngle = 40; d.ProjectileStagger = .3f; d.HomingTurnRate = 360; d.HomingDelay = .05f;
			d.Range = 30; d.AimingAngle = 180; })).IsFalse();
		AssertBool(Prepare(d => { d.AttackStyle = "axe.1h.chop"; d.HomingTurnRate = 90; })).IsFalse();
		AssertBool(Prepare(d => { d.AttackStyle = "crossbow.shoot"; d.IsRanged = true; d.LaunchSpeedScale = 0; })).IsFalse();
		AssertBool(Prepare(d => { d.AttackStyle = "crossbow.shoot"; d.IsRanged = true; d.HomingDelay = float.NaN; })).IsFalse();
		AssertBool(Prepare(d => { d.AttackStyle = "crossbow.shoot"; d.IsRanged = true; d.LaunchSpeedScale = .5f; d.LaunchRampDuration = .1f; })).IsTrue();
		AssertBool(controller.IsAttacking).IsFalse();
	}
}
