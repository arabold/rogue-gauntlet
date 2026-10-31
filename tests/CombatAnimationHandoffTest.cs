namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Combat actions pose through <see cref="CombatAnimationComponent"/>, never the player's AnimationTree. While an
/// unarmed strike, a technique or a spell cast runs, the tree sees no action id; once the pose hands back, the tree
/// resumes idling instead of replaying an attack clip on its own clock.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CombatAnimationHandoffTest
{
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

		var tree = player.GetNode<AnimationTree>("AnimationTree");
		var playback = (AnimationNodeStateMachinePlayback)tree.Get("parameters/playback");
		var pose = player.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		var states = new HashSet<string>();
		void Record() => states.Add(playback.GetCurrentNode());
		tree.MixerApplied += Record;

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
		tree.MixerApplied -= Record;

		AssertBool(posed).OverrideFailureMessage("The action never posed through the combat component.").IsTrue();
		AssertString(treeActionId).IsNull();
		AssertArray(states.ToArray()).ContainsExactly("Idle");
	}
}
