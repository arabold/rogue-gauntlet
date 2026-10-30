namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Real actor/gear coverage, reversible material ownership, shadows and presentation gates.</summary>
[TestSuite, RequireGodotRuntime]
public class InvisibilityTest
{
	private const string Harness = "res://scenes/debug/effects/world_effect_harness.tscn";
	private static InvisibilityComponent Cloak(Node3D target, HealthComponent health = null)
	{
		var component = TestFactories.Load<PackedScene>("res://scenes/components/invisibility_component.tscn").Instantiate<InvisibilityComponent>();
		var surface = new SurfaceAppearanceController { Target = target }; target.AddChild(surface);

		var host = new WorldEffectHost { InstancesRoot = target, OriginAnchor = target, SurfaceController = surface, Health = health };
		var statuses = new BuffController { Health = health }; target.AddChild(statuses);
		component.Statuses = statuses; component.Host = host; target.AddChild(host); target.AddChild(component); return component;
	}
	[TestCase]
	public void MaterialsArePrivateAndRestoreExactlyWithoutChangingVisibilityOrCollision()
	{
		var root = TestFactories.AddToSceneTree(new Node3D());
		var authored = new StandardMaterial3D { AlbedoColor = Colors.Red };
		var overlay = new StandardMaterial3D { AlbedoColor = Colors.Blue };
		var mesh = new MeshInstance3D { Mesh = new BoxMesh { Material = authored }, MaterialOverlay = overlay,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided, Visible = false };
		root.AddChild(mesh);
		var other = new MeshInstance3D { Mesh = mesh.Mesh }; root.AddChild(other);
		var actor = new CharacterBody3D { CollisionLayer = 8, CollisionMask = 3 }; root.AddChild(actor);
		var cloak = Cloak(mesh); cloak.SetInvisible(true, true);
		AssertBool(cloak.IsInvisible).IsTrue(); AssertFloat(cloak.Amount).IsEqual(1);
		AssertBool(mesh.Visible).IsFalse(); AssertInt((int)actor.CollisionLayer).IsEqual(8);
		AssertBool(mesh.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off).IsTrue();
		AssertObject(mesh.MaterialOverlay).IsNull(); AssertObject(other.GetActiveMaterial(0)).IsSame(authored);
		var shader = (ShaderMaterial)mesh.GetActiveMaterial(0);
		AssertFloat(shader.GetShaderParameter("amount").AsSingle()).IsEqual(1);
		AssertObject(mesh.Mesh.SurfaceGetMaterial(0)).IsSame(authored);
		cloak.SetInvisible(false, true);
		AssertObject(mesh.GetActiveMaterial(0)).IsSame(authored); AssertObject(mesh.MaterialOverlay).IsSame(overlay);
		AssertBool(mesh.CastShadow == GeometryInstance3D.ShadowCastingSetting.DoubleSided).IsTrue();
		AssertBool(mesh.Visible).IsFalse(); AssertBool(cloak.IsInvisible).IsFalse();
	}
	[TestCase]
	public async Task AddedEquipmentGetsTheCurrentCloakAndDeathRestoresBeforeCorpseFade()
	{
		using var runner = TestFactories.LoadScene(Harness); await runner.SimulateFrames(2);
		var root = TestFactories.AddToSceneTree(new Node3D());
		var body = new MeshInstance3D { Mesh = new BoxMesh() }; root.AddChild(body);
		var health = new HealthComponent(); root.AddChild(health); health.Owner = root;
		var cloak = Cloak(root, health); cloak.SetInvisible(true, true);
		var original = new StandardMaterial3D { AlbedoColor = Colors.Green };
		var gear = new MeshInstance3D { Mesh = new SphereMesh(), MaterialOverride = original }; root.AddChild(gear);
		await runner.SimulateFrames(2);
		AssertBool(gear.GetActiveMaterial(0) is ShaderMaterial).IsTrue();
		AssertFloat(((ShaderMaterial)gear.GetActiveMaterial(0)).GetShaderParameter("amount").AsSingle()).IsEqual(1);
		health.TakeDamage(100);
		AssertBool(cloak.IsInvisible).IsFalse(); AssertObject(gear.MaterialOverride).IsSame(original);
		AssertBool(gear.CastShadow == GeometryInstance3D.ShadowCastingSetting.On).IsTrue();
	}
	[TestCase]
	public async Task ActorPreviewUsesTheSharedInvisibilityDefinitionAndCleanup()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn"); await runner.SimulateFrames(2);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.SelectEffect(10);
		foreach (int subject in new[] { 0, 1, 2 })
		{
			preview.SetSubject(subject); await runner.SimulateFrames(20, 20);
			var actor = preview.Stage.GetChild<Node3D>(0); var cloak = actor.GetNode<InvisibilityComponent>("InvisibilityComponent");
			AssertBool(cloak.IsInvisible).IsFalse(); AssertBool(cloak.Host.SurfaceController.HasCloak).IsTrue(); AssertFloat(cloak.Amount).IsEqual(1);
			AssertInt(preview.InstanceCount).IsEqual(1);
			preview.ClearEffects(); AssertBool(cloak.IsInvisible).IsFalse(); AssertInt(preview.InstanceCount).IsEqual(0);
		}
		preview.SetSubject(0); var player = preview.Stage.GetChild<Player>(0); preview.ClearEffects();
		player.Invisibility.SetInvisible(true); await runner.SimulateFrames(6, 20);
		AssertFloat(player.Invisibility.Amount).IsGreater(0); AssertFloat(player.Invisibility.Amount).IsLess(1);
		player.Invisibility.SetInvisible(false); await runner.SimulateFrames(20, 20);
		AssertBool(player.Invisibility.IsInvisible).IsFalse();
	}
	[TestCase]
	public async Task ReversingTheSharedCloakKeepsItsCurrentAmountAndOneInstance()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/actor_item_effect_preview.tscn"); await runner.SimulateFrames(2);
		var preview = (ActorItemEffectPreview)runner.Scene(); preview.ClearEffects(); var player = preview.Stage.GetChild<Player>(0);
		player.Invisibility.SetInvisible(true); await runner.SimulateFrames(8, 20);
		player.Invisibility.SetInvisible(false); await runner.SimulateFrames(3, 20);
		float amount = player.Invisibility.Amount;
		player.Invisibility.SetInvisible(true);
		AssertFloat(player.Invisibility.Amount).IsEqual(amount); AssertInt(preview.InstanceCount).IsEqual(1);
		await runner.SimulateFrames(14, 20); AssertFloat(player.Invisibility.Amount).IsEqual(1);
		player.Invisibility.SetInvisible(false, true); AssertBool(player.Invisibility.IsInvisible).IsFalse();
	}
	[TestCase]
	public void ImmediateHostRequestsPreserveOtherOwnersAndCanFinishARetiringCloak()
	{
		var mesh = TestFactories.AddToSceneTree(new MeshInstance3D { Mesh = new BoxMesh() });
		var cloak = Cloak(mesh); var host = cloak.Host; var definition = cloak.Effect;
		var first = new object(); var second = new object();
		host.SetSustained(first, definition, true, 42, immediate: true);
		AssertFloat(cloak.Amount).IsEqual(1);
		host.SetSustained(second, definition, true, 42);
		host.SetSustained(first, definition, false, 42, immediate: true);
		AssertBool(host.SurfaceController.HasCloak).IsTrue(); AssertInt(host.LeaseCount).IsEqual(1);
		host.SetSustained(second, definition, false, 42);
		AssertBool(host.SurfaceController.HasCloak).IsTrue(); AssertInt(host.LeaseCount).IsEqual(0);
		host.SetSustained(second, definition, false, 42, immediate: true);
		AssertBool(host.SurfaceController.HasCloak).IsFalse(); AssertInt(host.ActiveInstanceCount).IsEqual(0);
	}
	// Every actor assembly's cloak wiring is checked with its other statuses in ActorStatusInspectorTest.
}
