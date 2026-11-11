namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Every actor clip has one driver. Combat actions and staggers pose through <see cref="CombatAnimationComponent"/>,
/// and the player's and monsters' AnimationTrees play locomotion, item use, sleep and knockback. Once a pose hands
/// back, the tree resumes without replaying what the pose already showed, and one knockback plays one hit reaction.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CombatAnimationHandoffTest
{
	/// <summary>The tree's state at every evaluation, in order, with its play position.</summary>
	private sealed class TreeLog : IDisposable
	{
		private readonly AnimationTree _tree;
		private readonly AnimationNodeStateMachinePlayback _playback;
		public readonly List<(string State, double Position)> Samples = [];

		public TreeLog(AnimationTree tree)
		{
			_tree = tree;
			_playback = (AnimationNodeStateMachinePlayback)_tree.Get("parameters/playback");
			_tree.MixerApplied += Record;
		}

		private void Record() => Samples.Add((_playback.GetCurrentNode(), _playback.GetCurrentPlayPosition()));

		public string[] States => Samples.Select(sample => sample.State).Distinct().ToArray();

		public string Current => _playback.GetCurrentNode();

		/// <summary>How often <paramref name="state"/>'s clip started: entering the state, or its position jumping back.</summary>
		public int Starts(string state) => Samples.Where((sample, i) => sample.State == state
			&& (i == 0 || Samples[i - 1].State != state || sample.Position < Samples[i - 1].Position)).Count();

		public void Dispose()
		{
			if (GodotObject.IsInstanceValid(_tree)) _tree.MixerApplied -= Record;
		}
	}

	private const string Player = "player";

	private static AnimationTree Tree(Node actor) => actor.GetNode<AnimationTree>(actor is global::Player ? "AnimationTree" : "EnemyBehaviorComponent/AnimationTree");

	/// <summary>The harness player, or a harness monster standing in its idle pose.</summary>
	private static async Task<CharacterBody3D> IdleActor(ISceneRunner runner, string actor)
	{
		var harness = await TestFactories.SettleCombatHarness(runner);
		if (actor == Player)
		{
			await runner.SimulateFrames(20, 20);
			return harness.Player;
		}
		var monster = TestFactories.SpawnHarnessMonster(harness, actor);
		// Skip the spawn clip, a 3.5-second rise from the ground: these tests start from an idling monster.
		var playback = (AnimationNodeStateMachinePlayback)Tree(monster).Get("parameters/playback");
		playback.Start("Idle");
		await TestFactories.AwaitPhysicsUntil(runner, () => playback.GetCurrentNode() == "Idle", 1);
		await runner.SimulateFrames(5, 20);
		return monster;
	}

	/// <summary>A push that decays over about 1.2 seconds at the actor's own speed.</summary>
	private static MovementComponent PushLong(Node actor)
	{
		var movement = actor.GetNode<MovementComponent>("MovementComponent");
		movement.Push(Vector3.Right, movement.Speed * 1.2f);
		return movement;
	}

	/// <summary>An unarmed attack, a weapon technique and a spell each pose through the combat component, and the tree stays in Idle.</summary>
	[TestCase]
	public async Task CombatActionsLeaveTheTreeToLocomotion()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var player = harness.Player;
		var pose = player.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		await runner.SimulateFrames(10, 20);
		async Task Perform(string action, Func<bool> request)
		{
			await runner.SimulateFrames(10, 20); // Let the tree settle into Idle before logging.
			using var log = new TreeLog(Tree(player));
			AssertBool(request()).OverrideFailureMessage($"{action} was not accepted.").IsTrue();
			bool posed = false;
			string treeActionId = null;
			await TestFactories.AwaitPhysicsUntil(runner, () =>
			{
				posed |= pose.IsPlaying;
				treeActionId ??= player.CurrentActionId;
				return !player.IsPerformingAction;
			}, 3);
			await runner.SimulateFrames(10, 20);

			AssertBool(posed).OverrideFailureMessage($"The {action} never posed through the combat component.").IsTrue();
			AssertString(treeActionId).OverrideFailureMessage(action).IsNull();
			AssertArray(log.States).OverrideFailureMessage(action).ContainsExactly("Idle");
		}

		player.Inventory.Unequip(player.Inventory.EquippedItems[EquipmentSlot.WeaponHand]);
		await Perform("unarmed attack", player.ActionManager.TryPerformPrimary);
		AssertBool(harness.ExecuteCommand("equip", "res://scenes/items/weapons/sword_common.tres")).IsTrue();
		await Perform("technique", player.ActionManager.TryPerformSecondary);
		AssertBool(harness.ExecuteCommand("equip", "res://scenes/items/weapons/staff_common.tres")).IsTrue();
		await Perform("spell", player.ActionManager.TryPerformSecondary);
	}

	/// <summary>
	/// A knockback plays one hit reaction however long it lasts. A poise break staggers, then pushes, as a hurtbox reacts:
	/// the stagger pose owns the knockback it overlaps, so the tree never shows the reaction under it.
	/// </summary>
	[TestCase(Player)]
	[TestCase(TestFactories.MinionScene)]
	[TestCase(TestFactories.WardenScene)]
	public async Task KnockbackPlaysOneHitReactionAndAStaggerPoseOwnsTheKnockbackItOverlaps(string actor)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var body = await IdleActor(runner, actor);
		var tree = Tree(body);
		var clip = tree.GetNode<AnimationPlayer>(tree.AnimPlayer).GetAnimation("Hit_A");
		using (var log = new TreeLog(tree))
		{
			var movement = PushLong(body);
			ulong start = Engine.GetPhysicsFrames();
			await TestFactories.AwaitPhysicsUntil(runner, () => !movement.IsPushed, 3);
			AssertFloat((Engine.GetPhysicsFrames() - start) / (float)Engine.PhysicsTicksPerSecond).OverrideFailureMessage("The knockback must outlast the clip.").IsGreater((float)clip.Length);
			await runner.SimulateFrames(10, 20);
			AssertInt(log.Starts("Hit")).IsEqual(1);
			AssertString(log.Current).IsEqual("Idle");
		}

		var gate = body.GetNode<ActorActionGateComponent>("ActorActionGateComponent");
		using (var log = new TreeLog(tree))
		{
			AssertBool(gate.TryStagger(0.3f)).IsTrue();
			AssertBool(body.GetNode<CombatAnimationComponent>("CombatAnimationComponent").IsPlaying).OverrideFailureMessage("The stagger must pose.").IsTrue();
			var movement = PushLong(body);
			await TestFactories.AwaitPhysicsUntil(runner, () => !gate.IsStaggered, 2);
			AssertBool(movement.IsPushed).OverrideFailureMessage("The knockback must outlast the stagger.").IsTrue();
			await TestFactories.AwaitPhysicsUntil(runner, () => !movement.IsPushed, 3);
			await runner.SimulateFrames(10, 20);
			AssertArray(log.States).ContainsExactly("Idle");
		}
	}

	/// <summary>The rat holds its looping idle clip as its hit reaction, so it must leave the reaction when the push ends.</summary>
	[TestCase]
	public async Task RatRunsAgainOnceAKnockbackEnds()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var rat = await IdleActor(runner, TestFactories.RatScene);
		using var log = new TreeLog(Tree(rat));
		var movement = PushLong(rat);
		await TestFactories.AwaitPhysicsUntil(runner, () => !movement.IsPushed, 3);
		movement.SetInputDirection(Vector3.Left);
		await runner.SimulateFrames(10, 20);

		AssertArray(log.States).Contains("Hit");
		AssertString(log.Current).IsEqual("Walking");
	}

	[TestCase]
	public async Task SleepingSkeletonLiesUntilWokenThenStandsUp()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var skeleton = TestFactories.Load<PackedScene>(TestFactories.MinionScene).Instantiate<EnemyBase>();
		var definition = (MonsterDefinition)skeleton.Definition.Duplicate();
		definition.Behavior = (EnemyBehaviorProfile)definition.Behavior.Duplicate();
		definition.Behavior.InitialBehavior = EnemyBehaviorState.Sleeping;
		skeleton.Definition = definition;
		skeleton.Position = harness.Player.Position + new Vector3(30, 0, 0);
		using var log = new TreeLog(Tree(skeleton));
		runner.Scene().AddChild(skeleton);
		await runner.SimulateFrames(30, 20);
		AssertString(log.Current).IsEqual("Lie");

		skeleton.EnemyBehaviorComponent.NoticeAttacker(harness.Player);
		await TestFactories.AwaitPhysicsUntil(runner, () => log.Current is not ("Lie" or "StandUp"), 4);

		AssertArray(log.States.Where(state => state != "Start").Take(2).ToArray()).ContainsExactly("Lie", "StandUp");
		AssertArray(log.States).NotContains("Spawn");
	}
}
