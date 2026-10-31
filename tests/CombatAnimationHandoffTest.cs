namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Every player clip has one driver. Combat actions and staggers pose through <see cref="CombatAnimationComponent"/>,
/// and the AnimationTree plays locomotion, item use and knockback. Once a pose hands back, the tree resumes without
/// replaying what the pose already showed, and one knockback plays one hit reaction.
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

		public TreeLog(Player player)
		{
			_tree = player.GetNode<AnimationTree>("AnimationTree");
			_playback = (AnimationNodeStateMachinePlayback)_tree.Get("parameters/playback");
			_tree.MixerApplied += Record;
		}

		private void Record() => Samples.Add((_playback.GetCurrentNode(), _playback.GetCurrentPlayPosition()));

		public string[] States => Samples.Select(sample => sample.State).Distinct().ToArray();

		/// <summary>How often <paramref name="state"/>'s clip started: entering the state, or its position jumping back.</summary>
		public int Starts(string state) => Samples.Where((sample, i) => sample.State == state
			&& (i == 0 || Samples[i - 1].State != state || sample.Position < Samples[i - 1].Position)).Count();

		public void Dispose() => _tree.MixerApplied -= Record;
	}

	private static async Task<CombatHarness> LoadHarness(ISceneRunner runner)
	{
		var harness = await TestFactories.SettleCombatHarness(runner);
		await runner.SimulateFrames(20, 20);
		return harness;
	}

	[TestCase("unarmed")]
	[TestCase("technique")]
	[TestCase("spell")]
	public async Task CombatActionsLeaveTheTreeToLocomotion(string action)
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var player = harness.Player;
		if (action == "unarmed") player.Inventory.Unequip(player.Inventory.EquippedItems[EquipmentSlot.WeaponHand]);
		if (action == "spell") AssertBool(harness.ExecuteCommand("equip", Array.FindIndex(harness.Weapons, weapon => weapon is MagicStaff), Vector3.Zero)).IsTrue();
		await runner.SimulateFrames(20, 20);
		var pose = player.GetNode<CombatAnimationComponent>("CombatAnimationComponent");

		using var log = new TreeLog(player);
		bool accepted = action == "unarmed" ? player.ActionManager.TryPerformPrimary() : player.ActionManager.TryPerformSecondary();
		AssertBool(accepted).IsTrue();
		bool posed = false;
		string treeActionId = null;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			posed |= pose.IsPlaying;
			treeActionId ??= player.CurrentActionId;
			return !player.IsPerformingAction;
		}, 3);
		await runner.SimulateFrames(25, 20);

		AssertBool(posed).OverrideFailureMessage("The action never posed through the combat component.").IsTrue();
		AssertString(treeActionId).IsNull();
		AssertArray(log.States).ContainsExactly("Idle");
	}

	/// <summary>A poise break staggers, then pushes, as a hurtbox reacts; the knockback outlasts the stagger.</summary>
	[TestCase]
	public async Task StaggerPoseOwnsTheKnockbackItOverlaps()
	{
		var runner = TestFactories.LoadCombatHarness();
		var player = (await LoadHarness(runner)).Player;

		using var log = new TreeLog(player);
		AssertBool(player.TryStagger(0.3f)).IsTrue();
		player.MovementComponent.Push(Vector3.Right, 10);
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.ActionGate.IsStaggered, 2);
		AssertBool(player.MovementComponent.IsPushed).OverrideFailureMessage("The knockback must outlast the stagger.").IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.IsHit, 3);
		await runner.SimulateFrames(25, 20);

		AssertArray(log.States).ContainsExactly("Idle");
	}

	[TestCase]
	public async Task OneKnockbackPlaysOneHitReactionHoweverLongItLasts()
	{
		var runner = TestFactories.LoadCombatHarness();
		var player = (await LoadHarness(runner)).Player;
		var clip = player.GetNode<AnimationPlayer>("Pivot/Character/AnimationPlayer").GetAnimation("Hit_A");

		using var log = new TreeLog(player);
		player.MovementComponent.Push(Vector3.Right, 10);
		ulong start = Engine.GetPhysicsFrames();
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.IsHit, 3);
		AssertFloat((Engine.GetPhysicsFrames() - start) / (float)Engine.PhysicsTicksPerSecond).OverrideFailureMessage("The knockback must outlast the clip.").IsGreater((float)clip.Length);
		await runner.SimulateFrames(25, 20);

		AssertInt(log.Starts("Hit")).IsEqual(1);
		AssertString(log.Samples[^1].State).IsEqual("Idle");
	}
}
