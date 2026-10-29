namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Gust uses real area contacts, defenses, wall visibility and forced movement.</summary>
[TestSuite, RequireGodotRuntime]
public partial class GustCombatTest
{
	private sealed class Defense(DamageOutcome outcome) : IDefenseEvaluator
	{
		public bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation)
		{
			evaluation = new DefenseEvaluation(outcome, 1, false, true);
			return true;
		}
	}


	[TestCase]
	public async Task HitsVisibleActorsOnceAndClipsTheVisualAtWalls()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var root = runner.Scene(); harness.Player.GlobalPosition = Vector3.Zero;
		harness.Player.InputController.SetPhysicsProcess(false); harness.Player.MovementComponent.SetPhysicsProcess(false);
		var first = TestFactories.SpawnCombatMonster(root, new Vector3(2, 0, 0), "GustFirst");
		var second = TestFactories.SpawnCombatMonster(root, new Vector3(0, 0, 2), "GustSecond");
		var blocked = TestFactories.SpawnCombatMonster(root, new Vector3(-2, 0, 0), "GustBlocked");
		var distant = TestFactories.SpawnCombatMonster(root, new Vector3(7, 0, 0), "GustDistant");
		var wall = new StaticBody3D { Position = new Vector3(-1, 1, 0), CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 2) } }); root.AddChild(wall);
		await runner.SimulateFrames(5);
		foreach (var actor in new[] { first, second, blocked, distant }) TestFactories.PrepareCombatMonster(actor);
		float playerHealth = harness.Player.HealthComponent.CurrentHealth;
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 1 })).IsTrue();
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(47);
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(47);
		AssertFloat(blocked.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(distant.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(playerHealth);
		AssertBool(first.MovementComponent.IsPushed).IsTrue(); AssertBool(second.MovementComponent.IsPushed).IsTrue();
		AssertBool(blocked.MovementComponent.IsPushed).IsFalse(); AssertBool(distant.MovementComponent.IsPushed).IsFalse();
		var visual = root.GetChildren().OfType<VisualEffectInstance>().Single();
		AssertFloat(visual.Scale.X).IsEqualApprox(3.5 / 9, .001);
		AssertBool(((ShaderMaterial)visual.Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		harness.GetSpell<GustSpell>().Cancel(); AssertBool(visual.Visible).IsFalse();
	}

	[TestCase]
	public async Task PushesArmorAbsorptionButNotDefendedOrInvulnerableContacts()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5); harness.Player.GlobalPosition = Vector3.Zero;
		var root = runner.Scene();
		var armored = TestFactories.SpawnCombatMonster(root, new Vector3(2, 0, 0), "Armored");
		var blocked = TestFactories.SpawnCombatMonster(root, new Vector3(0, 0, 2), "Blocked");
		var dodged = TestFactories.SpawnCombatMonster(root, new Vector3(-2, 0, 0), "Dodged");
		var immune = TestFactories.SpawnCombatMonster(root, new Vector3(0, 0, -2), "Immune");
		await runner.SimulateFrames(5);
		foreach (var actor in new[] { armored, blocked, dodged, immune }) TestFactories.PrepareCombatMonster(actor);
		armored.HurtBoxComponent.Armor = 1000000;
		blocked.HurtBoxComponent.RegisterDefenseEvaluator(new Defense(DamageOutcome.Blocked));
		dodged.HurtBoxComponent.RegisterDefenseEvaluator(new Defense(DamageOutcome.Dodged));
		immune.HurtBoxComponent.Invulnerable = true;
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 2 })).IsTrue();
		AssertFloat(armored.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(armored.MovementComponent.IsPushed).IsTrue();
		AssertBool(blocked.MovementComponent.IsPushed).IsFalse(); AssertFloat(blocked.HealthComponent.CurrentHealth).IsEqual(49);
		AssertBool(dodged.MovementComponent.IsPushed).IsFalse(); AssertFloat(dodged.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(immune.MovementComponent.IsPushed).IsFalse(); AssertFloat(immune.HealthComponent.CurrentHealth).IsEqual(50);
	}

	[TestCase]
	public async Task FrozenActorCanBeForcedOutwardWhileStillUnableToAct()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5); harness.Player.GlobalPosition = Vector3.Zero;
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Frozen");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(monster);
		monster.Frozen = true; var before = monster.GlobalPosition;
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 3 })).IsTrue();
		monster.MovementComponent.SetPhysicsProcess(true); await runner.SimulateFrames(10);
		AssertBool(monster.Frozen).IsTrue(); AssertBool(monster.ActionGate.IsStatusLocked).IsTrue();
		AssertFloat(monster.GlobalPosition.X).IsGreater(before.X + .05);
	}

	[TestCase]
	public async Task CancellationStopsRemainingTargetsAndHarnessUsesSelectedLevel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5); harness.Player.GlobalPosition = Vector3.Zero;
		var first = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "AFirst");
		var second = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, 2), "BSecond");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		first.HurtBoxComponent.DamageResolved += (_, _, _) => harness.GetSpell<GustSpell>().Cancel();
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 4 })).IsTrue();
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(47); AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(50);
		first.QueueFree(); second.QueueFree(); await runner.SimulateFrames(2);
		var distant = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(5.5f, 0, 0), "Distant");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(distant);
		TestFactories.CastHarnessSpell(harness, "Gust", 3);
		await runner.SimulateFrames(3);
		AssertFloat(distant.HealthComponent.CurrentHealth).IsEqual(45); AssertBool(distant.MovementComponent.IsPushed).IsTrue();
	}

	[TestCase]
	public async Task ACenteredTargetUsesCapturedFacingAndEmptyCastsStillRender()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false); harness.Player.MovementComponent.SetPhysicsProcess(false);
		var root = runner.Scene(); harness.Player.GlobalPosition = Vector3.Zero;
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 5 })).IsTrue();
		AssertInt(root.GetChildren().OfType<VisualEffectInstance>().Count()).IsEqual(1);
		harness.GetSpell<GustSpell>().Cancel(); await runner.SimulateFrames(2);
		var target = TestFactories.SpawnCombatMonster(root, new Vector3(1, 0, 0), "Centered");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		target.GlobalPosition = harness.Player.GlobalPosition;
		Vector3 facing = -harness.Player.GlobalBasis.Z; facing.Y = 0; facing = facing.Normalized();
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 6 })).IsTrue();
		target.MovementComponent._PhysicsProcess(1.0 / 60);
		AssertFloat(target.MovementComponent.Velocity.Dot(facing)).IsGreater(.9);
	}

	[TestCase]
	public async Task AWallStopsForcedDisplacement()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false); harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Player.GlobalPosition = Vector3.Zero;
		var root = runner.Scene(); var target = TestFactories.SpawnCombatMonster(root, new Vector3(1.5f, 0, 0), "WallTarget");
		var wall = new StaticBody3D { Position = new Vector3(2.7f, 1, 0), CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 4) } }); root.AddChild(wall);
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 3, new RandomNumberGenerator { Seed = 7 })).IsTrue();
		target.MovementComponent.SetPhysicsProcess(true); await runner.SimulateFrames(40);
		AssertFloat(target.GlobalPosition.X).IsGreater(1.6);
		AssertFloat(target.GlobalPosition.X).IsLess(2.35);
	}

	[TestCase]
	public async Task GustCreatesAControlledPushThatGrowsWithLevel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false); harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Player.GlobalPosition = Vector3.Zero;
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "PushDistance");
		await runner.SimulateFrames(5);
		target.HealthComponent.SetHealth(50, 50); target.HurtBoxComponent.Armor = 0;
		float start = target.GlobalPosition.X;
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 11 })).IsTrue();
		await runner.SimulateFrames(80);
		float firstDistance = target.GlobalPosition.X - start;
		AssertFloat(firstDistance).IsGreater(.25);
		AssertFloat(firstDistance).IsLess(1.2);
		target.MovementComponent.ResetTransientMotion(); target.GlobalPosition = new Vector3(2, 0, 0);
		await runner.SimulateFrames(3); start = target.GlobalPosition.X;
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 3, new RandomNumberGenerator { Seed = 12 })).IsTrue();
		await runner.SimulateFrames(80);
		float thirdDistance = target.GlobalPosition.X - start;
		AssertFloat(thirdDistance).IsGreater(firstDistance + .2);
		AssertFloat(thirdDistance).IsLess(2);
	}

	[TestCase]
	public async Task ParriedGuardBrokenAndFilteredContactsCannotPush()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Defended");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		var hit = new DamageContext { Amount = 3, Faction = DamageSourceFlags.Player, OwnerNode = harness.Player,
			IncomingDirection = Vector3.Right, ForcedPush = 5, DefenseTraits = AttackDefenseTraits.Guardable };
		foreach (var outcome in new[] { DamageOutcome.Parried, DamageOutcome.GuardBroken })
		{
			var defense = new Defense(outcome); target.HurtBoxComponent.RegisterDefenseEvaluator(defense);
			AssertInt((int)target.ResolveDamage(in hit).Outcome).IsEqual((int)outcome);
			AssertBool(target.MovementComponent.IsPushed).IsFalse();
			target.HurtBoxComponent.UnregisterDefenseEvaluator(defense);
		}
		target.HurtBoxComponent.DamageFilter = DamageSourceFlags.Enemy;
		AssertInt((int)target.ResolveDamage(in hit).Outcome).IsEqual((int)DamageOutcome.Filtered);
		AssertBool(target.MovementComponent.IsPushed).IsFalse();
	}

	[TestCase]
	public async Task InvalidCastInputsLeaveActorsAndVisualsUnchanged()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), harness.Player.Position + Vector3.Right * 2, "InvalidTarget");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		var spell = harness.GetSpell<GustSpell>(); var original = spell.VisualScene;
		AssertBool(spell.Cast(harness.Player, 0, new RandomNumberGenerator())).IsFalse();
		spell.VisualScene = GD.Load<PackedScene>("res://scenes/debug/combat/combat_practice_target.tscn");
		AssertBool(spell.Cast(harness.Player, 1, new RandomNumberGenerator())).IsFalse();
		spell.VisualScene = GD.Load<PackedScene>("res://scenes/magic/air/storm_guard.tscn");
		AssertBool(spell.Cast(harness.Player, 1, new RandomNumberGenerator())).IsFalse();
		spell.VisualScene = original;
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(target.MovementComponent.IsPushed).IsFalse();
		AssertInt(runner.Scene().GetChildren().OfType<VisualEffectInstance>().Count()).IsEqual(0);
	}

	[TestCase]
	public async Task HarnessCrowdUsesRealMonstersAndCanBeCleared()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false); harness.Player.MovementComponent.SetPhysicsProcess(false);
		var button = harness.GetNode<Button>("%Controls/SpawnCrowd");
		button.EmitSignal(Button.SignalName.Pressed); await runner.SimulateFrames(5);
		var monsters = runner.Scene().GetChildren().OfType<EnemyBase>().ToArray();
		AssertInt(monsters.Length).IsEqual(3);
		foreach (var monster in monsters) TestFactories.PrepareCombatMonster(monster);
		AssertBool(harness.GetSpell<GustSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 8 })).IsTrue();
		foreach (var monster in monsters) AssertBool(monster.MovementComponent.IsPushed).IsTrue();
		button.EmitSignal(Button.SignalName.Pressed); await runner.SimulateFrames(2);
		AssertInt(runner.Scene().GetChildren().OfType<EnemyBase>().Count()).IsEqual(0);
	}
}
