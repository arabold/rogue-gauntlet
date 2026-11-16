namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using static GdUnit4.Assertions;

/// <summary>Ambient movement is opt-in, source-preserving and instance-local, including world injection after placement.</summary>
[TestSuite, RequireGodotRuntime]
public class AmbientMotionTest
{
	[TestCase]
	public void WorldWindOverrideRestoresStaticSurfacesAndStopsBothClocks()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/levels/level.tscn");
		var template = TestFactories.Load<ShaderMaterial>("res://scenes/effects/ambient_motion/ambient_motion.tres");
		var level = scene.Instantiate<Level>();
		level.AutoGenerateOnReady = false;
		level.Lighting.DisabledEffects = EnvironmentalEffect.Wind;
		var original = new StandardMaterial3D();
		var target = new MeshInstance3D { Mesh = new BoxMesh { Material = original } };
		var motion = new AmbientMotionComponent
		{
			Target = target, Airflow = level.Airflow, Material = template, Profile = new AmbientMotionProfile()
		};
		target.AddChild(motion);
		level.AddChild(target);
		TestFactories.AddToSceneTree(level);
		AssertObject(target.GetActiveMaterial(0)).IsSame(original);
		AssertBool(motion.IsProcessing() || level.Airflow.IsProcessing()).IsFalse();
		level.Lighting.DisabledEffects = EnvironmentalEffect.None;
		var animated = target.GetActiveMaterial(0);
		AssertObject(animated).IsInstanceOf<ShaderMaterial>();
		AssertBool(motion.IsProcessing() && level.Airflow.IsProcessing()).IsTrue();
		level.Lighting.DisabledEffects = EnvironmentalEffect.Reflections;
		AssertObject(target.GetActiveMaterial(0)).IsSame(animated);
		level.Lighting.DisabledEffects |= EnvironmentalEffect.Wind;
		AssertObject(target.GetActiveMaterial(0)).IsSame(original);
		AssertFloat(target.ExtraCullMargin).IsZero();
		AssertBool(motion.IsProcessing() || level.Airflow.IsProcessing()).IsFalse();
		AssertBool(motion.Enabled && level.Airflow.Enabled).IsTrue();
		level.Lighting.DisabledEffects = EnvironmentalEffect.None;
		AssertObject(target.GetActiveMaterial(0)).IsInstanceOf<ShaderMaterial>();
		AssertBool(motion.IsProcessing() && level.Airflow.IsProcessing()).IsTrue();
	}

	[TestCase("res://scenes/levels/rooms/props/wall/banner_red.tres")]
	[TestCase("res://scenes/levels/rooms/props/plants/fern.tres")]
	public void PlacedPropsWaitForTheirWorldAndAnimateInWorldDirection(string path)
	{
		var definition = TestFactories.Load<Prop>(path);
		var sourceScene = TestFactories.Load<PackedScene>("res://scenes/effects/ambient_motion/airflow.tscn");
		var target = TestFactories.AddToSceneTree(RoomBlueprint.CreateNode(new(definition, 0, Transform3D.Identity, default)));
		var motion = target.GetChildren().OfType<AmbientMotionComponent>().Single();
		var mesh = target.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First();
		var original = mesh.GetActiveMaterial(0);
		AssertBool(motion.IsProcessing()).IsFalse();
		target.GlobalTransform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), new Vector3(12, 0, 8));
		target.ResetPhysicsInterpolation();
		var source = TestFactories.AddToSceneTree(sourceScene.Instantiate<Airflow>());
		source.Bind(target);
		source._Process(.7);
		motion._Process(.2);
		var material = (ShaderMaterial)mesh.GetActiveMaterial(0);
		Vector3 worldWind = target.GlobalBasis * material.GetShaderParameter("motion_wind").AsVector3();
		Vector2 force = source.Sample(target.GlobalPosition);
		AssertBool(worldWind.LengthSquared() > .0001f).IsTrue();
		AssertBool(worldWind.Normalized().Dot(new Vector3(force.X, 0, force.Y).Normalized()) > .99f).IsTrue();
		motion.Enabled = false;
		AssertObject(mesh.GetActiveMaterial(0)).IsSame(original);
	}

	[TestCase]
	public void WorldBindingPreservesLocalDraftsAndLiveForceEditsExpandRenderingBounds()
	{
		var template = TestFactories.Load<ShaderMaterial>("res://scenes/effects/ambient_motion/ambient_motion.tres");
		var localProfile = new AirflowProfile { Direction = Vector2.Right, Strength = .5f, GustStrength = 0 };
		var local = TestFactories.AddToSceneTree(new Airflow { Profile = localProfile });
		var world = TestFactories.AddToSceneTree(new Airflow { Profile = new AirflowProfile { Direction = Vector2.Left } });
		var target = new MeshInstance3D { Mesh = new BoxMesh { Material = new StandardMaterial3D() } };
		var motion = new AmbientMotionComponent { Target = target, Airflow = local, Material = template, Profile = new AmbientMotionProfile() };
		target.AddChild(motion);
		TestFactories.AddToSceneTree(target);
		world.Bind(target);
		motion._Process(.2);
		AssertBool(((ShaderMaterial)target.GetActiveMaterial(0)).GetShaderParameter("motion_wind").AsVector3().X > 0).IsTrue();
		float margin = target.ExtraCullMargin;
		localProfile.Strength = 2;
		AssertBool(target.ExtraCullMargin > margin).IsTrue();
	}

	[TestCase]
	public void ComponentsShareAForceButKeepIndependentResponseAndRestoreTheirMaterials()
	{
		var template = TestFactories.Load<ShaderMaterial>("res://scenes/effects/ambient_motion/ambient_motion.tres");
		var profile = TestFactories.Load<AmbientMotionProfile>("res://scenes/effects/ambient_motion/profiles/hanging_cloth.tres");
		var source = TestFactories.AddToSceneTree(new Airflow { Profile = new AirflowProfile() });
		var original = new StandardMaterial3D { AlbedoColor = Colors.Orange, Roughness = .83f };
		var mesh = new BoxMesh { Material = original };
		var first = TestFactories.AddToSceneTree(new MeshInstance3D { Mesh = mesh });
		var second = TestFactories.AddToSceneTree(new MeshInstance3D { Mesh = mesh, Position = new Vector3(2, 0, 0) });
		var a = TestFactories.AddToSceneTree(new AmbientMotionComponent { Target = first, Airflow = source, Material = template, Profile = profile });
		var b = TestFactories.AddToSceneTree(new AmbientMotionComponent { Target = second, Airflow = source, Material = template, Profile = profile });
		var materialA = (ShaderMaterial)first.GetActiveMaterial(0);
		var materialB = (ShaderMaterial)second.GetActiveMaterial(0);
		AssertObject(materialA).IsNotSame(materialB);
		AssertObject(mesh.SurfaceGetMaterial(0)).IsSame(original);
		AssertThat(materialA.GetShaderParameter("base_color").AsColor()).IsEqual(Colors.Orange);
		source._Process(.4);
		a._Process(.1);
		b._Process(.1);
		Vector3 oldWind = materialB.GetShaderParameter("motion_wind").AsVector3();
		a._Process(.3);
		AssertThat(materialB.GetShaderParameter("motion_wind").AsVector3()).IsEqual(oldWind);
		AssertBool(materialA.GetShaderParameter("motion_wind").AsVector3().LengthSquared() > oldWind.LengthSquared()).IsTrue();
		a.Enabled = false;
		AssertObject(first.GetActiveMaterial(0)).IsSame(original);
		AssertObject(second.GetActiveMaterial(0)).IsSame(materialB);
		a.Enabled = true;
		AssertObject(first.GetActiveMaterial(0)).IsInstanceOf<ShaderMaterial>();
		a.GetParent().RemoveChild(a);
		AssertObject(first.GetActiveMaterial(0)).IsSame(original);
		((SceneTree)Engine.GetMainLoop()).Root.AddChild(a);
		AssertObject(first.GetActiveMaterial(0)).IsInstanceOf<ShaderMaterial>();
		a.Free();
		AssertObject(first.GetActiveMaterial(0)).IsSame(original);
		AssertObject(mesh.SurfaceGetMaterial(0)).IsSame(original);
	}

	[TestCase]
	public void InspectorProfileEditsUpdatePrivateMaterialsAndAirflowCanBeStopped()
	{
		var template = TestFactories.Load<ShaderMaterial>("res://scenes/effects/ambient_motion/ambient_motion.tres");
		var response = new AmbientMotionProfile();
		var flow = TestFactories.AddToSceneTree(new Airflow { Profile = new AirflowProfile() });
		var target = TestFactories.AddToSceneTree(new MeshInstance3D { Mesh = new BoxMesh { Material = new StandardMaterial3D() } });
		TestFactories.AddToSceneTree(new AmbientMotionComponent { Target = target, Airflow = flow, Material = template, Profile = response });
		var material = (ShaderMaterial)target.GetActiveMaterial(0);
		response.PinnedFraction = .35f;
		response.BendAmplitude = .12f;
		AssertFloat(material.GetShaderParameter("pinned_fraction").AsSingle()).IsEqualApprox(.35f, .001f);
		AssertFloat(material.GetShaderParameter("bend_amplitude").AsSingle()).IsEqualApprox(.12f, .001f);
		AssertFloat(template.GetShaderParameter("bend_amplitude").AsSingle()).IsNotEqual(.12f);
		flow.Enabled = false;
		AssertThat(flow.Sample(Vector3.Zero)).IsEqual(Vector2.Zero);
		AssertBool(flow.IsProcessing()).IsFalse();
		flow.Enabled = true;
		AssertBool(flow.Sample(Vector3.Zero).LengthSquared() > 0).IsTrue();
	}
}