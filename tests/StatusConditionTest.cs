namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests <see cref="BuffController"/>'s status conditions, cures and change notifications, and the authored
/// identity of <see cref="StatusCondition"/>s. Needs the Godot runtime: the controller is a Node and
/// buffs/conditions are Resources. Expiry is driven by calling the physics callbacks directly, as in
/// <see cref="ActiveBuffTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class StatusConditionTest
{
	/// <summary>A buff that only counts its removals; ignores the (null) player.</summary>
	private sealed partial class CountingBuff : Buff
	{
		public int RemoveCount;

		public override void OnRemove(ActiveBuff activation) => RemoveCount++;
	}

	private static (BuffController Controller, System.Func<int> Changes) NewController()
	{
		var controller = AutoFree(new BuffController());
		int changes = 0;
		controller.BuffsChanged += () => changes++;
		return (controller, () => changes);
	}

	[TestCase]
	public void OverlappingBuffsReadAsOneContinuousCondition()
	{
		(BuffController controller, var changes) = NewController();
		var poisoned = new StatusCondition();
		var food = new CountingBuff { Name = "Food poisoning", Duration = 6f, Condition = poisoned };
		var potion = new CountingBuff { Name = "Poison", Duration = 6f, Condition = poisoned };

		controller.ApplyBuff(food);
		controller.ApplyBuff(potion);
		AssertBool(controller.HasCondition(poisoned)).IsTrue();

		controller.RemoveBuff(food);
		AssertBool(controller.HasCondition(poisoned)).IsTrue();

		controller.RemoveBuff(potion);
		AssertBool(controller.HasCondition(poisoned)).IsFalse();
		AssertInt(changes()).IsEqual(4);
	}

	[TestCase]
	public void ExpiryEndsTheConditionAfterTheBuffIsReversed()
	{
		(BuffController controller, var changes) = NewController();
		var poisoned = new StatusCondition();
		var buff = new CountingBuff { Name = "Poison", Duration = 1f, Condition = poisoned };

		controller.ApplyBuff(buff);
		controller.ActiveBuffs[0]._PhysicsProcess(2.0); // expires and reverses itself
		controller._PhysicsProcess(0.0);                // the controller's sweep removes it

		AssertInt(buff.RemoveCount).IsEqual(1);
		AssertBool(controller.HasCondition(poisoned)).IsFalse();
		AssertInt(controller.ActiveBuffs.Count).IsEqual(0);
		AssertInt(changes()).IsEqual(2);
	}

	[TestCase]
	public void ClearBuffsReversesEveryBuffOnceWithOneNotification()
	{
		(BuffController controller, var changes) = NewController();
		var poisoned = new StatusCondition();
		var buffs = new[]
		{
			new CountingBuff { Name = "Poison", Duration = 6f, Condition = poisoned },
			new CountingBuff { Name = "Blindness", Duration = 0f, Condition = new StatusCondition() },
			new CountingBuff { Name = "Haste", Duration = 6f },
		};
		foreach (CountingBuff buff in buffs)
		{
			controller.ApplyBuff(buff);
		}

		controller.ClearBuffs();

		AssertInt(controller.ActiveBuffs.Count).IsEqual(0);
		AssertArray(buffs.Select(buff => buff.RemoveCount).ToArray()).ContainsExactly(1, 1, 1);
		AssertBool(controller.HasCondition(poisoned)).IsFalse();
		AssertInt(changes()).IsEqual(buffs.Length + 1);
	}

	[TestCase]
	public void CuredConditionsEndEveryBuffImposingThemAndNothingElse()
	{
		(BuffController controller, var changes) = NewController();
		var blinded = new StatusCondition();
		var poisoned = new StatusCondition();
		var blindness = new CountingBuff { Name = "Blindness", Duration = 5f, Condition = blinded };
		controller.ApplyBuff(blindness);
		controller.ApplyBuff(new CountingBuff { Name = "Blindness", Duration = 9f, Condition = blinded });
		controller.ApplyBuff(new CountingBuff { Name = "Poison", Duration = 5f, Condition = poisoned });

		controller.ApplyBuff(new CountingBuff { Name = "Healing", Duration = 3f, CuredConditions = [blinded] });

		AssertBool(controller.HasCondition(blinded)).IsFalse();
		AssertBool(controller.HasCondition(poisoned)).IsTrue();
		AssertInt(blindness.RemoveCount).IsEqual(1);
		AssertInt(controller.ActiveBuffs.Count).IsEqual(2);
		AssertInt(changes()).IsEqual(4);
	}

	[TestCase]
	public void BuffWithoutConditionImposesNone()
	{
		(BuffController controller, _) = NewController();
		controller.ApplyBuff(new CountingBuff { Name = "Haste", Duration = 6f });

		AssertBool(controller.HasCondition(null)).IsFalse();
		AssertBool(controller.HasCondition(new StatusCondition())).IsFalse();
	}

	[TestCase]
	public void ConditionIdentitySurvivesItemCreation()
	{
		// ItemFactory deep-duplicates definitions, so each item gets its own copy of an embedded buff. The
		// condition is an external resource and must stay the shared instance that drivers compare against.
		var poisoned = GD.Load<StatusCondition>("res://scenes/buffs/conditions/poisoned.tres");
		var definition = GD.Load<BuffedItem>("res://scenes/items/potions/potion_poison_small.tres");
		var potion = (BuffedItem)ItemFactory.Create(definition);
		AssertObject(potion.Buff).IsNotSame(definition.Buff);
		AssertObject(potion.Buff.Condition).IsSame(poisoned);

		var apple = (FoodItem)ItemFactory.Create(GD.Load<Item>("res://scenes/items/food/food/food_apple_red.tres"));
		AssertObject(apple.PoisonEffect.Condition).IsSame(poisoned);
	}

	[TestCase]
	public void AStatusLastsUntilItsLastSourceReleasesIt()
	{
		(BuffController controller, _) = NewController();
		var condition = new StatusCondition();
		object first = new(), second = new();
		AssertBool(controller.ApplyStatus(condition, first, 5)).IsTrue();
		AssertBool(controller.ApplyStatus(condition, first, 8)).IsTrue();
		AssertInt(controller.ActiveBuffs.Count).IsEqual(1);
		AssertFloat(controller.ActiveBuffs[0].RemainingDuration).IsEqual(8);
		AssertBool(controller.ApplyStatus(condition, second, 3)).IsTrue();
		AssertInt(controller.ActiveBuffs.Count).IsEqual(2);

		controller.RemoveStatuses(first);
		AssertBool(controller.HasStatusFrom(first)).IsFalse();
		AssertBool(controller.HasCondition(condition)).IsTrue();
		controller.RemoveStatuses(second);
		AssertBool(controller.HasCondition(condition)).IsFalse();
	}

	[TestCase]
	public void ReapplicationAfterExpiryCreatesALiveActivation()
	{
		(BuffController controller, _) = NewController();
		var condition = new StatusCondition();
		var source = new object();
		AssertBool(controller.ApplyStatus(condition, source, 1)).IsTrue();
		controller.ActiveBuffs[0]._PhysicsProcess(2);
		AssertBool(controller.ApplyStatus(condition, source, 5)).IsTrue();
		controller._PhysicsProcess(0);
		AssertInt(controller.ActiveBuffs.Count).IsEqual(1);
		AssertBool(controller.HasCondition(condition)).IsTrue();
		controller.ActiveBuffs[0]._PhysicsProcess(6);
		controller._PhysicsProcess(0);
		AssertBool(controller.HasCondition(condition)).IsFalse();
	}

	[TestCase]
	public void ASourceCanSwitchBetweenTimedAndPersistentStatus()
	{
		(BuffController controller, _) = NewController();
		var condition = new StatusCondition(); var source = new object();
		AssertBool(controller.ApplyStatus(condition, source, 1)).IsTrue();
		AssertBool(controller.SetPersistentStatus(condition, source, true)).IsTrue();
		AssertBool(controller.ActiveBuffs[0].IsPermanent).IsTrue();
		controller.ActiveBuffs[0]._PhysicsProcess(2);
		AssertBool(controller.HasCondition(condition)).IsTrue();
		AssertBool(controller.ApplyStatus(condition, source, 1)).IsTrue();
		AssertBool(controller.ActiveBuffs[0].IsPermanent).IsFalse();
		controller.ActiveBuffs[0]._PhysicsProcess(2); controller._PhysicsProcess(0);
		AssertBool(controller.HasCondition(condition)).IsFalse();
	}

	[TestCase]
	public void IdleStatusProcessingTracksTimedAndPersistentActivations()
	{
		var controller = TestFactories.AddToSceneTree(new BuffController());
		var condition = new StatusCondition(); var source = new object();
		AssertBool(controller.IsPhysicsProcessing()).IsFalse();
		controller.SetPersistentStatus(condition, source, true);
		var active = controller.ActiveBuffs[0];
		AssertBool(controller.IsPhysicsProcessing()).IsFalse();
		AssertBool(active.IsPhysicsProcessing()).IsFalse();
		controller.ApplyStatus(condition, source, 1);
		AssertBool(controller.IsPhysicsProcessing()).IsTrue();
		AssertBool(active.IsPhysicsProcessing()).IsTrue();
		controller.SetPersistentStatus(condition, source, true);
		AssertBool(controller.IsPhysicsProcessing()).IsFalse();
		AssertBool(active.IsPhysicsProcessing()).IsFalse();
		controller.ApplyStatus(condition, source, 1);
		active._PhysicsProcess(2); controller._PhysicsProcess(0);
		AssertBool(controller.HasCondition(condition)).IsFalse();
		AssertBool(controller.IsPhysicsProcessing()).IsFalse();
	}
}
