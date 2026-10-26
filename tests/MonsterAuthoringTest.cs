namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Exercise actual authored scenes, defaults before readiness and independent runtime state.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class MonsterAuthoringTest
{
	[TestCase("skeleton/skeleton_minion")]
	[TestCase("skeleton/skeleton_warrior")]
	[TestCase("templates/melee_enemy")]
	[TestCase("templates/projectile_enemy")]
	[TestCase("skeleton/skeleton_crossbow")]
	[TestCase("rat/rat")]
	[TestCase("forsaken_warden/forsaken_warden")]
	public void AuthoredExampleHasCompleteDefinition(string path)
	{
		var actor = GD.Load<PackedScene>($"res://scenes/enemies/{path}.tscn").Instantiate<EnemyBase>();
		try { AssertString(string.Join("; ", MonsterSceneValidator.Validate(actor))).IsEmpty(); }
		finally { actor.Free(); }
	}

	[TestCase]
	public async Task DefinitionInitializesBeforeChildReadyAndDoesNotShareHealth()
	{
		using var runner = ISceneRunner.Load("res://scenes/enemies/skeleton/skeleton_minion.tscn", true);
		await runner.SimulateFrames(2);
		var first = (EnemyBase)runner.Scene();
		var scene = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn");
		var second = scene.Instantiate<EnemyBase>();
		var custom = (MonsterDefinition)first.Definition.Duplicate();
		custom.MaxHealth = 37;
		custom.MoveSpeed = 1.7f;
		custom.XpReward = 19;
		second.Definition = custom;
		float healthAtReady = 0;
		second.HealthComponent.Ready += () => healthAtReady = second.HealthComponent.CurrentHealth;
		first.AddSibling(second);
		await runner.SimulateFrames(2);
		try
		{
			AssertBool(first.DefinitionValid && second.DefinitionValid).IsTrue();
			AssertFloat(healthAtReady).IsEqual(37);
			AssertFloat(second.MovementComponent.Speed).IsEqual(1.7f);
			AssertInt(second.DeathComponent.Xp).IsEqual(19);
			AssertBool(ReferenceEquals(second.EnemyBehaviorComponent.Profile, custom.Behavior)).IsTrue();
			AssertBool(ReferenceEquals(second.LootTableComponent.Profile, custom.Loot)).IsTrue();
			second.HealthComponent.TakeDamage(3);
			AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(34);
			AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(8);
			AssertFloat(first.Definition.MaxHealth).IsEqual(8);
			AssertFloat(custom.MaxHealth).IsEqual(37);
		}
		finally { second.QueueFree(); }
	}

	[TestCase]
	public void RemovingGuardRequiresChangingPolicyButDoesNotRepairScene()
	{
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_warrior.tscn").Instantiate<EnemyBase>();
		try
		{
			var guard = actor.GuardComponent;
			actor.RemoveChild(guard);
			actor.GuardComponent = null;
			actor.DefenseController.GuardComponent = null;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("GuardComponent:") || e.StartsWith("DefenseController.GuardComponent:"))).IsTrue();
			AssertObject(actor.GuardComponent).IsNull();
			actor.Definition = (MonsterDefinition)actor.Definition.Duplicate();
			actor.Definition.Behavior = (EnemyBehaviorProfile)actor.Definition.Behavior.Duplicate();
			actor.Definition.Behavior.DefenseStrategy = null;
			AssertString(string.Join("; ", MonsterSceneValidator.Validate(actor))).IsEmpty();
			guard.Free();
		}
		finally { actor.Free(); }
	}

	[TestCase]
	public void InvalidNumbersAndMissingGateProduceActionableErrors()
	{
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn").Instantiate<EnemyBase>();
		try
		{
			actor.Definition = (MonsterDefinition)actor.Definition.Duplicate();
			actor.Definition.MaxHealth = float.NaN;
			actor.ActionGate = null;
			var errors = MonsterSceneValidator.Validate(actor);
			AssertBool(errors.Any(e => e.StartsWith("Definition.MaxHealth:"))).IsTrue();
			AssertBool(errors.Any(e => e.StartsWith("ActionGate:"))).IsTrue();
		}
		finally { actor.Free(); }
	}

	[TestCase]
	public void MissingControllerAssignmentIsReportedEvenWhenGuardNodeExists()
	{
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_warrior.tscn").Instantiate<EnemyBase>();
		try
		{
			actor.DefenseController.GuardComponent = null;
			AssertObject(actor.GuardComponent).IsNotNull();
			AssertBool(MonsterSceneValidator.Validate(actor).Any(error => error.StartsWith("DefenseController.GuardComponent:"))).IsTrue();
			AssertObject(actor.DefenseController.GuardComponent).IsNull();
		}
		finally { actor.Free(); }
	}

	[TestCase]
	public void ExplicitDefenseReferencesDoNotRequireConventionalNames()
	{
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_warrior.tscn").Instantiate<EnemyBase>();
		try
		{
			actor.GuardComponent.Name = "ShieldGuard";
			actor.DodgeComponent.Name = "Evade";
			actor.DefenseController.Name = "DefensePolicy";
			actor.AttackController.Name = "WeaponAttack";
			AssertString(string.Join("; ", MonsterSceneValidator.Validate(actor))).IsEmpty();
			AssertBool(ReferenceEquals(actor.DefenseController.GuardComponent, actor.GuardComponent)).IsTrue();
			AssertBool(ReferenceEquals(actor.DefenseController.DodgeComponent, actor.DodgeComponent)).IsTrue();
		}
		finally { actor.Free(); }
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task UnassignedControllerCapabilitiesStayUnavailable(bool clearAfterReady)
	{
		using var runner = ISceneRunner.Load("res://scenes/enemies/skeleton/skeleton_minion.tscn", true);
		await runner.SimulateFrames(2);
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_warrior.tscn").Instantiate<EnemyBase>();
		actor.Definition = (MonsterDefinition)actor.Definition.Duplicate();
		actor.Definition.Behavior = (EnemyBehaviorProfile)actor.Definition.Behavior.Duplicate();
		actor.Definition.Behavior.DefenseStrategy = null;
		actor.EnemyBehaviorComponent.AIEnabled = false;
		var controller = actor.DefenseController;
		if (!clearAfterReady) { controller.GuardComponent = null; controller.DodgeComponent = null; }
		runner.Scene().AddSibling(actor);
		try
		{
			await runner.SimulateFrames(2);
			if (clearAfterReady) { controller.GuardComponent = null; controller.DodgeComponent = null; }
			controller._PhysicsProcess(.1);
			AssertBool(actor.DefinitionValid).IsTrue();
			AssertObject(actor.GuardComponent).IsNotNull();
			AssertObject(actor.DodgeComponent).IsNotNull();
			AssertObject(controller.GuardComponent).IsNull();
			AssertObject(controller.DodgeComponent).IsNull();
			AssertBool(controller.RequestGuard(Vector3.Forward)).IsFalse();
			AssertBool(controller.RequestDodge(Vector3.Right)).IsFalse();
		}
		finally { actor.QueueFree(); }
	}

	[TestCase]
	public void DefenseIntervalsAreExclusiveAndUnavailableActionsDoNotReroll()
	{
		using var strategy = new EnemyDefenseStrategy { DodgeChance = .2f, GuardChance = .5f };
		AssertInt((int)strategy.Choose(true, true, .1f)).IsEqual((int)EnemyDefenseMode.Dodge);
		AssertInt((int)strategy.Choose(true, true, .2f)).IsEqual((int)EnemyDefenseMode.GuardParry);
		AssertInt((int)strategy.Choose(true, true, .7f)).IsEqual((int)EnemyDefenseMode.None);
		AssertInt((int)strategy.Choose(true, false, .1f)).IsEqual((int)EnemyDefenseMode.None);
		AssertInt((int)strategy.Choose(false, true, .3f)).IsEqual((int)EnemyDefenseMode.None);
	}
}