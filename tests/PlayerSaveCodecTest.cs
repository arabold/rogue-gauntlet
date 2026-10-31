namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Same-version player capture and restore through the codec the session uses for saves and stairs.</summary>
[TestSuite, RequireGodotRuntime]
public class PlayerSaveCodecTest
{
	[TestCase]
	public async Task BuffAndStatusSourcesSurviveSaveWithoutReplayingTicksOrCures()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		var frozen = player.FrozenStatus.Condition;
		player.Frozen = true;
		player.BuffController.ApplyStatus(frozen, player.Invisibility, 8);
		var focus = new FrostNovaSpell { Name = "SavedFocus" }; player.AddChild(focus); focus.Owner = player;
		player.BuffController.ApplyStatus(frozen, focus, 4);
		player.BuffController.ApplyBuff(new StatModifierBuff { Name = "Strength", Duration = 12,
			Modifiers = [new StatModifier { Stat = StatType.Strength, Value = 3 }] });
		player.BuffController.ApplyBuff(new HealingBuff { Name = "Healing", Duration = 10, TicksPerSecond = 2, HealthPointsPerTick = 1 });
		var periodic = player.BuffController.ActiveBuffs.Last();
		periodic._PhysicsProcess(.3);
		float strength = player.Stats.Strength;
		var saved = JsonSerializer.Deserialize<PlayerSaveData>(JsonSerializer.Serialize(PlayerSaveCodec.Capture(player, false)));
		var restored = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		var restoredFocus = new FrostNovaSpell { Name = "SavedFocus" }; restored.AddChild(restoredFocus); restoredFocus.Owner = restored;
		TestFactories.AddToSceneTree(restored);
		PlayerSaveCodec.Apply(restored, saved);
		AssertBool(restored.Frozen).IsTrue();
		AssertFloat(restored.Stats.Strength).IsEqual(strength);
		AssertFloat((float)restored.BuffController.ActiveBuffs.Last().TickProgress).IsEqualApprox(.3, .001);
		restored.Frozen = false;
		AssertBool(restored.Frozen).IsTrue();
		restored.BuffController.RemoveStatuses(restored.Invisibility);
		AssertBool(restored.Frozen).IsTrue();
		restoredFocus.Cancel();
		AssertBool(restored.Frozen).IsFalse();
		AssertInt(restored.BuffController.ActiveBuffs.Count).IsEqual(2);
	}

	[TestCase]
	public async Task UnsupportedStatusSourcesAndInvalidSnapshotsAreRejected()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		var saved = PlayerSaveCodec.Capture(player, false);
		var source = new object();
		player.BuffController.ApplyStatus(player.FrozenStatus.Condition, source, 10);
		bool rejected = false;
		try { PlayerSaveCodec.Capture(player, false); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertBool(player.Frozen).IsTrue();
		player.BuffController.RemoveStatuses(source);
		saved.Buffs.Add(new ActiveBuffSaveData { Kind = "condition", HasSource = true, SourcePath = "../Invalid", Remaining = 5 });
		player.Stats.Gold = 91; rejected = false;
		try { PlayerSaveCodec.Apply(player, saved); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertInt(player.Stats.Gold).IsEqual(91);
	}

	[TestCase]
	public async Task CapturedPlayerRestoresExactlyAndARejectedRecordChangesNothing()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5);
		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().Single();
		player.Stats.Gold = 37;
		player.Stats.Xp = 5;
		player.Stats.BaseStrength = 13;
		player.ManaComponent.CurrentMana = 12.5f;
		var saved = PlayerSaveCodec.Capture(player, includeTransform: true);
		var data = JsonSerializer.Deserialize<PlayerSaveData>(JsonSerializer.Serialize(saved));
		Vector3 position = player.GlobalPosition;

		player.Stats.Gold = 0;
		player.Stats.BaseStrength = 1;
		// A deeper pool now than in the save: restoring the smaller maximum must not clamp away the saved mana.
		player.Stats.BaseIntelligence = 40;
		player.ManaComponent.CurrentMana = 200;
		player.GlobalPosition += new Vector3(4, 0, 0);
		PlayerSaveCodec.Apply(player, data);
		AssertInt(player.Stats.Gold).IsEqual(37);
		AssertInt(player.Stats.Xp).IsEqual(5);
		AssertFloat(player.Stats.BaseStrength).IsEqual(13);
		AssertFloat(player.ManaComponent.CurrentMana).IsEqual(12.5f);
		AssertFloat(player.ManaComponent.MaxMana).IsEqual(player.Stats.MaxMana);
		AssertVector(player.GlobalPosition).IsEqual(position);
		AssertString(JsonSerializer.Serialize(PlayerSaveCodec.Capture(player, includeTransform: true)))
			.IsEqual(JsonSerializer.Serialize(saved));

		// Level travel leaves placement to the next floor's arrival.
		AssertBool(PlayerSaveCodec.Capture(player, includeTransform: false).ApplyTransform).IsFalse();

		data.Inventory.Capacity = 0;
		player.Stats.Gold = 5;
		bool rejected = false;
		try { PlayerSaveCodec.Apply(player, data); } catch (InvalidOperationException) { rejected = true; }
		AssertBool(rejected).IsTrue();
		AssertInt(player.Stats.Gold).IsEqual(5);
		await runner.SimulateFrames(1);
	}

	[TestCase]
	public void RuntimeCopyKeepsEverySavedValue()
	{
		var stats = new PlayerStats { Health = 12, Mana = 7, Xp = 3, XpLevel = 2, Gold = 9, BaseDexterity = 14, BaseArmor = 2,
			BaseMaxMana = 30, ManaRegenRate = 0.1f, GoldModifier = 1.5f };
		var copy = stats.CreateRuntimeCopy();
		AssertBool(copy != stats).IsTrue();
		AssertString(JsonSerializer.Serialize(copy.CreateSaveData())).IsEqual(JsonSerializer.Serialize(stats.CreateSaveData()));
	}
}
