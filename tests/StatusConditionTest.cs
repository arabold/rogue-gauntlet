namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests <see cref="BuffController"/>'s status-condition tracking and the authored identity of
/// <see cref="StatusCondition"/>s. Needs the Godot runtime: the controller is a Node and buffs/conditions are
/// Resources. Expiry is driven by calling the physics callbacks directly, as in <see cref="ActiveBuffTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class StatusConditionTest
{
	private const string PoisonedPath = "res://scenes/buffs/conditions/poisoned.tres";

	/// <summary>A buff that only counts its removals; ignores the (null) player.</summary>
	private sealed partial class CountingBuff : Buff
	{
		public int RemoveCount;

		public override void OnRemove(Player player) => RemoveCount++;
	}

	private sealed class ConditionLog
	{
		public readonly List<StatusCondition> Gained = [];
		public readonly List<StatusCondition> Lost = [];
		public readonly List<bool> HasConditionWhenLost = [];

		public ConditionLog(BuffController controller)
		{
			controller.ConditionGained += Gained.Add;
			controller.ConditionLost += condition =>
			{
				Lost.Add(condition);
				HasConditionWhenLost.Add(controller.HasCondition(condition));
			};
		}
	}

	[TestCase]
	public void OverlappingBuffsReportOneContinuousCondition()
	{
		var controller = AutoFree(new BuffController());
		var log = new ConditionLog(controller);
		var poisoned = new StatusCondition();
		var food = new CountingBuff { Name = "Food poisoning", Duration = 6f, Condition = poisoned };
		var potion = new CountingBuff { Name = "Poison", Duration = 6f, Condition = poisoned };

		controller.ApplyBuff(food);
		controller.ApplyBuff(potion);
		AssertInt(log.Gained.Count).IsEqual(1);
		AssertBool(controller.HasCondition(poisoned)).IsTrue();

		controller.RemoveBuff(food);
		AssertInt(log.Lost.Count).IsEqual(0);
		AssertBool(controller.HasCondition(poisoned)).IsTrue();

		controller.RemoveBuff(potion);
		AssertInt(log.Lost.Count).IsEqual(1);
		AssertObject(log.Lost[0]).IsSame(poisoned);
		AssertBool(log.HasConditionWhenLost[0]).IsFalse();
		AssertBool(controller.HasCondition(poisoned)).IsFalse();
	}

	[TestCase]
	public void ExpiryReportsTheConditionLostAfterTheBuffIsReversed()
	{
		var controller = AutoFree(new BuffController());
		var log = new ConditionLog(controller);
		var poisoned = new StatusCondition();
		var buff = new CountingBuff { Name = "Poison", Duration = 1f, Condition = poisoned };

		controller.ApplyBuff(buff);
		controller.ActiveBuffs[0]._PhysicsProcess(2.0); // expires and reverses itself
		AssertInt(log.Lost.Count).IsEqual(0);           // reported by the controller's removal sweep

		controller._PhysicsProcess(0.0);
		AssertInt(buff.RemoveCount).IsEqual(1);
		AssertInt(log.Lost.Count).IsEqual(1);
		AssertBool(controller.HasCondition(poisoned)).IsFalse();
		AssertInt(controller.ActiveBuffs.Count).IsEqual(0);
	}

	[TestCase]
	public void ClearBuffsReversesEveryBuffAndReportsEachCondition()
	{
		var controller = AutoFree(new BuffController());
		var log = new ConditionLog(controller);
		var poisoned = new StatusCondition();
		var blinded = new StatusCondition();
		var buffs = new[]
		{
			new CountingBuff { Name = "Poison", Duration = 6f, Condition = poisoned },
			new CountingBuff { Name = "Blindness", Duration = 0f, Condition = blinded },
			new CountingBuff { Name = "Haste", Duration = 6f },
		};
		foreach (CountingBuff buff in buffs)
		{
			controller.ApplyBuff(buff);
		}

		controller.ClearBuffs();

		AssertInt(controller.ActiveBuffs.Count).IsEqual(0);
		AssertArray(buffs.Select(buff => buff.RemoveCount).ToArray()).ContainsExactly(1, 1, 1);
		AssertInt(log.Lost.Count).IsEqual(2);
		AssertBool(controller.HasCondition(poisoned) || controller.HasCondition(blinded)).IsFalse();
	}

	[TestCase]
	public void BuffWithoutConditionReportsNothing()
	{
		var controller = AutoFree(new BuffController());
		var log = new ConditionLog(controller);
		var buff = new CountingBuff { Name = "Haste", Duration = 6f };

		controller.ApplyBuff(buff);
		controller.RemoveBuff(buff);

		AssertInt(log.Gained.Count + log.Lost.Count).IsEqual(0);
		AssertBool(controller.HasCondition(null)).IsFalse();
	}

	[TestCase]
	public void ConditionIdentitySurvivesItemCreation()
	{
		// ItemFactory deep-duplicates definitions, so each item gets its own copy of an embedded buff. The
		// condition is an external resource and must stay the shared instance that drivers compare against.
		var poisoned = GD.Load<StatusCondition>(PoisonedPath);
		var definition = GD.Load<BuffedItem>("res://scenes/items/potions/poison_potion.tres");
		var potion = (BuffedItem)ItemFactory.Create(definition);
		AssertObject(potion.Buff).IsNotSame(definition.Buff);
		AssertObject(potion.Buff.Condition).IsSame(poisoned);

		var apple = (FoodItem)ItemFactory.Create(GD.Load<Item>("res://scenes/items/food/food/food_apple_red.tres"));
		AssertObject(apple.PoisonEffect.Condition).IsSame(poisoned);
	}

	[TestCase]
	public void EveryAuthoredPoisonCarriesThePoisonedCondition()
	{
		// A new poison source authored without the condition would poison the player without the overlay.
		var poisoned = GD.Load<StatusCondition>(PoisonedPath);
		Item[] items = Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/items"), "*.tres", SearchOption.AllDirectories)
			.OrderBy(path => path, StringComparer.Ordinal)
			.Select(path => ResourceLoader.Load<Resource>(ProjectSettings.LocalizePath(path)))
			.OfType<Item>()
			.ToArray();
		var poisons = items.OfType<BuffedItem>().Select(item => (item.ResourcePath, Buff: item.Buff))
			.Concat(items.OfType<FoodItem>().Select(item => (item.ResourcePath, Buff: (Buff)item.PoisonEffect)))
			.Where(source => source.Buff is PoisonBuff)
			.ToArray();

		AssertInt(poisons.Length).IsGreaterEqual(5);
		AssertArray(poisons.Where(source => source.Buff.Condition != poisoned).Select(source => source.ResourcePath).ToArray()).IsEmpty();
	}
}