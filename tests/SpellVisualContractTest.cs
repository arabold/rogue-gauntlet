namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Contracts every authored spell visual shares, checked across all magic scenes rather than per workbench: a
/// timeline owns instance-local shader materials, and a finite timeline can be clipped to a cast's area and ends
/// its effect. Also checks that the captured area clip stops at walls.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class SpellVisualContractTest
{
	private static readonly string[] AreaClipUniforms = ["area_clip_enabled", "area_visibility", "area_origin", "area_extent"];

	[TestCase]
	public void EveryTimelineOwnsClippableMaterialsAndEndsItsEffect()
	{
		var failures = new List<string>();
		int checkedTimelines = 0;
		foreach (string path in SceneFiles("res://scenes/magic"))
		{
			var scene = TestFactories.Load<PackedScene>(path);
			var probe = scene.Instantiate();
			bool animated = Timelines(probe).Length > 0;
			probe.Free();
			if (!animated) continue;

			var first = TestFactories.AddToSceneTree(scene.Instantiate());
			var second = TestFactories.AddToSceneTree(scene.Instantiate());
			var firstTimelines = Timelines(first);
			var secondTimelines = Timelines(second);
			for (int i = 0; i < firstTimelines.Length; i++)
			{
				checkedTimelines++;
				var timeline = firstTimelines[i];
				string name = $"{path} {first.GetPathTo(timeline)}";
				var materials = Materials(timeline);
				if (materials.Any(material => material == null))
				{
					failures.Add($"{name} drives a surface or emitter without a shader material");
					continue;
				}
				if (materials.Zip(Materials(secondTimelines[i])).Any(pair => pair.First == pair.Second))
					failures.Add($"{name} shares a material between two instances");

				// Sustained guards follow their caster and are never clipped; every finite effect may be.
				if (timeline.Sustained) continue;
				foreach (var material in materials)
				{
					var uniforms = material.Shader.GetShaderUniformList()
						.Select(uniform => uniform.AsGodotDictionary()["name"].AsString()).ToHashSet();
					string missing = string.Join(", ", AreaClipUniforms.Where(uniform => !uniforms.Contains(uniform)));
					if (missing.Length > 0) failures.Add($"{name}: {material.Shader.ResourcePath} lacks {missing}");
				}
				timeline._Process(timeline.StartDelay + timeline.Duration + .01f);
				if (timeline.FreeOnCompletion ? !timeline.GetParent().IsQueuedForDeletion() : timeline.IsProcessing())
					failures.Add($"{name} does not end after its {timeline.Duration}s duration");
			}
		}

		AssertInt(checkedTimelines).IsGreater(0);
		AssertArray(failures).OverrideFailureMessage(string.Join("\n  ", failures)).IsEmpty();
	}

	[TestCase]
	public async Task AreaClipStopsAtWallsAndStaysOpenElsewhere()
	{
		// Far from the origin so bodies other suites leave behind cannot block the rays.
		var origin = new Vector3(500, 0, 500);
		var root = TestFactories.AddToSceneTree(new Node3D());
		var wall = new StaticBody3D { Position = origin + new Vector3(3, 1, 0), CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 4) } });
		root.AddChild(wall);
		await root.ToSignal(root.GetTree(), SceneTree.SignalName.PhysicsFrame);
		await root.ToSignal(root.GetTree(), SceneTree.SignalName.PhysicsFrame);

		var mask = AreaVisibility.Capture(root.GetWorld3D(), origin, 8);
		AssertBool(mask.Contains(origin + new Vector3(2, 0, 0))).IsTrue();
		AssertBool(mask.Contains(origin + new Vector3(5, 0, 0))).IsFalse();
		AssertBool(mask.Contains(origin + new Vector3(-5, 0, 0))).IsTrue();
		AssertBool(mask.Contains(origin + new Vector3(0, 0, 9))).IsFalse();
	}

	private static IEnumerable<string> SceneFiles(string directory)
	{
		foreach (string file in DirAccess.GetFilesAt(directory).Where(file => file.EndsWith(".tscn")))
			yield return $"{directory}/{file}";
		foreach (string child in DirAccess.GetDirectoriesAt(directory))
			foreach (string file in SceneFiles($"{directory}/{child}"))
				yield return file;
	}

	private static VisualEffectTimeline[] Timelines(Node root) =>
		new[] { root }.Concat(root.FindChildren("*", "", true, false)).OfType<VisualEffectTimeline>().ToArray();

	private static ShaderMaterial[] Materials(VisualEffectTimeline timeline) =>
		timeline.Surfaces.Select(surface => surface?.MaterialOverride as ShaderMaterial)
			.Concat(timeline.Emitters.Select(emitter => emitter?.MaterialOverride as ShaderMaterial)).ToArray();
}
