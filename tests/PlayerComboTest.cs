namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Primary combos, the input buffer and recovery cancels through the real player: action slots, gate, attack
/// executor and input controller in the combat harness. No monster is spawned, so no hit-stop shifts timing.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class PlayerComboTest
{
	private const int Sword = 0, Axe = 1;

	private static async Task<(ISceneRunner, CombatHarness)> Load(int weapon = Sword)
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		AssertBool(harness.ExecuteCommand("equip", weapon, Vector3.Zero)).IsTrue();
		await Frames(runner, 2);
		return (runner, harness);
	}

	private static async Task Frames(ISceneRunner runner, int count)
	{
		var tree = runner.Scene().GetTree();
		for (int i = 0; i < count; i++)
			await runner.Scene().ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
	}

	/// <summary>Step physics frames until <paramref name="condition"/> holds; fails after a generous limit.</summary>
	private static async Task Until(ISceneRunner runner, Func<bool> condition, string what, int limit = 120)
	{
		for (int i = 0; i < limit && !condition(); i++) await Frames(runner, 1);
		AssertBool(condition()).OverrideFailureMessage($"Timed out waiting for {what}.").IsTrue();
	}

	private static PlayerAttackController Attack(CombatHarness harness) => harness.Player.AttackController;

	private static async Task PressPrimaryDuringContact(ISceneRunner runner, CombatHarness harness, int step)
	{
		await Until(runner, () => Attack(harness).ComboStep == step && Attack(harness).Phase == ActorActionPhase.Active, $"step {step} contact");
		harness.Player.InputController.PushIntent(PlayerIntent.Primary);
	}

	[TestCase]
	public async Task BufferedPressesChainEachStepOutOfRecoveryIntoTheFinisher()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			var finished = new List<bool>();
			Attack(harness).AttackFinished += (_, interrupted) => finished.Add(interrupted);
			harness.Player.InputController.PushIntent(PlayerIntent.Primary);
			await Until(runner, () => Attack(harness).ComboStep == 0, "the opener");
			AssertString(Attack(harness).CurrentStyle.ToString()).IsEqual("sword.1h.slash");

			// A press during contact is buffered and cancels the opener's recovery as soon as it begins.
			await PressPrimaryDuringContact(runner, harness, 0);
			await Until(runner, () => Attack(harness).ComboStep == 1, "the follow-up");
			AssertArray(finished).ContainsExactly(true);
			AssertString(Attack(harness).CurrentStyle.ToString()).IsEqual("sword.1h.combo2");

			await PressPrimaryDuringContact(runner, harness, 1);
			await Until(runner, () => Attack(harness).ComboStep == 2, "the finisher");
			AssertString(Attack(harness).CurrentStyle.ToString()).IsEqual("sword.1h.combo3");
			var finisher = Attack(harness).CurrentAttack;
			AssertFloat(finisher.PoiseDamage).IsEqualApprox(Attack(harness).PrimaryPoiseDamage * 1.8f, .001);
			AssertFloat(Attack(harness).PerformDuration).IsEqualApprox(0.5 * 1.3, .001);

			// The finisher has no next step: its recovery plays out and the combo starts over.
			harness.Player.InputController.PushIntent(PlayerIntent.Primary);
			await Until(runner, () => !Attack(harness).IsAttacking, "the finisher to end");
			AssertArray(finished).ContainsExactly(true, true, false);
			AssertInt(Attack(harness).ComboStep).IsEqual(-1);
		}
	}

	[TestCase]
	public async Task CompletedStepContinuesTheComboOnlyWithinTheChainWindow()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			await Until(runner, () => !harness.Player.IsPerformingAction, "the opener to end");
			// A held button starts the next step after recovery, still inside the window.
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			AssertInt(Attack(harness).ComboStep).IsEqual(1);
			await Until(runner, () => !harness.Player.IsPerformingAction, "the follow-up to end");

			await Frames(runner, Mathf.CeilToInt(0.4 * Engine.PhysicsTicksPerSecond));
			AssertInt(Attack(harness).ComboStep).IsEqual(-1);
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			AssertInt(Attack(harness).ComboStep).IsEqual(0);
		}
	}

	[TestCase]
	public async Task NoRecoveryCancelWithoutAFreshPress()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			await Until(runner, () => Attack(harness).Phase == ActorActionPhase.Recovery, "recovery");
			AssertBool(Attack(harness).CanChainPrimary).IsTrue();
			// Only the chain request may replace recovery; a plain request is still busy.
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsFalse();
			AssertBool(harness.Player.ActionManager.TryPerformSecondary()).IsFalse();
			AssertBool(harness.Player.ActionManager.TryChainPrimary()).IsTrue();
			AssertInt(Attack(harness).ComboStep).IsEqual(1);
		}
	}

	[TestCase]
	public async Task BufferedDodgeLeavesAComboAtRecoveryAndEndsIt()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			ActorActionPhase phaseBeforeDodge = ActorActionPhase.None;
			harness.Player.InputController.PushIntent(PlayerIntent.Primary);
			await PressPrimaryDuringContact(runner, harness, 0);
			await Until(runner, () => Attack(harness).ComboStep == 1, "the follow-up");

			// Dodge pressed during contact waits for recovery instead of being dropped.
			await Until(runner, () => Attack(harness).Phase == ActorActionPhase.Active, "follow-up contact");
			harness.Player.InputController.PushIntent(PlayerIntent.Dodge);
			for (int i = 0; i < 30 && !harness.Player.DodgeComponent.IsDodging; i++)
			{
				phaseBeforeDodge = Attack(harness).Phase;
				await Frames(runner, 1);
			}
			AssertBool(harness.Player.DodgeComponent.IsDodging).IsTrue();
			AssertInt((int)phaseBeforeDodge).IsEqual((int)ActorActionPhase.Recovery);
			AssertInt(Attack(harness).ComboStep).IsEqual(-1);

			await Until(runner, () => !harness.Player.IsPerformingAction, "the dodge to end");
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			AssertInt(Attack(harness).ComboStep).IsEqual(0);
		}
	}

	[TestCase]
	public async Task GuardCancelsPlayerRecovery()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			await Until(runner, () => Attack(harness).Phase == ActorActionPhase.Recovery, "recovery");
			AssertBool(harness.RequestHeroGuard(true)).IsTrue();
			AssertBool(Attack(harness).IsAttacking).IsFalse();
			AssertInt(Attack(harness).ComboStep).IsEqual(-1);
		}
	}

	[TestCase]
	public async Task SwitchingWeaponsRestartsTheCombo()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			await Until(runner, () => !harness.Player.IsPerformingAction, "the opener to end");
			AssertBool(harness.ExecuteCommand("equip", Axe, Vector3.Zero)).IsTrue();
			await Frames(runner, 1);
			AssertBool(harness.Player.ActionManager.TryPerformPrimary()).IsTrue();
			AssertInt(Attack(harness).ComboStep).IsEqual(0);
			AssertString(Attack(harness).CurrentStyle.ToString()).IsEqual("axe.1h.chop");
		}
	}
}
