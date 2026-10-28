namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Three fireball flights select distinct targets and never damage cosmetic or unselected owners.</summary>
[TestSuite, RequireGodotRuntime]
public partial class FireballSpellTest
{
	[TestCase]
	public async Task FireballHitsOnlyDistinctSelectedOwners()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/fireball_preview.tscn", true);
		await runner.SimulateFrames(5);
		var scene = runner.Scene();
		var spell = scene.GetNode<FireballSpell>("FireballSpell");
		var targets = scene.GetChildren().OfType<Node3D>().Where(n => n.IsInGroup("enemy")).ToArray();
		AssertInt(targets.Length).IsEqual(3);
		AssertInt(spell.Cast(2, targets, 12)).IsEqual(2);
		await runner.SimulateFrames(75);
		var health = targets.Select(t => t.GetNode<HealthComponent>("HealthComponent").CurrentHealth).ToArray();
		AssertInt(health.Count(h => h < 100)).IsEqual(2);
		AssertInt(health.Count(h => Mathf.IsEqualApprox(h, 89))).IsEqual(2);
		var before = targets.Select(t => t.GetNode<HealthComponent>("HealthComponent").CurrentHealth).ToArray();
		AssertInt(spell.Cast(3, targets.Take(1), 15)).IsEqual(1);
		await runner.SimulateFrames(75);
		AssertFloat(targets[0].GetNode<HealthComponent>("HealthComponent").CurrentHealth).IsEqualApprox(before[0] - 14, .01);
		AssertFloat(targets[1].GetNode<HealthComponent>("HealthComponent").CurrentHealth).IsEqualApprox(before[1], .01);
		AssertFloat(targets[2].GetNode<HealthComponent>("HealthComponent").CurrentHealth).IsEqualApprox(before[2], .01);
	}
	[TestCase]
	public async Task FireballImpactAnimatesAndCleansUp()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/fireball_preview.tscn", true);
		await runner.SimulateFrames(3);
		var effect = GD.Load<PackedScene>("res://scenes/magic/fireball/fireball_impact.tscn").Instantiate<FireballImpact>();
		runner.Scene().AddChild(effect);
		var material = (ShaderMaterial)effect.FlameLayers[0].MaterialOverride;
		await runner.SimulateFrames(12);
		AssertBool(material.GetShaderParameter("progress").AsSingle() > 0).IsTrue();
		await runner.Scene().ToSignal(runner.Scene().GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
		await runner.SimulateFrames(2);
		AssertBool(GodotObject.IsInstanceValid(effect)).IsFalse();
	}

	[TestCase]
	public async Task WallImpactCentersItsRingOnTheWall()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/fireball_preview.tscn", true);
		await runner.SimulateFrames(4);
		var scene = runner.Scene();
		var spell = scene.GetNode<FireballSpell>("FireballSpell");
		AssertBool(spell.PreviewWall(3, new Vector3(-9, 1, 0))).IsTrue();
		await scene.ToSignal(scene.GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
		var impact = scene.GetChildren().OfType<FireballImpact>().FirstOrDefault();
		AssertObject(impact).IsNotNull();
		AssertFloat(Mathf.Abs(impact.GlobalBasis.Z.Normalized().Dot(Vector3.Right))).IsGreater(.99f);
		var ring = impact.GetNode<MeshInstance3D>("FacetedRing");
		AssertFloat(ring.Position.Length()).IsEqualApprox(0, .001);
		AssertFloat(Mathf.Abs(ring.Basis.Y.Normalized().Dot(Vector3.Forward))).IsGreater(.99f);
	}

}