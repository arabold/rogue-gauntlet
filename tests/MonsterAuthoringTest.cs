namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Exercise actual authored scenes, defaults before readiness and independent runtime state.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class MonsterAuthoringTest
{
	/// <summary>Deliberately unfinished assemblies (the inherited base and the rig-agnostic template, see templates/README.md).</summary>
	private static readonly string[] UnfinishedScenes =
	[
		"res://scenes/enemies/enemy_base.tscn",
		"res://scenes/enemies/templates/mobile_enemy.tscn",
	];

	/// <summary>Every monster scene under scenes/enemies, templates included, validates; a new monster is covered automatically.</summary>
	[TestCase]
	public void EveryAuthoredMonsterHasACompleteDefinition()
	{
		string root = ProjectSettings.GlobalizePath("res://");
		var validated = new List<string>();
		var errors = new List<string>();
		foreach (string file in Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/enemies"), "*.tscn", SearchOption.AllDirectories))
		{
			string path = "res://" + Path.GetRelativePath(root, file).Replace('\\', '/');
			if (UnfinishedScenes.Contains(path)) continue;
			var actor = TestFactories.Load<PackedScene>(path).Instantiate<Node>();
			try
			{
				if (actor is not EnemyBase) continue;
				validated.Add(path);
				errors.AddRange(MonsterSceneValidator.Validate(actor).Select(error => $"{path}: {error}"));
			}
			finally { actor.Free(); }
		}
		AssertArray(validated).Contains(TestFactories.MinionScene, TestFactories.WardenScene);
		AssertString(string.Join("\n", errors)).IsEmpty();
	}

	/// <summary>A spawned monster's definition applies before its children are ready, and its runtime state is its own.</summary>
	[TestCase]
	public void DefinitionInitializesBeforeChildReadyAndDoesNotShareHealth()
	{
		var scene = TestFactories.Load<PackedScene>(TestFactories.MinionScene);
		var first = TestFactories.AddToSceneTree(scene.Instantiate<EnemyBase>());
		float authoredHealth = first.Definition.MaxHealth;
		var second = scene.Instantiate<EnemyBase>();
		var custom = (MonsterDefinition)first.Definition.Duplicate();
		custom.MaxHealth = 37;
		custom.MoveSpeed = 1.7f;
		second.Definition = custom;
		float healthAtReady = 0;
		second.HealthComponent.Ready += () => healthAtReady = second.HealthComponent.CurrentHealth;
		TestFactories.AddToSceneTree(second);

		AssertBool(first.DefinitionValid && second.DefinitionValid).IsTrue();
		AssertFloat(healthAtReady).IsEqual(37);
		AssertFloat(second.MovementComponent.Speed).IsEqual(1.7f);
		second.HealthComponent.TakeDamage(3);
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(34);
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(authoredHealth);
		AssertFloat(first.Definition.MaxHealth).IsEqual(authoredHealth);
	}

	/// <summary>A guard the defense policy uses must stay wired; removing it is valid only once the policy stops using it.</summary>
	[TestCase]
	public void AGuardThePolicyUsesMustStayWiredUntilThePolicyDropsIt()
	{
		var actor = TestFactories.Load<PackedScene>(TestFactories.WarriorScene).Instantiate<EnemyBase>();
		try
		{
			var guard = actor.GuardComponent;
			// The guard node still exists, but the controller no longer reaches it.
			actor.DefenseController.GuardComponent = null;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(error => error.StartsWith("DefenseController.GuardComponent:"))).IsTrue();
			actor.RemoveChild(guard);
			actor.GuardComponent = null;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("GuardComponent:") || e.StartsWith("DefenseController.GuardComponent:"))).IsTrue();
			TestFactories.UsePrivateCombatProfile(actor);
			actor.Definition.Behavior.Combat.DefenseStrategy = null;
			AssertString(string.Join("; ", MonsterSceneValidator.Validate(actor))).IsEmpty();
			guard.Free();
		}
		finally { actor.Free(); }
	}

	[TestCase]
	public void PoiseMustBelongToTheActorAndBindItsGate()
	{
		var actor = TestFactories.Load<PackedScene>(TestFactories.MinionScene).Instantiate<EnemyBase>();
		var stranger = new PoiseComponent { Definition = new PoiseDefinition() };
		try
		{
			actor.HurtBoxComponent.Poise.ActionGate = null;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("HurtBoxComponent.Poise:"))).IsTrue();
			actor.HurtBoxComponent.Poise = stranger;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("HurtBoxComponent.Poise:"))).IsTrue();
			actor.HurtBoxComponent.Poise = null;
			AssertString(string.Join("; ", MonsterSceneValidator.Validate(actor))).IsEmpty();
		}
		finally { actor.Free(); stranger.Free(); }
	}

	[TestCase]
	public void ThreatTierNeedsASharedIndicatorAndATierLengthWindUp()
	{
		var minion = TestFactories.Load<PackedScene>(TestFactories.MinionScene).Instantiate<EnemyBase>();
		try
		{
			// The minion's 0.52 s chop wind-up covers Normal and Heavy but is too short for Unblockable (dodge only).
			var definition = (MonsterDefinition)minion.Definition.Duplicate();
			definition.Behavior = (EnemyBehaviorProfile)definition.Behavior.Duplicate();
			definition.Behavior.Combat = (EnemyCombatProfile)definition.Behavior.Combat.Duplicate();
			var oldAction = (MonsterAttackAction)definition.Behavior.Combat.PrimaryOffense;
			var attackAction = (MonsterAttackAction)oldAction.Duplicate();
			var attack = (AttackDefinition)oldAction.Attack.Duplicate();
			attack.DefenseTraits = AttackDefenseTraits.Dodgeable;
			attackAction.Attack = attack;
			definition.Behavior.Combat.Actions = [attackAction];
			minion.Definition = definition;
			minion.AttackController.Indicator = null;
			var errors = MonsterSceneValidator.Validate(minion);
			AssertBool(errors.Any(e => e.Contains("Attack: Unblockable attacks need"))).IsTrue();
			AssertBool(errors.Any(e => e.Contains("AttackController.Indicator:"))).IsTrue();
		}
		finally { minion.Free(); }
	}

	[TestCase]
	public void OverlappingVulnerabilityArcsAreReported()
	{
		var actor = TestFactories.Load<PackedScene>(TestFactories.MinionScene).Instantiate<EnemyBase>();
		try
		{
			actor.HurtBoxComponent.Vulnerability = new HitVulnerabilityProfile { FrontHalfAngle = 120, RearHalfAngle = 90 };
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("HurtBoxComponent.Vulnerability:"))).IsTrue();
		}
		finally { actor.Free(); }
	}

	/// <summary>
	/// Bad numbers, a missing gate and an animation profile that cannot resolve (one broken binding rejects every style)
	/// are each reported by name.
	/// </summary>
	[TestCase]
	public void InvalidNumbersMissingGateAndUnresolvableClipsProduceActionableErrors()
	{
		var actor = TestFactories.Load<PackedScene>(TestFactories.MinionScene).Instantiate<EnemyBase>();
		try
		{
			actor.Definition = (MonsterDefinition)actor.Definition.Duplicate();
			actor.Definition.MaxHealth = float.NaN;
			actor.ActionGate = null;
			var animation = actor.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
			var profile = (AttackAnimationProfile)animation.Profile.Duplicate();
			profile.Bindings = [.. profile.Bindings, new AttackAnimationBinding { Style = "broken", Clip = "Idle", ContactStart = .8f, ContactEnd = .2f }];
			animation.Profile = profile;
			var errors = MonsterSceneValidator.Validate(actor);
			AssertBool(errors.Any(e => e.StartsWith("CombatAnimationComponent.Profile:"))).IsTrue();
			AssertBool(errors.Any(e => e.StartsWith("Definition.MaxHealth:"))).IsTrue();
			AssertBool(errors.Any(e => e.StartsWith("ActionGate:"))).IsTrue();
		}
		finally { actor.Free(); }
	}

	/// <summary>An unassigned defense export disables that defense, whether it was cleared before or after readiness.</summary>
	[TestCase]
	public void UnassignedControllerCapabilitiesStayUnavailable()
	{
		var actor = TestFactories.Load<PackedScene>(TestFactories.WarriorScene).Instantiate<EnemyBase>();
		TestFactories.UsePrivateCombatProfile(actor).DefenseStrategy = null;
		actor.EnemyBehaviorComponent.AIEnabled = false;
		var controller = actor.DefenseController;
		var guard = actor.GuardComponent;
		controller.GuardComponent = null;
		controller.DodgeComponent = null;
		TestFactories.AddToSceneTree(actor);
		AssertBool(actor.DefinitionValid).IsTrue();
		controller._PhysicsProcess(.1);
		AssertBool(controller.RequestGuard(Vector3.Forward)).IsFalse();
		AssertBool(controller.RequestDodge(Vector3.Right)).IsFalse();

		// Assigned after readiness the guard works; cleared again, the controller keeps no stale reference.
		controller.GuardComponent = guard;
		AssertBool(controller.RequestGuard(Vector3.Forward)).IsTrue();
		guard.EndGuard(immediate: true);
		controller.GuardComponent = null;
		controller._PhysicsProcess(.1);
		AssertBool(controller.RequestGuard(Vector3.Forward)).IsFalse();
	}
}
