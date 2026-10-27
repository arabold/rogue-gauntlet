namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Real resource/scene coverage for source ownership, material arbitration, death and committed item use.</summary>
[TestSuite, RequireGodotRuntime]
public class WorldEffectLifecycleTest
{
	private static WorldEffectDefinition Definition(string id) => GD.Load<WorldEffectDefinition>($"res://scenes/effects/world/{id}/{id}.tres");
	private static (Node3D Root, WorldEffectHost Host, MeshInstance3D Mesh, StandardMaterial3D Original) Fixture()
	{
		// This material fixture shares the runtime viewport with preview scenes. Keep it out of visual captures.
		var root = new Node3D { Visible = false };
		var original = new StandardMaterial3D { AlbedoColor = Colors.Red };
		var mesh = new MeshInstance3D { Mesh = new BoxMesh { Material = original } }; root.AddChild(mesh);
		var surface = new SurfaceAppearanceController { Target = mesh }; root.AddChild(surface);
		var host = GD.Load<PackedScene>("res://scenes/effects/world/actor_world_effects.tscn").Instantiate<WorldEffectHost>();
		host.SurfaceController = surface; root.AddChild(host); TestFactories.AddToSceneTree(root);
		return (root, host, mesh, original);
	}
	[TestCase]
	public void ActivationBurstsIncludeTheirParticleTailsWithinOneSecond()
	{
		foreach (string id in new[] { "healing", "strength", "arcane", "level_up" })
		{
			var instance = AutoFree(Definition(id).Scene.Instantiate<WorldEffectInstance>());
			AssertFloat(instance.Duration + instance.ExitTime).IsLess(1);
			foreach (var emitter in instance.Emitters) AssertBool(emitter.Lifetime <= instance.ExitTime).IsTrue();
		}
	}
	[TestCase]
	public void SustainedSourcesAreIdempotentAndOnlyTheLastReleaseEndsTheVisual()
	{
		var (_, host, _, _) = Fixture(); var definition = Definition("blessed");
		var a = new object(); var b = new object();
		AssertBool(host.SetSustained(a, definition, true, 42) == WorldEffectResult.Started).IsTrue();
		AssertBool(host.SetSustained(a, definition, true, 42) == WorldEffectResult.Unchanged).IsTrue();
		host.SetSustained(b, definition, true, 42);
		AssertInt(host.ActiveInstanceCount).IsEqual(1); AssertInt(host.LeaseCount).IsEqual(2);
		host.SetSustained(a, definition, false, 42);
		AssertInt(host.ActiveInstanceCount).IsEqual(1); AssertInt(host.LeaseCount).IsEqual(1);
		host.SetSustained(b, definition, false, 42);
		AssertInt(host.LeaseCount).IsEqual(0);
		AssertBool(host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single().IsEnding).IsTrue();
		host.Clear(); AssertInt(host.ActiveInstanceCount).IsEqual(0);
	}
	[TestCase]
	public void HigherPrioritySkinRestoresTheBaselineAndNeverCapturesAnotherSkin()
	{
		var (_, host, mesh, original) = Fixture(); var a = new object(); var b = new object();
		host.SetSustained(a, Definition("iron_skin"), true, 42);
		AssertInt(((ShaderMaterial)mesh.GetActiveMaterial(0)).GetShaderParameter("skin_kind").AsInt32()).IsEqual(0);
		host.SetSustained(b, Definition("stone_skin"), true, 42);
		AssertInt(((ShaderMaterial)mesh.GetActiveMaterial(0)).GetShaderParameter("skin_kind").AsInt32()).IsEqual(1);
		AssertObject(mesh.Mesh.SurfaceGetMaterial(0)).IsSame(original);
		host.Clear(); AssertObject(mesh.GetActiveMaterial(0)).IsSame(original);
		AssertBool(mesh.CastShadow == GeometryInstance3D.ShadowCastingSetting.On).IsTrue();
	}
	[TestCase]
	public void RestartsStayBoundedAndRejectedOperationsPreserveExistingEffects()
	{
		var (_, host, _, _) = Fixture(); var healing = Definition("healing");
		for (int i = 0; i < 10; i++) host.PlayBurst(healing, 42);
		AssertInt(host.ActiveInstanceCount).IsEqual(1);
		AssertBool(host.SetSustained(new object(), healing, true, 42) == WorldEffectResult.Rejected).IsTrue();
		AssertInt(host.ActiveInstanceCount).IsEqual(1);
		host.PlayBurst(Definition("strength"), 42); AssertInt(host.ActiveInstanceCount).IsEqual(2);
		for (int i = 0; i < 10; i++) host.PlayBurst(Definition("level_up"), 42);
		AssertInt(host.ActiveInstanceCount).IsEqual(4);
	}
	[TestCase]
	public void InstancesOwnTheirMaterialsAndRepeatTheSameEmitterSeeds()
	{
		var (_, a, _, _) = Fixture(); var (_, b, _, _) = Fixture();
		a.PlayBurst(Definition("healing"), 42); b.PlayBurst(Definition("healing"), 42);
		var first = a.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single();
		var second = b.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single();
		AssertBool(first.Layers[0].MaterialOverride != second.Layers[0].MaterialOverride).IsTrue();
		AssertBool(first.Emitters[0].Seed == second.Emitters[0].Seed).IsTrue();
		first.Layers[0].MaterialOverride.Set("shader_parameter/envelope", .2f);
		AssertFloat(((ShaderMaterial)second.Layers[0].MaterialOverride).GetShaderParameter("envelope").AsSingle()).IsEqual(0);
	}
	[TestCase]
	public void DeathIsTerminalAndRestoresSurfaceSlotsImmediately()
	{
		var (_, host, mesh, original) = Fixture();
		host.SetSustained(new object(), Definition("iron_skin"), true, 42); host.StopForDeath();
		AssertObject(mesh.GetActiveMaterial(0)).IsSame(original); AssertInt(host.LeaseCount).IsEqual(0);
		AssertBool(host.PlayBurst(Definition("healing"), 42) == WorldEffectResult.Rejected).IsTrue();
		AssertBool(host.SurfaceController.IsStopped).IsTrue(); host.Clear();
		AssertBool(host.SetSustained(new object(), Definition("blessed"), true, 42) == WorldEffectResult.Rejected).IsTrue();
	}
	[TestCase]
	public async Task BurstFinishesAndSuppressedAuraKeepsItsSourceWithoutReplayingTheEntry()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/actor_item_effect_preview.tscn", true);
		await runner.SimulateFrames(2); var preview = (ActorItemEffectPreview)runner.Scene();
		preview.ClearEffects(); preview.SetPlaybackSpeed(1);
		var (_, host, _, _) = Fixture();
		host.PlayBurst(Definition("healing"), 42); await runner.SimulateFrames(110, 20);
		AssertInt(host.ActiveInstanceCount).IsEqual(0);
		host.SetSustained(new object(), Definition("blessed"), true, 42);
		await runner.SimulateFrames(20, 20); host.SetOwnerSuppressed(true);
		AssertInt(host.LeaseCount).IsEqual(1);
		var effect = host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single();
		AssertBool(effect.Visible).IsFalse(); float elapsed = effect.Elapsed;
		host.SetOwnerSuppressed(false); AssertBool(effect.Visible).IsTrue(); AssertFloat(effect.Elapsed).IsEqual(elapsed);
	}
	[TestCase]
	public async Task HarnessAndCommittedConsumptionUseTheProductionHost()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/actor_item_effect_preview.tscn", true);
		await runner.SimulateFrames(2); var preview = (ActorItemEffectPreview)runner.Scene();
		preview.ClearEffects(); var player = preview.Stage.GetChildren().OfType<Player>().Single();
		var potion = ItemFactory.CopyDefinition(GD.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres"));
		potion.Set("PerformDuration", 0f); player.Inventory.TryAdd(potion, 1);
		var slot = player.Inventory.Items.First(s => s.Item == potion);
		player.HealthComponent.TakeDamage(2);
		AssertBool(player.InventoryController.TryUse(slot).Success).IsTrue();
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(1);
		AssertBool(player.Inventory.Items.Contains(slot)).IsFalse();
		preview.SelectEffect(0); await runner.SimulateFrames(2);
		AssertInt(preview.Stage.GetChildCount()).IsEqual(1); AssertInt(preview.InstanceCount).IsEqual(1);
	}
	[TestCase]
	public void CurseAuraRequiresKnowledgeAndStopsWhenTheCurseIsLifted()
	{
		var model = new Node3D(); model.AddChild(new MeshInstance3D { Mesh = new BoxMesh() });
		var item = new EquipableItem { CursePenalty = [new StatModifier()], CurseStatusKnown = false };
		var aura = GD.Load<PackedScene>("res://scenes/effects/world/item_curse_presenter.tscn").Instantiate<ItemCurseWorldEffectPresenter>();
		aura.Initialize(item, model); model.AddChild(aura); TestFactories.AddToSceneTree(model);
		AssertInt(aura.Host.LeaseCount).IsEqual(0); item.RevealCurseStatus();
		AssertInt(aura.Host.LeaseCount).IsEqual(1); item.LiftCurse(); AssertInt(aura.Host.LeaseCount).IsEqual(0);
	}
	[TestCase]
	public async Task HelpersAddedDuringASkinAreExcludedAndTheWorkbenchUnloadsCleanly()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/world_effect_harness.tscn", true);
		await runner.SimulateFrames(2);
		var (_, host, mesh, _) = Fixture();
		host.SetSustained(new object(), Definition("iron_skin"), true, 42);
		var helper = new Node3D(); VisualHelper.Mark(helper);
		var own = new StandardMaterial3D { AlbedoColor = Colors.Purple };
		var auraMesh = new MeshInstance3D { Mesh = new SphereMesh(), MaterialOverride = own };
		helper.AddChild(auraMesh); mesh.AddChild(helper); await runner.SimulateFrames(2);
		AssertObject(auraMesh.MaterialOverride).IsSame(own);
		var harness = (WorldEffectHarness)runner.Scene(); harness.SelectEffect(6); await runner.SimulateFrames(2);
		var preview = harness.PreviewRoot.GetChildren().OfType<ActorItemEffectPreview>().Single();
		preview.SelectEffect(0);
		var actor = preview.Stage.GetChildren().OfType<Player>().Single();
		actor.WorldEffects.SetSustained(this, Definition("blessed"), true, 42);
		actor.WorldEffects.PlayBurst(Definition("healing"), 42);
		harness.SelectEffect(0); await runner.SimulateFrames(3);
		AssertBool(GodotObject.IsInstanceValid(preview)).IsFalse(); AssertObject(harness.PreviewTrap).IsNotNull();
	}
	[TestCase]
	public async Task HealingTicksDoNotReplayTheActivationAndCanceledUseEmitsNothing()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/actor_item_effect_preview.tscn", true);
		await runner.SimulateFrames(2); var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects();
		var player = preview.Stage.GetChildren().OfType<Player>().Single();
		player.BuffController.ProcessMode = Node.ProcessModeEnum.Always;
		var potion = ItemFactory.CopyDefinition(GD.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres"));
		potion.Set("PerformDuration", 0f); player.Inventory.TryAdd(potion, 2);
		var slot = player.Inventory.Items.First(s => s.Item == potion);
		player.HealthComponent.TakeDamage(30); player.InventoryController.TryUse(slot);
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(1);
		await runner.SimulateFrames(110, 20);
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(0);
		AssertInt(player.BuffController.ActiveBuffs.Count).IsGreater(0);
		potion.Set("PerformDuration", 1f); player.InventoryController.TryUse(slot);
		player.InventoryController.CancelItemUse(ActionInterruptReason.Invalidated);
		await runner.SimulateFrames(60, 20);
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(0); AssertInt(slot.Quantity).IsEqual(1);
	}

	[TestCase]
	public void InitiallyCloakedActorsSuppressItemPresentersThatEnteredBeforeTheirHost()
	{
		var root = new Node3D(); var model = new Node3D();
		model.AddChild(new MeshInstance3D { Mesh = new BoxMesh() }); root.AddChild(model);
		var surface = new SurfaceAppearanceController { Target = model };
		var cloak = GD.Load<PackedScene>("res://scenes/components/invisibility_component.tscn").Instantiate<InvisibilityComponent>();
		cloak.StartInvisible = true;
		var host = GD.Load<PackedScene>("res://scenes/effects/world/actor_world_effects.tscn").Instantiate<WorldEffectHost>();
		host.SurfaceController = surface; cloak.Host = host;
		var item = new EquipableItem { CursePenalty = [new StatModifier()], CurseStatusKnown = true };
		var presenter = GD.Load<PackedScene>("res://scenes/effects/world/item_curse_presenter.tscn").Instantiate<ItemCurseWorldEffectPresenter>();
		presenter.Initialize(item, model, host); model.AddChild(presenter);
		root.AddChild(cloak); root.AddChild(surface); root.AddChild(host); TestFactories.AddToSceneTree(root);
		AssertBool(cloak.IsInvisible).IsTrue(); AssertBool(presenter.Host.IsSuppressed).IsTrue();
		AssertInt(presenter.Host.LeaseCount).IsEqual(1);
		AssertBool(presenter.Host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single().Visible).IsFalse();
	}

}