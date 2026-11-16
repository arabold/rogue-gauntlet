namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Darkness removes environmental illumination and restores it without changing another world's resource.</summary>
[TestSuite, RequireGodotRuntime]
public class WorldLightingTest
{
	[TestCase]
	public void DarknessRestoresHazeAndReflectionsWithoutChangingAnotherWorld()
	{
		var authored = new Godot.Environment
		{
			AmbientLightEnergy = .4f, FogEnabled = true, VolumetricFogEnabled = true,
			ReflectedLightSource = Godot.Environment.ReflectionSource.Sky
		};
		var dark = new WorldLighting { Environment = authored, IsDark = true };
		var lit = new WorldLighting { Environment = authored };
		var world = new Node();
		var first = new SubViewport { OwnWorld3D = true };
		var second = new SubViewport { OwnWorld3D = true };
		first.AddChild(dark);
		second.AddChild(lit);
		world.AddChild(first);
		world.AddChild(second);
		TestFactories.AddToSceneTree(world);

		AssertFloat(dark.Environment.AmbientLightEnergy).IsZero();
		AssertBool(dark.Environment.FogEnabled).IsFalse();
		AssertBool(dark.Environment.VolumetricFogEnabled).IsFalse();
		AssertThat(dark.Environment.ReflectedLightSource).IsEqual(Godot.Environment.ReflectionSource.Disabled);
		AssertBool(lit.Environment.FogEnabled).IsTrue();
		AssertBool(lit.Environment.VolumetricFogEnabled).IsTrue();
		AssertFloat(lit.Environment.AmbientLightEnergy).IsEqual(.4f);
		AssertBool(authored.FogEnabled).IsTrue();
		AssertBool(authored.VolumetricFogEnabled).IsTrue();
		AssertThat(authored.ReflectedLightSource).IsEqual(Godot.Environment.ReflectionSource.Sky);

		dark.IsDark = false;
		lit.IsDark = true;
		AssertFloat(dark.Environment.AmbientLightEnergy).IsEqual(.4f);
		AssertBool(dark.Environment.FogEnabled).IsTrue();
		AssertBool(dark.Environment.VolumetricFogEnabled).IsTrue();
		AssertThat(dark.Environment.ReflectedLightSource).IsEqual(Godot.Environment.ReflectionSource.Sky);
		AssertBool(lit.Environment.FogEnabled).IsFalse();
		AssertBool(lit.Environment.VolumetricFogEnabled).IsFalse();

		dark.FogEnabled = false;
		dark.IsDark = true;
		dark.IsDark = false;
		AssertBool(dark.Environment.FogEnabled).IsFalse();
		AssertBool(dark.Environment.VolumetricFogEnabled).IsFalse();
		dark.FogEnabled = true;
		AssertBool(dark.Environment.FogEnabled).IsTrue();
		AssertBool(dark.Environment.VolumetricFogEnabled).IsTrue();
	}
}