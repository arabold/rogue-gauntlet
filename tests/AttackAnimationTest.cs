namespace RogueGauntlet.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Exercises authored rig coverage and the shared animation/contact execution clock.</summary>
[TestSuite]
[RequireGodotRuntime]
public class AttackAnimationTest
{
	private readonly AttackAnimationProfile _profile = TestFactories.Load<AttackAnimationProfile>("res://scenes/attacks/animation_profiles/kaykit_player.tres");

	[TestCase]
	public void EveryPlayerRigHasEveryAuthoredStyleClip()
	{
		AssertInt(_profile.Bindings.Length).IsGreater(10);
		foreach (string id in new[] { "barbarian", "knight", "rogue", "mage" })
		{
			var character = TestFactories.Load<CharacterClass>($"res://scenes/player/classes/class_{id}.tres");
			var model = character.CharacterScene.Instantiate<Node3D>();
			var animations = model.GetNode<AnimationPlayer>("AnimationPlayer");
			foreach (var binding in _profile.Bindings)
			{
				AssertBool(binding.IsValid).IsTrue();
				AssertBool(CharacterModel.FindSkeleton(model).FindBone("handslot.r") >= 0).IsTrue();
				AssertBool(animations.HasAnimation(binding.Clip)).OverrideFailureMessage($"{id}: missing {binding.Clip}").IsTrue();
				AssertBool(animations.GetAnimation(binding.Clip).Length > 0).IsTrue();
			}
			if (id == "barbarian" || id == "rogue")
			{
				var grip = TestFactories.Load<WeaponGrip>(id == "rogue" ? "res://scenes/items/grips/kaykit_crossbow.tres" : "res://scenes/items/grips/kaykit_melee.tres");
				var skeleton = CharacterModel.FindSkeleton(model);
				var native = skeleton.GetBoneRest(skeleton.FindBone(id == "rogue" ? "2H_Crossbow" : "1H_Axe"));
				AssertBool(grip.Transform.IsEqualApprox(native)).IsTrue();
			}
			model.Free();
		}
	}

	[TestCase]
	public void EveryMonsterResolvesItsAuthoredAndEnabledAttackSlots()
	{
		var paths = Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/enemies"), "*.tscn", SearchOption.AllDirectories);
		int count = 0;
		foreach (var path in paths)
		{
			var actor = TestFactories.Load<PackedScene>("res://" + Path.GetRelativePath(ProjectSettings.GlobalizePath("res://"), path)).Instantiate<Node>();
			try
			{
				if (actor is not EnemyBase enemy || enemy.Definition == null) continue;
				count++;
				var component = actor.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
				var policy = enemy.Definition.Behavior.AttackStrategy;
				AssertBool(component.TryResolve(policy.Style, out _)).OverrideFailureMessage(path + " " + policy.Style).IsTrue();
				if (policy is ProjectileAttackStrategy) AssertBool(component.TryResolve("melee", out _)).IsFalse();
			}
			finally { actor.Free(); }
		}
		AssertInt(count).IsGreaterEqual(5);
	}

	[TestCase]
	public void DuplicateOrInvalidBindingsAreRejected()
	{
		using var profile = new AttackAnimationProfile { Bindings = new[] {
			new AttackAnimationBinding { Style = "same", Clip = "Idle" },
			new AttackAnimationBinding { Style = "same", Clip = "Other" }
		} };
		AssertBool(profile.TryGetBinding("same", out _)).IsFalse();
		profile.Bindings = new[] { new AttackAnimationBinding { Style = "bad", Clip = "Idle", ContactStart = .8f, ContactEnd = .2f } };
		AssertBool(profile.TryGetBinding("bad", out _)).IsFalse();
		AssertBool(_profile.TryGetBinding("missing", out _)).IsFalse();
	}

	[TestCase]
	public async Task DefinitionOverrideAndTimingAreCapturedWithoutMutatingResources()
	{
		var runner = TestFactories.LoadCombatHarness();
		await runner.SimulateFrames(3);
		var player = runner.Scene().GetNode<Player>("Player");
		var controller = player.AttackController;
		using var definition = new AttackDefinition { AttackStyle = "spear.2h.thrust", HitWindowStart = 9, HitWindowEnd = 10 };
		AssertBool(controller.TryPrepareAttack(definition, "sword.1h.slash", 2, out var attack)).IsTrue();
		AssertString(attack.Playback.Style).IsEqual("spear.2h.thrust");
		// The rig binding's normalized window scales to the requested 2-second duration.
		AssertBool(controller.AnimationComponent.Profile.TryGetBinding("spear.2h.thrust", out var binding)).IsTrue();
		AssertFloat(attack.Definition.HitWindowStart).IsEqualApprox(2 * binding.ContactStart, .0001);
		AssertFloat(attack.Definition.HitWindowEnd).IsEqualApprox(2 * binding.ContactEnd, .0001);
		AssertFloat(definition.HitWindowStart).IsEqual(9);
		AssertFloat(definition.HitWindowEnd).IsEqual(10);
		definition.AttackStyle = "axe.1h.chop";
		AssertString(attack.Playback.Style).IsEqual("spear.2h.thrust");
		AssertBool(controller.TryPrepareAttack(definition, "sword.1h.slash", double.NaN, out _)).IsFalse();
	}

	[TestCase]
	public async Task PlaybackAndExecutorShareRecoveryAndCancellation()
	{
		var runner = TestFactories.LoadCombatHarness();
		await runner.SimulateFrames(3);
		var player = runner.Scene().GetNode<Player>("Player");
		var controller = player.AttackController;
		var animation = player.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		using var definition = new AttackDefinition { AttackStyle = "axe.1h.chop" };
		AssertBool(controller.TryPrepareAttack(definition, "", 1, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, new AttackStats(1, 1, 0), 0)).IsTrue();
		controller.SetPhysicsProcess(false);
		var skeleton = CharacterModel.FindSkeleton(player);
		int hand = skeleton.FindBone("hand.r");
		var initialPose = skeleton.GetBoneGlobalPose(hand);
		controller._PhysicsProcess(.2);
		AssertBool(initialPose.IsEqualApprox(skeleton.GetBoneGlobalPose(hand))).IsFalse();
		AssertBool(animation.IsPlaying).IsTrue();
		AssertBool(player.GetNode<AnimationTree>("AnimationTree").Active).IsFalse();
		controller._PhysicsProcess(.6);
		AssertBool(controller.IsAttacking).IsTrue();
		controller.CancelAttack();
		AssertBool(animation.IsPlaying).IsFalse();
		AssertBool(player.GetNode<AnimationTree>("AnimationTree").Active).IsTrue();
		controller._PhysicsProcess(1);
		AssertBool(controller.IsAttacking).IsFalse();
	}

	[TestCase]
	public async Task RangedReleaseOccursOnceAndCancellationPreventsADeferredShot()
	{
		var runner = TestFactories.LoadCombatHarness();
		await runner.SimulateFrames(3);
		var player = runner.Scene().GetNode<Player>("Player");
		var controller = player.AttackController;
		Node parent = player.GetTree().CurrentScene ?? runner.Scene();
		var before = parent.GetChildren().OfType<Projectile>().ToArray();
		using var definition = new AttackDefinition { AttackStyle = "crossbow.shoot", IsRanged = true };
		AssertBool(controller.TryPrepareAttack(definition, "", 1, out var attack)).IsTrue();
		controller.StartAttack(attack, new AttackStats(1, 1, 0), 0);
		controller.SetPhysicsProcess(false);
		controller._PhysicsProcess(.5);
		var released = parent.GetChildren().OfType<Projectile>().Except(before).ToArray();
		AssertInt(released.Length).IsEqual(1);
		controller._PhysicsProcess(.2);
		AssertInt(parent.GetChildren().OfType<Projectile>().Except(before).Count()).IsEqual(1);
		controller.CancelAttack();
		foreach (var projectile in released) ScenePool.Despawn(projectile);
		controller.StartAttack(attack, new AttackStats(1, 1, 0), 0);
		controller.SetPhysicsProcess(false);
		controller._PhysicsProcess(.2);
		controller.CancelAttack();
		controller._PhysicsProcess(1);
		AssertInt(parent.GetChildren().OfType<Projectile>().Except(before).Count()).IsEqual(0);
	}

	[TestCase]
	public async Task CrossbowAimsThroughCommitmentThenShootsAtReleaseAndCancelsCleanly()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.ExecuteCommand("spawn_distance", 6, Vector3.Zero);
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		await runner.SimulateFrames(2);
		var shooter = harness.Monster;
		var controller = shooter.AttackController;
		var player = shooter.GetNode<AnimationPlayer>("Pivot/Skeleton_Minion/AnimationPlayer");
		var tree = shooter.GetNode<AnimationTree>("EnemyBehaviorComponent/AnimationTree");
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsTrue();
		controller.SetPhysicsProcess(false);
		AssertString(player.AssignedAnimation).IsEqual("1H_Ranged_Aiming");
		AssertBool(tree.Active).IsFalse();
		controller._PhysicsProcess(.35);
		AssertBool(controller.IsProjectileAimLocked).IsTrue();
		AssertString(player.AssignedAnimation).IsEqual("1H_Ranged_Aiming");
		controller._PhysicsProcess(.3);
		AssertString(player.AssignedAnimation).IsEqual("1H_Ranged_Shoot");
		var shots = runner.Scene().GetChildren().OfType<Projectile>().Where(p => p.Attacker == shooter).ToArray();
		AssertInt(shots.Length).IsEqual(1);
		controller.CancelAttack();
		AssertBool(tree.Active).IsTrue();
		foreach (var shot in shots) ScenePool.Despawn(shot);
		shooter.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsTrue();
		controller.SetPhysicsProcess(false);
		controller._PhysicsProcess(.1);
		controller.CancelAttack();
		controller._PhysicsProcess(1);
		AssertBool(tree.Active).IsTrue();
		AssertInt(runner.Scene().GetChildren().OfType<Projectile>().Count(p => p.Attacker == shooter)).IsEqual(0);
	}

	[TestCase]
	public async Task MissingWindUpClipRejectsPreparationBeforeAcquiringTheActionGate()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		var shooter = harness.Monster;
		var component = shooter.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		component.Profile = (AttackAnimationProfile)component.Profile.Duplicate(true);
		component.Profile.Bindings[0].WindUpClip = "missing_aim";
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsFalse();
		AssertBool(shooter.ActionGate.IsBusy).IsFalse();
		AssertBool(shooter.AttackController.IsAttacking).IsFalse();
	}

	[TestCase("res://scenes/enemies/skeleton/skeleton_crossbow.tscn")]
	[TestCase("res://scenes/enemies/templates/projectile_enemy.tscn")]
	public async Task SkeletonCrossbowUsesPlayerGripAndPointsAtTargetOnceAimLocks(string path)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Monsters = [TestFactories.Load<PackedScene>(path)];
		harness.ExecuteCommand("spawn_distance", 6, Vector3.Zero);
		harness.ExecuteCommand("spawn", 0, Vector3.Zero);
		await runner.SimulateFrames(2);
		var shooter = harness.Monster;
		var mount = shooter.GetNode<BoneAttachment3D>("Pivot/Skeleton_Minion/Rig/Skeleton3D/BoneAttachment3D");
		var crossbow = mount.GetNode<Node3D>("Skeleton_Crossbow");
		var grip = TestFactories.Load<WeaponGrip>("res://scenes/items/grips/kaykit_crossbow.tres");
		AssertString(mount.BoneName).IsEqual(grip.Bone);
		AssertBool(crossbow.Transform.IsEqualApprox(grip.Transform)).IsTrue();
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsTrue();
		shooter.AttackController.SetPhysicsProcess(false);
		// The first part of preparation raises the weapon; check barrel alignment throughout the locked interval.
		foreach (double delta in new[] { .35, .1, .1 })
		{
			shooter.AttackController._PhysicsProcess(delta);
			await runner.SimulateFrames(2); // Let Skeleton3D update the attached visual from the sampled pose.
			Vector3 targetDirection = harness.Player.GlobalPosition - shooter.GlobalPosition;
			targetDirection.Y = 0;
			// Both KayKit crossbow meshes use local +Z along the barrel, independently of actor -Z facing.
			AssertFloat(crossbow.GlobalBasis.Z.Normalized().Dot(targetDirection.Normalized())).IsGreater(.98f);
		}
	}

	[TestCase]
	public async Task EquipmentSwitchCancelsTheAcceptedSwing()
	{
		var runner = TestFactories.LoadCombatHarness();
		await runner.SimulateFrames(3);
		var player = runner.Scene().GetNode<Player>("Player");
		var slot = player.Inventory.EquippedItems[EquipmentSlot.WeaponHand];
		var weapon = (Weapon)slot.Item;
		AssertBool(player.AttackController.TryPrepareWeaponAttack(weapon, out var attack)).IsTrue();
		AssertBool(player.AttackController.StartPreparedWeaponAttack(attack, weapon)).IsTrue();
		player.Inventory.Unequip(slot);
		player.AttackController._PhysicsProcess(.01);
		AssertBool(player.AttackController.IsAttacking).IsFalse();
		AssertBool(player.GetNode<CombatAnimationComponent>("CombatAnimationComponent").IsPlaying).IsFalse();
	}

	[TestCase]
	public async Task MissingStyleDoesNotAcceptAnActionOrStartCooldown()
	{
		var runner = TestFactories.LoadCombatHarness();
		await runner.SimulateFrames(3);
		var player = runner.Scene().GetNode<Player>("Player");
		var weapon = (Weapon)player.Inventory.EquippedItems[EquipmentSlot.WeaponHand].Item;
		weapon.AttackStyle = "missing.style";
		var slot = player.ActionManager.GetChildren().OfType<ActionSlot>().First(value => ReferenceEquals(value.AssignedAction, weapon));
		slot.TryPerformAction(player);
		AssertBool(slot.IsPerformingAction || slot.IsDelayed || slot.IsOnCooldown).IsFalse();
		AssertFloat(slot.RemainingTime).IsEqual(0);
		AssertBool(player.AttackController.IsAttacking).IsFalse();
	}

	[TestCase]
	public async Task PlaygroundSelectionAndButtonUseTheRealActionSlotTimeline()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		AssertInt(harness.Weapons.Length).IsEqual(13);
		AssertObject(harness.GetNode<Label>("%Controls/Status")).IsNotNull();
		harness.EquipPreview(3);
		harness.Attack();
		var controller = harness.Player.AttackController;
		AssertBool(harness.Player.ActionManager.IsBusy).IsTrue();
		AssertString(controller.CurrentStyle).IsEqual("spear.2h.thrust");
		controller.SetPhysicsProcess(false);
		controller._PhysicsProcess(controller.PerformDuration + .01);
		await runner.SimulateFrames(2);
		AssertBool(controller.IsAttacking).IsFalse();
		AssertBool(harness.Player.ActionManager.IsBusy).IsFalse();
	}

	[TestCase]
	public async Task HitDuringCrossbowPreparationCannotFireAfterKnockbackEnds()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.EquipPreview(6);
		harness.Attack();
		var slot = harness.Player.ActionManager.GetChildren().OfType<ActionSlot>().First(value => value.IsDelayed);
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		harness.Player.MovementComponent.Push(Vector3.Right, 1);
		slot._PhysicsProcess(.01);
		AssertBool(harness.Player.ActionManager.IsBusy).IsFalse();
		await runner.SimulateFrames(30);
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		AssertBool(slot.IsDelayed || slot.IsPerformingAction).IsFalse();
	}

	[TestCase]
	public void EveryWeaponExplicitlyRequestsASupportedStyle()
	{
		var roots = Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/items/weapons"), "*.tres", SearchOption.AllDirectories)
			.Select(path => TestFactories.Load<Resource>("res://" + Path.GetRelativePath(ProjectSettings.GlobalizePath("res://"), path))).ToArray();
		var weapons = roots.OfType<Weapon>().ToArray();
		AssertInt(weapons.Length).IsGreater(50);
		foreach (var weapon in weapons)
		{
			AssertBool(_profile.TryGetBinding(weapon.AttackStyle, out _)).OverrideFailureMessage(weapon.ResourcePath).IsTrue();
			AssertObject(weapon.Grip).IsNotNull();
			if (weapon.CustomAttackDefinition is { } definition && !string.IsNullOrEmpty(definition.AttackStyle))
				AssertBool(_profile.TryGetBinding(definition.AttackStyle, out _)).IsTrue();
		}
		GC.KeepAlive(roots);
	}
}