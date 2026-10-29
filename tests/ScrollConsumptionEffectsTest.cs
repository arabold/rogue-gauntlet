namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Committed scroll reading is additive to result cues and uses the real run-assigned art.</summary>
[TestSuite, RequireGodotRuntime]
public class ScrollConsumptionEffectsTest
{
	[TestCase]
	public async Task EveryScrollUsesItsAssignedRingWithoutMutatingTheSharedAppearance()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn");
		await runner.SimulateFrames(2); var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects();
		var player = preview.Stage.GetChildren().OfType<Player>().Single();
		var identity = new IdentificationService(); identity.Initialize(42); player.ItemUseEffects.Identification = identity;
		var category = GD.Load<IdentityCatalog>("res://scenes/items/identity/identity_catalog.tres").Categories.Single(c => c.Category == "scroll");
		foreach (var scroll in category.Types.OfType<Scroll>())
		{
			player.WorldEffects.Clear(); player.Inventory.NotifyConsumed(scroll);
			var ring = player.WorldEffects.InstancesRoot.GetChildren().OfType<RuneRingEffect>().Single(e => e.IsInsideTree() && !e.IsQueuedForDeletion());
			var appearance = identity.GetAppearance(scroll);
			AssertBool(ring.Glyphs.All(g => g.Mesh == appearance.GlyphMesh)).IsTrue();
			AssertBool(ring.AccentLight.LightColor.IsEqualApprox(appearance.GlyphColor)).IsTrue();
			AssertBool(ring.Definition == player.ItemUseEffects.ScrollReading).IsTrue();
			AssertBool(identity.IsIdentified(scroll)).IsFalse();
		}
	}
	[TestCase]
	public async Task ActualConsumptionPlaysReadingAndResultCueOnceAndCancellationPlaysNeither()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn");
		await runner.SimulateFrames(2); var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects();
		var player = preview.Stage.GetChildren().OfType<Player>().Single();
		var identity = new IdentificationService(); identity.Initialize(42); player.ItemUseEffects.Identification = identity;
		var scroll = (Scroll)ItemFactory.CopyDefinition(GD.Load<Scroll>("res://scenes/items/scrolls/scroll_vitality.tres"));
		scroll.UseCue = "healing"; scroll.Set("PerformDuration", 0f); player.Inventory.TryAdd(scroll, 2);
		var slot = player.Inventory.Items.First(s => s.Item == scroll);
		AssertBool(player.InventoryController.TryUse(slot).Success).IsTrue();
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(2);
		AssertBool(player.WorldEffects.InstancesRoot.GetChildren().OfType<RuneRingEffect>().Any()).IsTrue();
		player.WorldEffects.Clear(); scroll.Set("PerformDuration", 1f);
		AssertBool(player.InventoryController.TryUse(slot).Success).IsTrue();
		player.InventoryController.CancelItemUse(ActionInterruptReason.Damage);
		await runner.SimulateFrames(65, 20);
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(0);
	}
	[TestCase]
	public async Task DepartureStaysAtItsOriginAndArrivalFollowsTheActorUntilCleanup()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn");
		await runner.SimulateFrames(2); var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects();
		var player = preview.Stage.GetChildren().OfType<Player>().Single(); var host = player.WorldEffects;
		var departure = GD.Load<WorldEffectDefinition>("res://scenes/effects/world/teleport_departure/teleport_departure.tres");
		var arrival = GD.Load<WorldEffectDefinition>("res://scenes/effects/world/teleport_arrival/teleport_arrival.tres");
		Vector3 origin = player.GlobalPosition;
		host.PlayBurstAt(departure, 42, origin); player.GlobalPosition += Vector3.Right * 5; host.PlayBurst(arrival, 42);
		var effects = host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Where(e => !e.IsQueuedForDeletion()).ToArray();
		AssertBool(effects.Single(e => e.Definition == departure).GlobalPosition.IsEqualApprox(origin)).IsTrue();
		AssertBool(effects.Single(e => e.Definition == arrival).GlobalPosition.IsEqualApprox(player.GlobalPosition)).IsTrue();
		await runner.SimulateFrames(40, 20); AssertInt(host.ActiveInstanceCount).IsEqual(0);
		AssertObject(GD.Load<Scroll>("res://scenes/items/scrolls/scroll_summon.tres").Effect.Get("Arrival").AsGodotObject()).IsSame(arrival);
	}
}