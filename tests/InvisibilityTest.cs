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
		var component = GD.Load<PackedScene>("res://scenes/components/invisibility_component.tscn").Instantiate<InvisibilityComponent>();
		component.Target = target; component.Health = health; target.AddChild(component); return component;
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
		using var runner = ISceneRunner.Load(Harness, true); await runner.SimulateFrames(2);
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
	public async Task HarnessAnimatesRealActorsAndTogglesWithoutLeavingShadersOrXrayLeaks()
	{
		using var runner = ISceneRunner.Load(Harness, true); await runner.SimulateFrames(2);
		var harness = (WorldEffectHarness)runner.Scene(); harness.SelectEffect(4); await runner.SimulateFrames(2);
		var player = harness.PreviewPlayer; var enemy = harness.PreviewMonster;
		AssertBool(player.Invisibility.IsInvisible && enemy.Invisibility.IsInvisible).IsTrue();
		var body = player.Invisibility.Target.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
		AssertBool(body.Length > 2).IsTrue(); // Includes runtime-equipped gear.
		AssertBool(body.All(m => m.GetActiveMaterial(0) is ShaderMaterial)).IsTrue();
		AssertBool(enemy.HealthBar.Visible).IsFalse();
		enemy.HealthComponent.TakeDamage(1); AssertBool(enemy.HealthBar.Visible).IsFalse();
		var helpers = enemy.Pivot.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
			.Where(m => VisualHelper.IsHelper(m)).ToArray();
		AssertBool(helpers.Length > 0).IsTrue(); AssertBool(helpers.All(m => !m.Visible)).IsTrue();
		harness.Replay(); await runner.SimulateFrames(25, 20);
		AssertBool(player.Invisibility.IsInvisible || enemy.Invisibility.IsInvisible).IsFalse();
		AssertBool(body.All(m => m.GetActiveMaterial(0) is BaseMaterial3D)).IsTrue();
		AssertBool(enemy.HealthBar.Visible).IsTrue();
		harness.Replay(); await runner.SimulateFrames(6, 20);
		AssertFloat(player.Invisibility.Amount).IsGreater(0); AssertFloat(player.Invisibility.Amount).IsLess(1);
		harness.Replay(); await runner.SimulateFrames(25, 20);
		AssertBool(player.Invisibility.IsInvisible).IsFalse();
		harness.SelectEffect(0); await runner.SimulateFrames(2);
		AssertBool(harness.PreviewTrap.IsDiscovered).IsFalse();
	}
	[TestCase]
	public void PlayerAndAllMonsterAssembliesExplicitlyAssignTheSharedComponent()
	{
		foreach (string path in new[] { "player/player", "enemies/skeleton/skeleton_minion", "enemies/skeleton/skeleton_warrior",
			"enemies/skeleton/skeleton_crossbow", "enemies/rat/rat", "enemies/forsaken_warden/forsaken_warden",
			"enemies/templates/melee_enemy", "enemies/templates/projectile_enemy" })
		{
			var actor = GD.Load<PackedScene>($"res://scenes/{path}.tscn").Instantiate<Node3D>();
			actor.ProcessMode = Node.ProcessModeEnum.Disabled;
			TestFactories.AddToSceneTree(actor);
			var cloak = actor.GetNode<InvisibilityComponent>("InvisibilityComponent");
			AssertObject(cloak.Target).IsSame(actor.GetNode<Node3D>("Pivot"));
			AssertObject(cloak.EffectMaterial.Shader).IsNotNull(); AssertBool(cloak.StartInvisible).IsFalse();
		}
	}
}