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

	private static AnimationTree Tree(Node actor) => actor.GetNode<AnimationTree>(actor is global::Player ? "AnimationTree" : "EnemyBehaviorComponent/AnimationTree");

	private static AnimationNodeStateMachinePlayback Playback(Node actor) => (AnimationNodeStateMachinePlayback)Tree(actor).Get("parameters/playback");

	/// <summary>Skips the monsters' spawn clip, a 3.5-second rise from the ground: these tests start from idling monsters.</summary>
	private static async Task SkipSpawnClip(ISceneRunner runner, params EnemyBase[] monsters)
	{
		foreach (var monster in monsters) Playback(monster).Start("Idle");
		await TestFactories.AwaitPhysicsUntil(runner, () => monsters.All(monster => Playback(monster).GetCurrentNode() == "Idle"), 1);
		await TestFactories.AwaitPhysicsFrames(runner, 5);
	}

	/// <summary>A push lasting about 1.2 seconds: it slows to rest at <see cref="MovementComponent.PushDeceleration"/>.</summary>
	private static MovementComponent PushLong(Node actor)
	{
		const float seconds = 1.2f;
		var movement = actor.GetNode<MovementComponent>("MovementComponent");
		movement.ResistsKnockback = false; // the animation handoff, not who resists a push (the Warden does)
		movement.Push(Vector3.Right, MovementComponent.PushDeceleration * seconds * seconds / 2);
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
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		async Task Perform(string action, Func<bool> request)
		{
			await TestFactories.AwaitPhysicsSeconds(runner, .2f); // Let the tree settle into Idle before logging.
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
			await TestFactories.AwaitPhysicsSeconds(runner, .2f);

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
	/// the stagger pose owns the knockback it overlaps, so the tree never shows the reaction under it. The player, a
	/// minion and the Warden are staged side by side in one arena and pushed together.
	/// </summary>
	[TestCase]
	public async Task KnockbackPlaysOneHitReactionAndAStaggerPoseOwnsTheKnockbackItOverlaps()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		TestFactories.MovePracticeTargetAway(harness);
		var minion = TestFactories.SpawnCombatMonster(runner.Scene(), harness.Player.Position + new Vector3(0, 0, 6), "Minion", TestFactories.MinionScene);
		var warden = TestFactories.SpawnCombatMonster(runner.Scene(), harness.Player.Position + new Vector3(0, 0, 14), "Warden", TestFactories.WardenScene);
		var actors = new List<CharacterBody3D> { harness.Player, minion, warden };
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		await SkipSpawnClip(runner, minion, warden);
		await TestFactories.AwaitPhysicsSeconds(runner, .4f);
		static string Label(Node actor) => actor is global::Player ? "player" : actor.SceneFilePath;

		var logs = actors.Select(actor => new TreeLog(Tree(actor))).ToArray();
		var movements = actors.Select(PushLong).ToArray();
		ulong start = Engine.GetPhysicsFrames();
		var pushed = new float[actors.Count];
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			for (int i = 0; i < actors.Count; i++)
				if (pushed[i] == 0 && !movements[i].IsPushed) pushed[i] = (Engine.GetPhysicsFrames() - start) / (float)Engine.PhysicsTicksPerSecond;
			return pushed.All(seconds => seconds > 0);
		}, 3);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		for (int i = 0; i < actors.Count; i++)
		{
			var clip = Tree(actors[i]).GetNode<AnimationPlayer>(Tree(actors[i]).AnimPlayer).GetAnimation("Hit_A");
			AssertFloat(pushed[i]).OverrideFailureMessage($"{Label(actors[i])}: the knockback must outlast the clip.").IsGreater((float)clip.Length);
			AssertInt(logs[i].Starts("Hit")).OverrideFailureMessage(Label(actors[i])).IsEqual(1);
			AssertString(logs[i].Current).OverrideFailureMessage(Label(actors[i])).IsEqual("Idle");
			logs[i].Dispose();
		}

		logs = actors.Select(actor => new TreeLog(Tree(actor))).ToArray();
		var gates = actors.Select(actor => actor.GetNode<ActorActionGateComponent>("ActorActionGateComponent")).ToArray();
		for (int i = 0; i < actors.Count; i++)
		{
			AssertBool(gates[i].TryStagger(0.3f)).OverrideFailureMessage(Label(actors[i])).IsTrue();
			AssertBool(actors[i].GetNode<CombatAnimationComponent>("CombatAnimationComponent").IsPlaying)
				.OverrideFailureMessage($"{Label(actors[i])}: the stagger must pose.").IsTrue();
		}
		movements = actors.Select(PushLong).ToArray();
		await TestFactories.AwaitPhysicsUntil(runner, () => gates.All(gate => !gate.IsStaggered), 2);
		for (int i = 0; i < actors.Count; i++)
			AssertBool(movements[i].IsPushed).OverrideFailureMessage($"{Label(actors[i])}: the knockback must outlast the stagger.").IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => movements.All(movement => !movement.IsPushed), 3);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		for (int i = 0; i < actors.Count; i++)
		{
			AssertArray(logs[i].States).OverrideFailureMessage(Label(actors[i])).ContainsExactly("Idle");
			logs[i].Dispose();
		}
	}

	/// <summary>The rat holds its looping idle clip as its hit reaction, so it must leave the reaction when the push ends.</summary>
	[TestCase]
	public async Task RatRunsAgainOnceAKnockbackEnds()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var rat = TestFactories.SpawnHarnessMonster(await TestFactories.SettleCombatHarness(runner), TestFactories.RatScene);
		await SkipSpawnClip(runner, rat);
		using var log = new TreeLog(Tree(rat));
		var movement = PushLong(rat);
		await TestFactories.AwaitPhysicsUntil(runner, () => !movement.IsPushed, 3);
		movement.SetInputDirection(Vector3.Left);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);

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
		await TestFactories.AwaitPhysicsSeconds(runner, .6f);
		AssertString(log.Current).IsEqual("Lie");

		skeleton.EnemyBehaviorComponent.NoticeAttacker(harness.Player);
		await TestFactories.AwaitPhysicsUntil(runner, () => log.Current is not ("Lie" or "StandUp"), 4);

		AssertArray(log.States.Where(state => state != "Start").Take(2).ToArray()).ContainsExactly("Lie", "StandUp");
		AssertArray(log.States).NotContains("Spawn");
	}
}
