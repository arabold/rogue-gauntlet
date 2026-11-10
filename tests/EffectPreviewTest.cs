namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Focused visual controls, fitted item presentation and the two-phase teleport handoff.</summary>
[TestSuite, RequireGodotRuntime]
public class EffectPreviewTest
{
	private const string Scene = "res://scenes/debug/effects/actor_item_effect_preview.tscn";
	[TestCase]
	public async Task ReplayKeepsOneSubjectAndReappliesSustainedEffectsAfterClearing()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.SelectEffect(0);
		AssertInt(preview.InstanceCount).IsEqual(1); preview.ClearEffects(); AssertInt(preview.InstanceCount).IsEqual(0);
		preview.Replay(); AssertInt(preview.Stage.GetChildCount()).IsEqual(1); AssertInt(preview.InstanceCount).IsEqual(1);
	}
	[TestCase]
	public async Task MouseDragAndTheGameCameraActionBothOrbitThePreview()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var camera = (EffectPreviewCamera)((ActorItemEffectPreview)runner.Scene()).Camera; Vector3 before = camera.Position;
		camera._UnhandledInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Right, Relative = new Vector2(40, 10) });
		AssertBool(camera.Position.IsEqualApprox(before)).IsFalse(); before = camera.Position;
		try { Input.ActionPress("camera_rotate_right"); camera._Process(.1); }
		finally { Input.ActionRelease("camera_rotate_right"); }
		AssertBool(camera.Position.IsEqualApprox(before)).IsFalse();
	}
	[TestCase]
	public async Task CurseReusesTheItemSurfaceAndHasNoProxyShell()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.SelectEffect(7); preview.SetSubject(3);
		var subject = preview.Stage.GetChild(0);
		AssertInt(subject.FindChildren("*", "", true, false).OfType<ItemAuraPresenter>().Count()).IsEqual(0);
		var host = subject.GetChildren().OfType<WorldEffectHost>().Single();
		var effect = host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single();
		AssertBool(effect is ItemAuraEffect).IsTrue(); AssertBool(effect.Layers.Any(m => m.Mesh is SphereMesh or CylinderMesh)).IsFalse();
		var originals = preview.Stage.GetChild(0).FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => !VisualHelper.IsInHelperSubtree(m)).ToArray();
		foreach (var layer in effect.Layers) AssertBool(originals.Any(m => m.Mesh == layer.Mesh && m.GlobalTransform.IsEqualApprox(layer.GlobalTransform))).IsTrue();
		AssertBool(effect.AccentLight.Visible).IsTrue();
	}
	[TestCase]
	public async Task TeleportFadesBeforeRelocationAndRestoresTheSkinAfterArrival()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.SelectEffect(0);
		var player = preview.Stage.GetChild<Player>(0); var host = player.WorldEffects; Vector3 origin = player.GlobalPosition;
		Vector3 destination = origin + Vector3.Right * 4;
		TeleportEffect.Play(player, host, destination, preview.TeleportDeparture, preview.TeleportArrival, 42);
		AssertBool(player.GlobalPosition.IsEqualApprox(origin)).IsTrue();
		await runner.SimulateFrames(7, 20);
		AssertBool(player.GlobalPosition.IsEqualApprox(origin)).IsTrue();
		var departure = host.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single(e => e.Definition == preview.TeleportDeparture);
		AssertObject(departure.SurfaceEnvelope).IsNotNull();
		await runner.SimulateFrames(38, 20);
		AssertBool(player.GlobalPosition.IsEqualApprox(destination)).IsTrue(); AssertInt(host.ActiveInstanceCount).IsEqual(1);
		// The iron skin shows again once the traveler has arrived.
		var head = player.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First(m => m.Name.ToString().Contains("Head"));
		var skin = ((ShaderMaterial)head.GetActiveMaterial(0)).GetShaderParameter("skin_kind");
		AssertBool(skin.VariantType == Variant.Type.Int && skin.AsInt32() == 0).IsTrue();
	}
	[TestCase]
	public async Task InterruptingATeleportCommitsItsDestination()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects(); var player = preview.Stage.GetChild<Player>(0);
		// Clearing the effects completes the travel without leaving an arrival behind.
		Vector3 destination = player.GlobalPosition + Vector3.Right * 4;
		TeleportEffect.Play(player, player.WorldEffects, destination, preview.TeleportDeparture, preview.TeleportArrival, 42);
		preview.ClearEffects(); AssertBool(player.GlobalPosition.IsEqualApprox(destination)).IsTrue(); AssertInt(preview.InstanceCount).IsEqual(0);
		// Saving mid-teleport records where the traveler is going, not where it stands.
		destination = player.GlobalPosition + Vector3.Back * 4;
		TeleportEffect.Play(player, player.WorldEffects, destination, preview.TeleportDeparture, preview.TeleportArrival, 42);
		var saved = PlayerSaveCodec.Capture(player, true);
		AssertBool(player.GlobalPosition.IsEqualApprox(destination)).IsTrue(); AssertInt(preview.InstanceCount).IsEqual(0);
		AssertFloat(saved.Position.Z).IsEqual(destination.Z);
	}
	[TestCase]
	public async Task SkinStyleOptionsOnlyModifyPrivatePreviewMaterials()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.SelectEffect(0); preview.SetSurfaceStyle(true, true);
		var head = preview.Stage.GetChild(0).FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First(m => m.Name.ToString().Contains("Head"));
		var material = (ShaderMaterial)head.GetActiveMaterial(0);
		AssertBool(material.GetShaderParameter("faceted").AsBool()).IsTrue(); AssertFloat(material.GetShaderParameter("polish").AsSingle()).IsEqual(1);
		var definition = TestFactories.Load<WorldEffectDefinition>("res://scenes/effects/world/iron_skin/iron_skin.tres");
		var template = AutoFree(definition.Scene.Instantiate<WorldEffectInstance>());
		AssertBool(template.Surface.Material.GetShaderParameter("faceted").AsBool()).IsFalse();
	}
	[TestCase]
	public async Task DeathCancelsRelocationAndCloakRetainsPriorityOverTheFade()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects(); var player = preview.Stage.GetChild<Player>(0);
		Vector3 origin = player.GlobalPosition;
		TeleportEffect.Play(player, player.WorldEffects, origin + Vector3.Right * 4, preview.TeleportDeparture, preview.TeleportArrival, 42);
		player.Invisibility.SetInvisible(true, true);
		var head = player.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First(m => m.Name.ToString().Contains("Head"));
		AssertBool(((ShaderMaterial)head.GetActiveMaterial(0)).Shader.Code.Contains("body_opacity")).IsTrue();
		player.HealthComponent.TakeDamage(100000);
		await runner.SimulateFrames(24, 20);
		AssertBool(player.GlobalPosition.IsEqualApprox(origin)).IsTrue(); AssertInt(preview.InstanceCount).IsEqual(0);
	}
	[TestCase]
	public async Task MonsterSilhouettesUseTheDebugLayerAndStayOutOfTheWorldReflectionProbe()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene();
		foreach (int subject in new[] { 1, 2 })
		{
			preview.SetSubject(subject); await runner.SimulateFrames(2, 20);
			var helpers = preview.Stage.GetChild(0).FindChildren("*_Xray", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
			AssertInt(helpers.Length).IsGreater(0);
			foreach (var helper in helpers)
			{
				// Each silhouette traces a real body mesh, but renders only on the debug layer the world probe never sees.
				AssertBool(helper.GetParent().GetChildren().OfType<MeshInstance3D>().Any(m => m.Mesh == helper.Mesh && !VisualHelper.IsHelper(m))).IsTrue();
				AssertBool(helper.Layers == VisualHelper.DebugRenderLayer).IsTrue(); AssertInt((int)(helper.Layers & 1)).IsEqual(0);
			}
		}
	}
	[TestCase]
	public async Task TeleportFadePassesArePrivateAndShareTheSameFadeAmount()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects(); var player = preview.Stage.GetChild<Player>(0);
		player.WorldEffects.PlayBurst(preview.TeleportDeparture, 42);
		var effect = player.WorldEffects.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single(e => !e.IsQueuedForDeletion());
		effect.Seek(.15f);
		var head = player.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First(m => m.Name.ToString().Contains("Head"));
		var depth = (ShaderMaterial)head.GetActiveMaterial(0); var color = (ShaderMaterial)depth.NextPass;
		AssertFloat(depth.GetShaderParameter("amount").AsSingle()).IsEqual(color.GetShaderParameter("amount").AsSingle());
		AssertBool(color != effect.Surface.Material.NextPass).IsTrue();
	}
	[TestCase]
	public async Task ActorAndScrollLightsShareTheirWorldOffsetWhileActorsTurnAndMove()
	{
		using var runner = TestFactories.LoadScene(Scene); await runner.SimulateFrames(2, 20);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.SelectEffect(2);
		var actor = preview.Stage.GetChild<Player>(0);
		var effect = actor.WorldEffects.InstancesRoot.GetChildren().OfType<WorldEffectInstance>().Single(e => !e.IsQueuedForDeletion());
		var ring = AutoFree(TestFactories.Load<PackedScene>("res://scenes/effects/world/runes/rune_ring.tscn").Instantiate<WorldEffectInstance>());
		AssertBool((effect.AccentLight.GlobalPosition - effect.GlobalPosition).IsEqualApprox(ring.AccentLight.Position)).IsTrue();
		actor.RotateY(1.3f); actor.Position += Vector3.Right * 3;
		effect.Seek(.15f);
		AssertBool((effect.AccentLight.GlobalPosition - effect.GlobalPosition).IsEqualApprox(ring.AccentLight.Position)).IsTrue();
	}
}