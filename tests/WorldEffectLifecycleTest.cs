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
	private static WorldEffectDefinition Definition(string id) => TestFactories.Load<WorldEffectDefinition>($"res://scenes/effects/world/{id}/{id}.tres");
	private static (Node3D Root, WorldEffectHost Host, MeshInstance3D Mesh, StandardMaterial3D Original) Fixture()
	{
		// This material fixture shares the runtime viewport with preview scenes. Keep it out of visual captures.
		var root = new Node3D { Visible = false };
		var original = new StandardMaterial3D { AlbedoColor = Colors.Red };
		var mesh = new MeshInstance3D { Mesh = new BoxMesh { Material = original } }; root.AddChild(mesh);
		var surface = new SurfaceAppearanceController { Target = mesh }; root.AddChild(surface);
		var host = TestFactories.Load<PackedScene>("res://scenes/effects/world/actor_world_effects.tscn").Instantiate<WorldEffectHost>();
		host.SurfaceController = surface; root.AddChild(host); TestFactories.AddToSceneTree(root);
		return (root, host, mesh, original);
	}
	/// <summary>The host's one instance that is still playing; finished ones linger until the frame ends.</summary>
	private static WorldEffectInstance Live(WorldEffectHost host) =>
		host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single(effect => !effect.IsQueuedForDeletion());
	[TestCase]
	public void EditorSurfacePreviewDoesNotChangeOrSerializeAuthoredMaterials()
	{
		var root = new Node3D();
		var original = new StandardMaterial3D { AlbedoColor = Colors.Red };
		var overlay = new StandardMaterial3D { AlbedoColor = Colors.Blue };
		var mesh = new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh(), MaterialOverride = original, MaterialOverlay = overlay };
		root.AddChild(mesh); mesh.Owner = root; TestFactories.AddToSceneTree(root);
		var profile = AutoFree(Definition("invisibility").Scene.Instantiate<WorldEffectInstance>()).Surface;
		var preview = new EditorSurfacePreview();
		AssertBool(preview.Set(root, profile)).IsTrue();
		AssertObject(mesh.MaterialOverride).IsSame(original);
		AssertObject(mesh.MaterialOverlay).IsSame(overlay);
		AssertBool(mesh.GetSurfaceOverrideMaterial(0) == null).IsTrue();
		AssertBool(mesh.CastShadow == GeometryInstance3D.ShadowCastingSetting.On).IsTrue();
		var packed = new PackedScene(); AssertBool(packed.Pack(root) == Error.Ok).IsTrue();
		var saved = AutoFree(packed.Instantiate<Node3D>()); var savedMesh = saved.GetNode<MeshInstance3D>("Mesh");
		AssertObject(savedMesh.MaterialOverride).IsSame(original);
		AssertObject(savedMesh.MaterialOverlay).IsSame(overlay);
		preview.Clear();
		AssertObject(mesh.MaterialOverride).IsSame(original);
		AssertObject(mesh.MaterialOverlay).IsSame(overlay);
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
	public void AuthoredInitialEffectsStartThroughTheSharedHost()
	{
		var root = new Node3D { Visible = false };
		var host = TestFactories.Load<PackedScene>("res://scenes/effects/world/actor_world_effects.tscn").Instantiate<WorldEffectHost>();
		host.InitialEffects = [Definition("blessed"), Definition("healing")];
		root.AddChild(host); TestFactories.AddToSceneTree(root);
		AssertInt(host.ActiveInstanceCount).IsEqual(2);
		AssertInt(host.LeaseCount).IsEqual(1);
		host.Clear();
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
	public void SkinsUseTheFullEntryAndExitEnvelope()
	{
		foreach (string id in new[] { "frozen", "iron_skin", "stone_skin" })
		{
			var (_, host, mesh, original) = Fixture();
			var definition = Definition(id); var source = new object();
			AssertBool(host.SetSustained(source, definition, true, 42) == WorldEffectResult.Started).IsTrue();
			var effect = host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single();
			var material = (ShaderMaterial)mesh.GetActiveMaterial(0);
			AssertFloat(material.GetShaderParameter("amount").AsSingle()).IsEqual(0);
			effect._Process(effect.EnterTime / 2);
			AssertFloat(material.GetShaderParameter("amount").AsSingle()).IsEqualApprox(.5, .01);
			effect._Process(effect.EnterTime / 2);
			AssertFloat(material.GetShaderParameter("amount").AsSingle()).IsEqualApprox(1, .01);
			host.SetSustained(source, definition, false, 42);
			effect._Process(effect.ExitTime / 2);
			AssertFloat(material.GetShaderParameter("amount").AsSingle()).IsEqualApprox(.5, .01);
			effect._Process(effect.ExitTime);
			AssertObject(mesh.GetActiveMaterial(0)).IsSame(original);
		}
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
		var levelUp = Definition("level_up");
		for (int i = 0; i < 10; i++) host.PlayBurst(levelUp, 42);
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
	public void BurstFinishesAndSuppressedAuraKeepsItsSourceWithoutReplayingTheEntry()
	{
		var (_, host, _, _) = Fixture();
		host.PlayBurst(Definition("healing"), 42);
		var burst = Live(host);
		burst._Process(burst.Duration); burst._Process(burst.ExitTime);
		AssertInt(host.ActiveInstanceCount).IsEqual(0);
		host.SetSustained(new object(), Definition("blessed"), true, 42);
		var effect = Live(host);
		effect._Process(.4f); host.SetOwnerSuppressed(true);
		AssertInt(host.LeaseCount).IsEqual(1);
		AssertBool(effect.Visible).IsFalse(); float elapsed = effect.Elapsed;
		host.SetOwnerSuppressed(false); AssertBool(effect.Visible).IsTrue(); AssertFloat(effect.Elapsed).IsEqual(elapsed);
	}
	[TestCase]
	public void CurseAuraRequiresKnowledgeAndStopsWhenTheCurseIsLifted()
	{
		var model = new Node3D(); model.AddChild(new MeshInstance3D { Mesh = new BoxMesh() });
		var item = new EquipableItem { CursePenalty = [new StatModifier()], CurseStatusKnown = false };
		var aura = TestFactories.Load<PackedScene>("res://scenes/effects/world/item_aura_presenter.tscn").Instantiate<ItemAuraPresenter>();
		aura.Initialize(item, model); model.AddChild(aura); TestFactories.AddToSceneTree(model);
		AssertInt(aura.Host.LeaseCount).IsEqual(0); item.RevealCurseStatus();
		AssertInt(aura.Host.LeaseCount).IsEqual(1); item.LiftCurse(); AssertInt(aura.Host.LeaseCount).IsEqual(0);
	}
	[TestCase]
	public void SpellWeaponsShowTheirElementAuraUntilAKnownCurseTakesOver()
	{
		var model = new Node3D(); model.AddChild(new MeshInstance3D { Mesh = new BoxMesh() });
		var wand = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/wand.tres"));
		var aura = TestFactories.Load<PackedScene>("res://scenes/effects/world/item_aura_presenter.tscn").Instantiate<ItemAuraPresenter>();
		aura.Initialize(wand, model); model.AddChild(aura); TestFactories.AddToSceneTree(model);
		string Showing() => aura.Host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single(i => !i.IsEnding).Definition.Id;

		AssertString(Showing()).IsEqual("fire_aura");
		wand.BoundSpell = TestFactories.Load<SpellDefinition>("res://scenes/magic/spells/frost_nova.tres");
		AssertString(Showing()).IsEqual("water_aura");
		// An unknown curse keeps the element; revealing it hands the aura to the curse.
		wand.CurseStatusKnown = false; wand.CursePenalty = [new StatModifier()];
		AssertString(Showing()).IsEqual("water_aura");
		wand.RevealCurseStatus();
		AssertString(Showing()).IsEqual("cursed_item");
		wand.LiftCurse();
		AssertString(Showing()).IsEqual("water_aura");
		AssertInt(aura.Host.LeaseCount).IsEqual(1);
	}
	[TestCase]
	public async Task MeshesAddedDuringASkinTakeItUnlessTheyAreHelpers()
	{
		var (root, host, mesh, _) = Fixture();
		host.SetSustained(new object(), Definition("iron_skin"), true, 42);
		var gearMaterial = new StandardMaterial3D { AlbedoColor = Colors.Green };
		var gear = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = gearMaterial };
		var helper = new Node3D(); VisualHelper.Mark(helper);
		var own = new StandardMaterial3D { AlbedoColor = Colors.Purple };
		var auraMesh = new MeshInstance3D { Mesh = new SphereMesh(), MaterialOverride = own };
		helper.AddChild(auraMesh); mesh.AddChild(helper); mesh.AddChild(gear);
		// New meshes are captured on a deferred call.
		var tree = root.GetTree();
		await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		AssertObject(gear.GetActiveMaterial(0)).IsNotSame(gearMaterial);
		AssertObject(auraMesh.GetActiveMaterial(0)).IsSame(own);
	}
	[TestCase]
	public async Task HealingTicksDoNotReplayTheActivation()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn");
		await runner.SimulateFrames(2, 20); var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects();
		var player = preview.Stage.GetChildren().OfType<Player>().Single();
		var potion = ItemFactory.CopyDefinition(TestFactories.Load<ConsumableItem>("res://scenes/items/potions/potion_healing_small.tres"));
		potion.Set("PerformDuration", 0f); player.Inventory.TryAdd(potion, 1);
		player.HealthComponent.TakeDamage(30);
		AssertBool(player.InventoryController.TryUse(player.Inventory.Items.First(s => s.Item == potion)).Success).IsTrue();
		var activation = Live(player.WorldEffects);
		activation._Process(activation.Duration); activation._Process(activation.ExitTime);
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(0);

		// The regeneration keeps healing without replaying the drink's activation.
		float health = player.HealthComponent.CurrentHealth;
		foreach (var buff in player.BuffController.ActiveBuffs.ToArray()) buff._PhysicsProcess(1);
		AssertFloat(player.HealthComponent.CurrentHealth).IsGreater(health);
		AssertInt(player.WorldEffects.ActiveInstanceCount).IsEqual(0);
	}

	[TestCase]
	public void InitiallyCloakedActorsSuppressItemPresentersThatEnteredBeforeTheirHost()
	{
		var root = new Node3D(); var model = new Node3D();
		model.AddChild(new MeshInstance3D { Mesh = new BoxMesh() }); root.AddChild(model);
		var surface = new SurfaceAppearanceController { Target = model };
		var cloak = TestFactories.Load<PackedScene>("res://scenes/components/invisibility_component.tscn").Instantiate<InvisibilityComponent>();
		var statuses = new BuffController(); root.AddChild(statuses); cloak.Statuses = statuses;
		var host = TestFactories.Load<PackedScene>("res://scenes/effects/world/actor_world_effects.tscn").Instantiate<WorldEffectHost>();
		host.SurfaceController = surface; cloak.Host = host;
		var item = new EquipableItem { CursePenalty = [new StatModifier()], CurseStatusKnown = true };
		var presenter = TestFactories.Load<PackedScene>("res://scenes/effects/world/item_aura_presenter.tscn").Instantiate<ItemAuraPresenter>();
		presenter.Initialize(item, model, host); model.AddChild(presenter);
		root.AddChild(cloak); root.AddChild(surface); root.AddChild(host); TestFactories.AddToSceneTree(root);
		cloak.SetInvisible(true, true);
		AssertBool(cloak.IsInvisible).IsTrue(); AssertBool(presenter.Host.IsSuppressed).IsTrue();
		AssertInt(presenter.Host.LeaseCount).IsEqual(1);
		AssertBool(presenter.Host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single().Visible).IsFalse();
	}

}
