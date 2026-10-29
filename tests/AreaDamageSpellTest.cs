namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The shared area spell resolves contacts and displays each authored visual.</summary>
[TestSuite, RequireGodotRuntime]
public partial class AreaDamageSpellTest
{
	[TestCase("Rock Spikes", 60)]
	[TestCase("Thunderstorm", 3)]
	public async Task AdditionalAreaSpellsUseTheirAuthoredContactTiming(string spellName, int frames)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), harness.Player.Position + Vector3.Right * 2, "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		TestFactories.CastHarnessSpell(harness, spellName, 1);
		await runner.SimulateFrames((uint)frames);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(47);
		if (spellName == "Thunderstorm")
		{
			var visual = runner.Scene().GetChildren().OfType<ThunderstormVisual>().Single();
			AssertBool(visual.GetChildren().OfType<LightningArc>().Any()).IsTrue();
			AssertBool(visual.GetChildren().OfType<VisualEffectInstance>().Any()).IsTrue();
		}
	}

	[TestCase]
	public async Task ThunderstormLetsItsGroundCracksFinishAfterTheWave()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		TestFactories.CastHarnessSpell(harness, "Thunderstorm", 1); await runner.SimulateFrames(3);
		var visual = runner.Scene().GetChildren().OfType<ThunderstormVisual>().Single();
		for (int i = 0; i < 300 && GodotObject.IsInstanceValid(visual) && visual.Timeline.Progress < 1; i++) await runner.SimulateFrames(1);
		AssertBool(GodotObject.IsInstanceValid(visual)).IsTrue();
		AssertBool(visual.GetChildren().OfType<VisualEffectInstance>().Any()).IsTrue();
		for (int i = 0; i < 400 && GodotObject.IsInstanceValid(visual); i++) await runner.SimulateFrames(1);
		AssertBool(GodotObject.IsInstanceValid(visual)).IsFalse();
	}

	[TestCase]
	public async Task DelayedAreaHitsOccupantsAtImpactUsingTheOriginalCenter()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var origin = harness.Player.GlobalPosition;
		var leaving = TestFactories.SpawnCombatMonster(runner.Scene(), origin + Vector3.Right * 2, "Leaving");
		var entering = TestFactories.SpawnCombatMonster(runner.Scene(), origin + Vector3.Left * 7, "Entering");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(leaving); TestFactories.PrepareCombatMonster(entering);
		TestFactories.CastHarnessSpell(harness, "Rock Spikes", 1);
		await runner.SimulateFrames(3);
		leaving.GlobalPosition = origin + Vector3.Right * 7;
		entering.GlobalPosition = origin + Vector3.Left * 2;
		harness.Player.GlobalPosition = origin + Vector3.Back * 10;
		await runner.SimulateFrames(40);
		AssertFloat(leaving.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(entering.HealthComponent.CurrentHealth).IsEqual(47);
	}

	[TestCase("Gust")]
	[TestCase("Frost Nova")]
	public async Task OtherAreaSpellsCanUseTheSameContactTiming(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var origin = harness.Player.GlobalPosition;
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), origin + Vector3.Right * 7, "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		if (name == "Gust")
		{
			var spell = harness.SpellComponents.OfType<GustSpell>().Single();
			spell.Definition = (GustDefinition)spell.Definition.Duplicate(); spell.Definition.ContactDelay = .3f;
		}
		else
		{
			var spell = harness.SpellComponents.OfType<FrostNovaSpell>().Single();
			spell.Definition = (FrostNovaDefinition)spell.Definition.Duplicate(); spell.Definition.ContactDelay = .3f;
		}
		TestFactories.CastHarnessSpell(harness, name, 1); await runner.SimulateFrames(3);
		target.GlobalPosition = origin + Vector3.Right * 2;
		await runner.SimulateFrames(40);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(47);
		if (name == "Frost Nova") AssertBool(target.Frozen).IsTrue();
	}

	[TestCase]
	public async Task CancelingAnEruptionBeforeContactPreventsDamage()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), harness.Player.Position + Vector3.Right * 2, "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		TestFactories.CastHarnessSpell(harness, "Rock Spikes", 1);
		await runner.SimulateFrames(3);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
		harness.SpellComponents.OfType<AreaDamageSpell>().Single(spell => spell.DisplayName == "Rock Spikes").Cancel();
		await runner.SimulateFrames(25);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
	}

	[TestCase]
	public async Task AQueuedCastKeepsItsOriginalSelection()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), harness.Player.Position + Vector3.Right * 2, "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		TestFactories.CastHarnessSpell(harness, "Flame Burst", 1);
		var selector = harness.GetNode<OptionButton>("%Controls/Spell");
		int tremor = Enumerable.Range(0, selector.ItemCount).Single(i => selector.GetItemText(i) == "Tremor");
		selector.Select(tremor); selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)tremor);
		harness.GetNode<OptionButton>("%Controls/SpellLevel").Select(2);
		await runner.SimulateFrames(3);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(47);
		AssertInt(runner.Scene().GetChildren().OfType<FireSpellVisual>().Count()).IsEqual(1);
		AssertInt(runner.Scene().GetChildren().OfType<EarthSpellVisual>().Count()).IsEqual(0);
	}

	[TestCase("Flame Burst")]
	[TestCase("Tremor")]
	public async Task HarnessCastsOneContactPerVisibleEnemy(string spellName)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var root = runner.Scene(); harness.Player.GlobalPosition = Vector3.Zero;
		harness.Player.InputController.SetPhysicsProcess(false); harness.Player.MovementComponent.SetPhysicsProcess(false);
		var hit = TestFactories.SpawnCombatMonster(root, new Vector3(2, 0, 0), "Hit");
		var blocked = TestFactories.SpawnCombatMonster(root, new Vector3(-2, 0, 0), "Blocked");
		var distant = TestFactories.SpawnCombatMonster(root, new Vector3(7, 0, 0), "Distant");
		var wall = new StaticBody3D { Position = new Vector3(-1, 1, 0), CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 2) } }); root.AddChild(wall);
		await runner.SimulateFrames(5);
		foreach (var actor in new[] { hit, blocked, distant }) TestFactories.PrepareCombatMonster(actor);
		float playerHealth = harness.Player.HealthComponent.CurrentHealth;
		TestFactories.CastHarnessSpell(harness, spellName, 1); await runner.SimulateFrames(3);
		AssertFloat(hit.HealthComponent.CurrentHealth).IsEqual(47);
		AssertFloat(blocked.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(distant.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(playerHealth);
		if (spellName == "Flame Burst")
		{
			var visual = root.GetChildren().OfType<FireSpellVisual>().Single();
			AssertFloat(visual.PreviewRadius).IsEqualApprox(3.5, .001);
			AssertBool(visual.Visibility != null).IsTrue();
		}
		else
		{
			var visual = root.GetChildren().OfType<EarthSpellVisual>().Single();
			AssertFloat(visual.PreviewRadius).IsEqualApprox(3.5, .001);
			AssertBool(visual.Visibility != null).IsTrue();
		}
	}

	[TestCase("Flame Burst")]
	[TestCase("Tremor")]
	public async Task HigherLevelUsesItsOwnRadiusAndCancellationStopsFurtherContacts(string spellName)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var root = runner.Scene(); harness.Player.GlobalPosition = Vector3.Zero;
		var first = TestFactories.SpawnCombatMonster(root, new Vector3(5.5f, 0, 0), "AFirst");
		var second = TestFactories.SpawnCombatMonster(root, new Vector3(0, 0, 5.5f), "BSecond");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		var spell = harness.SpellComponents.OfType<AreaDamageSpell>().Single(s => s.DisplayName == spellName);
		first.HurtBoxComponent.DamageResolved += (_, _, _) => spell.Cancel();
		TestFactories.CastHarnessSpell(harness, spellName, 3); await runner.SimulateFrames(3);
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(45);
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(50);
	}
}
