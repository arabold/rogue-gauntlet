namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Observable audio requests from production execution, handoff, monster actions and resolved movement.</summary>
[TestSuite, RequireGodotRuntime]
public partial class CombatAudioTest
{
	private static SoundCueProfile Cue(string name) => TestFactories.Load<SoundCueProfile>($"res://scenes/audio/sound_cue_profiles/{name}.tres");

	/// <summary>Records presentation requests without relying on an installed audio device.</summary>
	private sealed class CueLog : IDisposable
	{
		private readonly List<(SoundCueProfile Cue, Node Source)> _requests = [];
		private readonly SignalBus _bus = SignalBus.Instance;
		private readonly Node _attacker;
		public int Contacts { get; private set; }
		public CueLog(Node attacker)
		{
			_attacker = attacker;
			_bus.WorldSoundEmitted += Record;
			SignalBus.CombatContactResolved += Contact;
		}
		private void Record(SoundCueProfile cue, Node source, Vector3 position) => _requests.Add((cue, source));
		private void Contact(in CombatContactFeedback contact)
		{
			if (contact.Context.OwnerNode == _attacker && contact.Outcome == DamageOutcome.Damaged) Contacts++;
		}
		public int Count(SoundCueProfile cue, Node source) => _requests.Count(r => r.Cue == cue && r.Source == source);
		public void Dispose()
		{
			_bus.WorldSoundEmitted -= Record;
			SignalBus.CombatContactResolved -= Contact;
		}
	}

	private partial class ProbeSpell : Node, ICombatSpell
	{
		public string DisplayName => "Audio probe";
		public int LevelCount => 1;
		public bool Succeeds { get; set; }
		public Action OnCast { get; set; }
		public int Calls { get; private set; }
		public IEnumerable<string> DescribeLevel(in SpellPower power) => [];
		public bool CanCast(in SpellCastContext context) => true;
		public bool Cast(in SpellCastContext context, RandomNumberGenerator random)
		{
			Calls++;
			OnCast?.Invoke();
			return Succeeds;
		}
		public void Cancel() { }
	}

	/// <summary>A combat-only actor deliberately has no movement, gate or visual capability.</summary>
	private static (CharacterBody3D Actor, AttackController Attack) CombatOnlyActor(Node world)
	{
		var actor = new CharacterBody3D { Name = "CombatOnlyActor" };
		var detector = new MeleeHitDetector();
		var attack = new AttackController { MeleeDetector = detector, EffectsEnabled = false,
			DefaultMeleeContact = TestFactories.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_1h.tres") };
		actor.AddChild(detector);
		actor.AddChild(attack);
		actor.AddChild(new ActorAudioComponent { Actor = actor, Attack = attack });
		world.AddChild(actor);
		return (actor, attack);
	}

	[TestCase]
	public async Task MissedMeleeSoundsOnceAcrossCancellationSkippedIntervalsAndReentry()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner);
		var (actor, attack) = CombatOnlyActor(runner.Scene());
		using var sounds = new CueLog(actor);
		var windUp = Cue("warden_windup");
		var release = Cue("attack_release");
		var voice = Cue("undead_attack_voice");
		var definition = new AttackDefinition { HitWindowStart = .03f, HitWindowEnd = .06f, WindUpCue = windUp, ReleaseCue = release };
		AssertBool(attack.TryPrepareAttack(definition, "", .15, out var prepared)).IsTrue();
		prepared = prepared with { VoiceCue = voice };
		AssertBool(attack.StartAttack(prepared, new(1, 1, 0), 0)).IsTrue();
		AssertBool(attack.StartAttack(prepared, new(1, 1, 0), 0)).IsFalse();
		attack.CancelAttack();
		AssertInt(sounds.Count(windUp, actor)).IsEqual(1);
		AssertInt(sounds.Count(release, actor)).IsEqual(0);

		AssertBool(attack.StartAttack(prepared, new(1, 1, 0), 0)).IsTrue();
		attack._PhysicsProcess(.2); // Skip the whole contact window and completion in one tick.
		AssertInt(sounds.Count(release, actor)).IsEqual(1);
		AssertInt(sounds.Contacts).IsEqual(0);
		runner.Scene().RemoveChild(actor);
		runner.Scene().AddChild(actor);
		AssertBool(attack.StartAttack(prepared, new(1, 1, 0), 0)).IsTrue();
		attack._PhysicsProcess(.04);
		attack._PhysicsProcess(.2);
		AssertInt(sounds.Count(release, actor)).IsEqual(2);
		AssertInt(sounds.Count(voice, actor)).IsEqual(3);
	}

	[TestCase(0)] // Release revalidation rejects without calling the spell.
	[TestCase(1)] // Cast itself fizzles.
	[TestCase(2)] // Successful cast.
	[TestCase(3)] // Successful instant contact interrupts its caster synchronously.
	public async Task SpellReleaseReportsOnlyCommittedCastsAndRetainsTheirSnapshot(int scenario)
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner);
		var (actor, attack) = CombatOnlyActor(runner.Scene());
		using var sounds = new CueLog(actor);
		var cue = Cue("spell_release");
		var spell = new ProbeSpell { Succeeds = scenario >= 2, OnCast = scenario == 3 ? attack.CancelAttack : null };
		actor.AddChild(spell);
		var context = TestFactories.CastContext(actor, new SpellPower(1, new(1, 1, 0)));
		var definition = new AttackDefinition { IsRanged = true, HitWindowStart = .02f, HitWindowEnd = .04f, ReleaseCue = cue };
		var prepared = new PreparedAttack(definition, null, .1, Spell: new SpellRelease(spell, context, _ => scenario != 0));
		AssertBool(attack.StartAttack(prepared, new(1, 1, 0), 0)).IsTrue();
		attack._PhysicsProcess(.03);
		attack._PhysicsProcess(.2);
		AssertInt(spell.Calls).IsEqual(scenario == 0 ? 0 : 1);
		AssertInt(sounds.Count(cue, actor)).IsEqual(scenario >= 2 ? 1 : 0);
	}

	private static void Equip(Player player, Item item, EquipmentSlot hand)
	{
		AssertBool(player.Inventory.TryAdd(item).Success).IsTrue();
		player.Inventory.Equip(player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, item)), hand);
	}

	private static MagicStaff Wand(SoundCueProfile release)
	{
		var wand = (MagicStaff)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/wand.tres"));
		var spell = (SpellDefinition)TestFactories.Load<SpellDefinition>("res://scenes/magic/spells/fire_nova.tres").Duplicate();
		spell.Cast = (AttackDefinition)spell.Cast.Duplicate();
		spell.Cast.ReleaseCue = null;
		spell.Cooldown = 3;
		wand.BoundSpell = spell;
		wand.SecondaryReleaseCue = release;
		return wand;
	}

	[TestCase]
	public async Task EitherHandUsesItsOwnFocusAndRecoveryHandoffNeverReplaysTheOldRelease()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.ManaComponent.CurrentMana = 100;
		var mainCue = Cue("spell_release");
		var offCue = Cue("spell_support_release");
		var main = Wand(mainCue);
		var off = Wand(offCue);
		Equip(player, main, EquipmentSlot.WeaponHand);
		Equip(player, off, EquipmentSlot.ShieldHand);
		using var sounds = new CueLog(player);
		AssertBool(player.ActionManager.TryPerformOffhand()).IsTrue();
		AssertBool(player.ActionManager.TryPerformSecondary()).IsFalse();
		await TestFactories.AwaitPhysicsUntil(runner, () => player.AttackController.Phase == ActorActionPhase.Recovery);
		AssertInt(sounds.Count(offCue, player)).IsEqual(1);
		AssertBool(player.ActionManager.TryPerformSecondary()).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.AttackController.IsAttacking);
		AssertInt(sounds.Count(mainCue, player)).IsEqual(1);
		AssertInt(sounds.Count(offCue, player)).IsEqual(1);
		AssertBool(player.ActionManager.OffhandActionSlot.IsOnCooldown).IsTrue();
		AssertObject(main.BoundSpell.Cast.ReleaseCue).IsNull();
		AssertObject(off.BoundSpell.Cast.ReleaseCue).IsNull();
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task PairedMeleeAndStaggeredMissilesHaveOneReleasePerExecution(bool missiles)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		player.MovementComponent.FaceDirection(Vector3.Forward);
		var target = (CombatPracticeTarget)harness.PracticeTarget;
		target.GlobalPosition = player.GlobalPosition + Vector3.Forward * 1.3f;
		target.ResetPhysicsInterpolation();
		var main = missiles ? (Weapon)Wand(Cue("spell_release"))
			: (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword/sword_knight.tres"));
		Equip(player, main, EquipmentSlot.WeaponHand);
		if (!missiles) Equip(player, ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword/sword_knight.tres")), EquipmentSlot.ShieldHand);
		using var sounds = new CueLog(player);
		AssertBool(player.ActionManager.TryPerformPrimary()).IsTrue();
		if (!missiles) AssertString(player.AttackController.CurrentStyle).StartsWith("dualwield.");
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.AttackController.IsAttacking);
		AssertInt(sounds.Count(main.PrimaryReleaseCue, player)).IsEqual(1);
		if (!missiles) AssertInt(sounds.Contacts).IsEqual(1);
	}

	[TestCase]
	public async Task MonsterVolleyLayersOneActionVoiceAndOneReleaseWhileTracking()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var mage = TestFactories.SpawnHarnessMonster(harness, "res://scenes/enemies/skeleton/skeleton_mage.tscn", 6);
		await runner.SimulateFrames(3, 20);
		var action = (MonsterAttackAction)mage.Definition.Behavior.Combat.PrimaryOffense;
		using var sounds = new CueLog(mage);
		AssertBool(mage.EnemyBehaviorComponent.TryAttack()).IsTrue();
		AssertInt(sounds.Count(action.VoiceCue, mage)).IsEqual(1);
		await runner.SimulateFrames(2, 20);
		AssertInt(sounds.Count(action.Attack.ReleaseCue, mage)).IsEqual(0);
		await TestFactories.AwaitPhysicsUntil(runner, () => mage.EnemyBehaviorComponent.RunningAction == null, 3);
		AssertInt(sounds.Count(action.Attack.ReleaseCue, mage)).IsEqual(1);
		AssertInt(sounds.Count(action.VoiceCue, mage)).IsEqual(1);
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task MageSupportCastUsesItsOwnSoundAndCanFizzleWithoutAnAttackVocal(bool fizzle)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var mage = TestFactories.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_mage.tscn").Instantiate<EnemyBase>();
		var combat = TestFactories.UsePrivateCombatProfile(mage);
		var action = (MonsterSpellAction)combat.Actions.Single(a => a.ResourceName == "instant heal");
		combat.Actions = [combat.PrimaryOffense, action];
		mage.Definition.Behavior.InitialAction = EnemyAction.None;
		mage.EnemyBehaviorComponent.AIEnabled = false;
		mage.Position = harness.Player.Position + Vector3.Right * 5;
		runner.Scene().AddChild(mage);
		mage.DeathComponent.RewardsEnabled = false;
		mage.HealthComponent.SetHealth(1, mage.HealthComponent.MaxHealth);
		using var sounds = new CueLog(mage);
		AssertBool(mage.DefinitionValid).IsTrue();
		AssertObject(mage.EnemyBehaviorComponent.TryRequestAction(MonsterActionFilter.SupportOnly)).IsEqual(ActorActionRequestResult.Success);
		if (fizzle) mage.HealthComponent.SetHealth(mage.HealthComponent.MaxHealth, mage.HealthComponent.MaxHealth);
		await TestFactories.AwaitPhysicsUntil(runner, () => mage.EnemyBehaviorComponent.RunningAction == null, 3);
		AssertInt(sounds.Count(action.Spell.Cast.ReleaseCue, mage)).IsEqual(fizzle ? 0 : 1);
		AssertInt(sounds.Count(Cue("undead_attack_voice"), mage)).IsEqual(0);
	}

	[TestCase(0)] // Canceled during wind-up.
	[TestCase(1)] // An accepted dash with no resolved displacement.
	[TestCase(2)] // Normal travel.
	public async Task DashAudioWaitsForMovementAndDoesNotRepeat(int scenario)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var rat = TestFactories.SpawnHarnessMonster(harness, TestFactories.RatScene, 6);
		await runner.SimulateFrames(3, 20);
		var cue = Cue("rush");
		using var sounds = new CueLog(rat);
		var profile = new DashProfile { Style = "melee", WindUpDuration = .1f, Distance = 2, TravelDuration = .4f, RecoveryDuration = 0, TravelCue = cue };
		AssertObject(rat.DashComponent.TryDash(profile, Vector3.Right, out var execution)).IsEqual(ActorActionRequestResult.Success);
		AssertInt(sounds.Count(cue, rat)).IsEqual(0);
		if (scenario == 0) rat.DashComponent.Cancel();
		if (scenario == 1) rat.MovementComponent.SetPhysicsProcess(false);
		await TestFactories.AwaitPhysicsUntil(runner, () => !rat.DashComponent.IsRunning(execution), 2);
		await runner.SimulateFrames(3, 20);
		AssertInt(sounds.Count(cue, rat)).IsEqual(scenario == 2 ? 1 : 0);
	}

	[TestCase]
	public void AuthoredCuesAndWeaponReferencesLoadThroughTheGuardedResourcePath()
	{
		// Keep the managed wrappers alive as well as the engine's native cache while walking shared references.
		var retained = new List<Resource>();
		foreach (string path in Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/audio/sound_cue_profiles"), "*.tres"))
		{
			var cue = TestFactories.Load<SoundCueProfile>(ProjectSettings.LocalizePath(path));
			AssertBool(cue.Validate(out string error)).OverrideFailureMessage($"{path}: {error}").IsTrue();
			retained.Add(cue);
		}
		foreach (string path in Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/items/weapons"), "*.tres", SearchOption.AllDirectories))
		{
			var resource = TestFactories.Load<Resource>(ProjectSettings.LocalizePath(path));
			retained.Add(resource);
			if (resource is not Weapon weapon) continue;
			foreach (var cue in new[] { weapon.PrimaryReleaseCue, weapon.SecondaryReleaseCue })
				if (cue != null) AssertBool(cue.Validate(out string error)).OverrideFailureMessage($"{path}: {error}").IsTrue();
		}
		foreach (string path in new[] { "res://scenes/audio/audio_controller.tscn", "res://scenes/debug/audio/audio_harness.tscn",
			"res://scenes/main/camera/game_camera.tscn", "res://scenes/menu/main_menu.tscn", "res://scenes/ui/pause_menu.tscn" })
			retained.Add(TestFactories.Load<PackedScene>(path));
		GC.KeepAlive(retained);
	}

	[TestCase]
	public async Task SharedCameraListenerFollowsTheHeroAndViewWithoutMovingWhenZoomed()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var rig = (GameCameraRig)runner.Scene().FindChild("GameCamera", true, false);
		rig.SetProcess(false);
		rig.Follow(harness.Player);
		AssertObject(rig.GetViewport().GetAudioListener3D()).IsSame(rig.AudioListener);
		Vector3 expected = harness.Player.GlobalPosition + Vector3.Up * rig.ListeningHeight;
		AssertFloat(rig.AudioListener.GlobalPosition.DistanceTo(expected)).IsLess(.001f);
		rig.PhantomCamera.GlobalBasis = new Basis(Vector3.Up, .5f) * rig.PhantomCamera.GlobalBasis;
		rig.Camera.Size *= 2;
		rig._Process(0);
		AssertFloat(rig.AudioListener.GlobalPosition.DistanceTo(expected)).IsLess(.001f);
		AssertFloat(rig.AudioListener.GlobalBasis.X.DistanceTo(rig.PhantomCamera.GlobalBasis.X)).IsLess(.001f);
	}
}