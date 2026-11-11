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

	/// <summary>
	/// Every class model plays every bound clip, including the dual-wield style a one-handed swing turns into, and the
	/// melee and crossbow grips sit where the models hold their own weapons.
	/// </summary>
	[TestCase]
	public void EveryPlayerRigHasEveryAuthoredStyleClip()
	{
		AssertInt(_profile.Bindings.Length).IsGreater(10);
		AssertBool(_profile.Bindings.Any(binding => !string.IsNullOrEmpty(binding.DualWieldStyle))).IsTrue();
		foreach (var binding in _profile.Bindings)
		{
			AssertBool(binding.IsValid).OverrideFailureMessage(binding.Style).IsTrue();
			if (!string.IsNullOrEmpty(binding.DualWieldStyle))
				AssertBool(_profile.TryGetBinding(binding.DualWieldStyle, out var dual) && dual.IsValid)
					.OverrideFailureMessage($"{binding.Style}: no valid '{binding.DualWieldStyle}' binding").IsTrue();
		}
		var meleeGrip = TestFactories.Load<WeaponGrip>("res://scenes/items/grips/kaykit_melee.tres");
		var crossbowGrip = TestFactories.Load<WeaponGrip>("res://scenes/items/grips/kaykit_crossbow.tres");
		foreach (string id in new[] { "barbarian", "knight", "rogue", "mage" })
		{
			var character = TestFactories.Load<CharacterClass>($"res://scenes/player/classes/class_{id}.tres");
			var model = AutoFree(character.CharacterScene.Instantiate<Node3D>());
			var animations = model.GetNode<AnimationPlayer>("AnimationPlayer");
			var skeleton = CharacterModel.FindSkeleton(model);
			AssertBool(skeleton.FindBone("handslot.r") >= 0).IsTrue();
			foreach (var binding in _profile.Bindings)
			{
				AssertBool(animations.HasAnimation(binding.Clip)).OverrideFailureMessage($"{id}: missing {binding.Clip}").IsTrue();
				AssertBool(animations.GetAnimation(binding.Clip).Length > 0).IsTrue();
			}
			if (id == "barbarian" || id == "rogue")
			{
				var grip = id == "rogue" ? crossbowGrip : meleeGrip;
				var native = skeleton.GetBoneRest(skeleton.FindBone(id == "rogue" ? "2H_Crossbow" : "1H_Axe"));
				AssertBool(grip.Transform.IsEqualApprox(native)).OverrideFailureMessage($"{id}: grip is off its native weapon").IsTrue();
			}
		}
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
		using var runner = TestFactories.LoadCombatHarness();
		var controller = (await TestFactories.SettleCombatHarness(runner)).Player.AttackController;
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

	/// <summary>The attack clip poses the rig from the attack's own clock, through recovery, and a cancel ends both at once.</summary>
	[TestCase]
	public async Task PlaybackAndExecutorShareRecoveryAndCancellation()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var player = (await TestFactories.SettleCombatHarness(runner)).Player;
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
		controller._PhysicsProcess(.6);
		AssertBool(controller.IsAttacking).IsTrue();
		controller.CancelAttack();
		AssertBool(animation.IsPlaying).IsFalse();
		controller._PhysicsProcess(1);
		AssertBool(controller.IsAttacking).IsFalse();
	}

	[TestCase]
	public async Task MissingWindUpClipRejectsPreparationBeforeAcquiringTheActionGate()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var shooter = TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 6);
		var component = shooter.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		component.Profile = (AttackAnimationProfile)component.Profile.Duplicate(true);
		component.Profile.Bindings[0].WindUpClip = "missing_aim";
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsFalse();
		AssertBool(shooter.ActionGate.IsBusy).IsFalse();
		AssertBool(shooter.AttackController.IsAttacking).IsFalse();
	}

	[TestCase]
	public async Task EquipmentSwitchCancelsTheAcceptedSwing()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var player = (await TestFactories.SettleCombatHarness(runner)).Player;
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
		using var runner = TestFactories.LoadCombatHarness();
		var player = (await TestFactories.SettleCombatHarness(runner)).Player;
		var weapon = (Weapon)player.Inventory.EquippedItems[EquipmentSlot.WeaponHand].Item;
		weapon.AttackStyle = "missing.style";
		var slot = player.ActionManager.GetChildren().OfType<ActionSlot>().First(value => ReferenceEquals(value.AssignedAction, weapon));
		slot.TryPerformAction(player);
		AssertBool(slot.IsPerformingAction || slot.IsDelayed || slot.IsOnCooldown).IsFalse();
		AssertFloat(slot.RemainingTime).IsEqual(0);
		AssertBool(player.AttackController.IsAttacking).IsFalse();
	}

	[TestCase]
	public async Task HitDuringCrossbowPreparationCannotFireAfterKnockbackEnds()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		AssertBool(harness.ExecuteCommand("equip", "res://scenes/items/weapons/crossbow_common.tres")).IsTrue();
		harness.Attack();
		var slot = harness.Player.ActionManager.GetChildren().OfType<ActionSlot>().First(value => value.IsDelayed);
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		harness.Player.MovementComponent.Push(Vector3.Right, 1);
		slot._PhysicsProcess(.01);
		AssertBool(harness.Player.ActionManager.IsBusy).IsFalse();
		await TestFactories.AwaitPhysicsUntil(runner, () => !harness.Player.MovementComponent.IsPushed, 2);
		await TestFactories.AwaitPhysicsFrames(runner, 3);
		AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
		AssertBool(slot.IsDelayed || slot.IsPerformingAction).IsFalse();
	}

	/// <summary>
	/// Every weapon's primary, custom and secondary attack plays a style the player rig binds, a melee weapon's secondary
	/// has a valid contact, and swing weapons carry the blade markers their trail follows.
	/// </summary>
	[TestCase]
	public void EveryWeaponPlaysASupportedStyleAndSwingWeaponsExposeBladeMarkers()
	{
		var roots = Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/items/weapons"), "*.tres", SearchOption.AllDirectories)
			.Select(path => TestFactories.Load<Resource>("res://" + Path.GetRelativePath(ProjectSettings.GlobalizePath("res://"), path))).ToArray();
		var weapons = roots.OfType<Weapon>().ToArray();
		AssertInt(weapons.Length).IsGreater(50);
		foreach (var weapon in weapons)
		{
			string path = weapon.ResourcePath;
			AssertBool(_profile.TryGetBinding(weapon.AttackStyle, out _)).OverrideFailureMessage(path).IsTrue();
			AssertObject(weapon.Grip).OverrideFailureMessage(path).IsNotNull();
			if (weapon.CustomAttackDefinition is { } definition && !string.IsNullOrEmpty(definition.AttackStyle))
				AssertBool(_profile.TryGetBinding(definition.AttackStyle, out _)).OverrideFailureMessage(path).IsTrue();
			AssertBool(_profile.TryGetBinding(weapon.GetEffectiveSecondaryStyle(), out _))
				.OverrideFailureMessage($"{path}: secondary '{weapon.GetEffectiveSecondaryStyle()}'").IsTrue();
			if (weapon is not RangedWeapon)
				AssertBool(weapon.GetEffectiveSecondaryMeleeContact()?.IsValid == true).OverrideFailureMessage($"{path}: secondary contact").IsTrue();

			string style = weapon.AttackStyle.ToString();
			if (!style.EndsWith(".slash") && !style.EndsWith(".chop")) continue;
			var visual = AutoFree(weapon.Scene.Instantiate<Node3D>());
			var bladeBase = visual.FindChild("TrailBase", true, false) as Marker3D;
			var tip = visual.FindChild("TrailTip", true, false) as Marker3D;
			AssertObject(bladeBase).OverrideFailureMessage(path).IsNotNull();
			AssertObject(tip).OverrideFailureMessage(path).IsNotNull();
			AssertBool(tip.Position.DistanceTo(bladeBase.Position) > .05f).OverrideFailureMessage(path).IsTrue();
		}
		GC.KeepAlive(roots);
	}
}
