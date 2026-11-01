namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

[TestSuite, RequireGodotRuntime]
public partial class DiscoveryAndAccessBalanceTest
{
	private partial class Victim : Node3D, IDamageable
	{
		public float DamageTaken { get; private set; }
		public void TakeDamage(float amount, Vector3 direction, Node attacker = null, DamageSourceFlags faction = DamageSourceFlags.None) => DamageTaken += amount;
	}
	[TestCase]
	public void SharedDetectionRangeHonorsRadiusAndHeight()
	{
		var profile = new SecretDetectionProfile();
		AssertBool(profile.InRange(new Vector3(0, 0, 5), Vector3.Zero)).IsTrue();
		AssertBool(profile.InRange(new Vector3(0, 0, 7), Vector3.Zero)).IsFalse();
		AssertBool(profile.InRange(new Vector3(0, 3, 0), Vector3.Zero)).IsFalse();
	}
	[TestCase]
	public void DiscoveredTrapRemainsDangerousAndRestoreDoesNotPlayTheCue()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/props/floor_trap.tscn");
		var trap = TestFactories.AddToSceneTree(scene.Instantiate<FloorTrap>());
		AssertBool(trap.IsDiscovered).IsFalse(); AssertBool(trap.Visible).IsFalse();
		AssertBool(trap.DiscoveryEffect.Visible).IsFalse();
		AssertBool(trap.Reveal()).IsTrue(); AssertBool(trap.Reveal()).IsFalse();
		AssertBool(trap.GetNodeOrNull("DangerMarker") == null).IsTrue(); AssertBool(trap.DiscoveryEffect.IsPlaying).IsTrue();
		AssertBool(trap.IsTriggered).IsFalse();
		var victim = TestFactories.AddToSceneTree(new Victim());
		trap.GetNode<TriggerComponent>("TriggerComponent").EmitSignal(TriggerComponent.SignalName.Triggered, victim);
		AssertBool(trap.IsTriggered).IsTrue(); AssertFloat(victim.DamageTaken).IsEqual(trap.Damage);
		trap.GetNode<TriggerComponent>("TriggerComponent").EmitSignal(TriggerComponent.SignalName.Triggered, victim);
		AssertFloat(victim.DamageTaken).IsEqual(trap.Damage);
		var restored = TestFactories.AddToSceneTree(scene.Instantiate<FloorTrap>());
		restored.RestoreDiscovered(true);
		AssertBool(restored.Visible).IsTrue(); AssertBool(restored.DiscoveryEffect.IsPlaying).IsFalse();
	}
	[TestCase]
	public async Task SurfaceRevealFinishesAndHarnessReplaysAllProductionEffects()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2);
		var harness = (WorldEffectHarness)runner.Scene();
		AssertBool(harness.PreviewTrap.IsDiscovered).IsFalse();
		await runner.SimulateFrames(30, 20);
		AssertBool(harness.PreviewTrap.IsDiscovered).IsFalse();
		bool RowVisible(string label) => harness.Controls.GetChildren().OfType<Container>()
			.Single(c => c.GetChild(0) is Label l && l.Text == label).Visible;
		AssertBool(RowVisible("Wall variant")).IsFalse(); AssertBool(RowVisible("Reveal glow")).IsTrue();
		harness.Replay();
		var trap = harness.PreviewTrap;
		AssertFloat(trap.Discovery.Profile.Radius).IsEqual(12);
		AssertFloat(trap.DiscoveryEffect.Duration).IsEqual(0.6f);
		AssertBool(trap.DiscoveryEffect.IsPlaying).IsTrue();
		var plate = trap.GetNode<MeshInstance3D>("floor_tile_big_spikes");
		AssertObject(plate.MaterialOverlay).IsNull();
		AssertBool(plate.Visible).IsFalse();
		AssertFloat(((BaseMaterial3D)plate.GetSurfaceOverrideMaterial(0)).AlbedoColor.A).IsEqual(0);
		await runner.SimulateFrames(12, 20);
		AssertFloat(((BaseMaterial3D)plate.GetSurfaceOverrideMaterial(0)).AlbedoColor.A).IsGreater(0);
		AssertFloat(((BaseMaterial3D)plate.GetSurfaceOverrideMaterial(0)).AlbedoColor.A).IsLess(1);
		await runner.SimulateFrames(30, 20);
		AssertBool(trap.DiscoveryEffect.IsPlaying).IsFalse();
		AssertObject(plate.MaterialOverlay).IsNull(); AssertFloat(plate.Transparency).IsEqual(0);
		AssertInt(trap.DiscoveryEffect.GetChildCount()).IsEqual(0);
		AssertInt(harness.PreviewRoot.GetChildren().OfType<GridMap>().Single().GetCellItem(Vector3I.Zero)).IsEqual(0);
		AssertBool(trap.GetNode<TriggerComponent>("TriggerComponent").IsInsideTree()).IsTrue();
		harness.SelectEffect(1);
		AssertBool(RowVisible("Wall variant")).IsTrue(); AssertBool(RowVisible("Reveal glow")).IsTrue();
		AssertBool(harness.PreviewDoor.IsDiscovered).IsFalse();
		AssertInt(harness.PreviewRoot.GetChildren().OfType<GridMap>().Last().GetCellItem(Vector3I.Zero)).IsEqual(0);
		harness.Replay();
		var door = harness.PreviewDoor;
		AssertBool(door.IsDiscovered).IsTrue(); AssertBool(door.IsOpen).IsFalse();
		AssertFloat(door.Secret.Discovery.Profile.Radius).IsEqual(6);
		AssertBool(door.Secret.DiscoveryEffect.IsPlaying).IsTrue();
		var originalWall = door.Secret.DiscoveryEffect.GetNode<MeshInstance3D>("ConcealedSurface");
		// During the glow-only lead-in keep the original opaque depth-writing material.
		AssertObject(originalWall.GetSurfaceOverrideMaterial(0)).IsNotNull();
		AssertBool(((BaseMaterial3D)originalWall.GetActiveMaterial(0)).Transparency == BaseMaterial3D.TransparencyEnum.Disabled).IsTrue();
		AssertInt(harness.PreviewRoot.GetChildren().OfType<GridMap>().Last().GetCellItem(Vector3I.Zero)).IsEqual(-1);
		await runner.SimulateFrames(30, 20);
		AssertBool(door.Secret.DiscoveryEffect.IsPlaying).IsFalse();
		foreach (var mesh in door.DoorVisual.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => !VisualHelper.IsHelper(m)))
		{ AssertObject(mesh.MaterialOverlay).IsNull(); AssertFloat(mesh.Transparency).IsEqual(0); }
		AssertInt(door.Secret.DiscoveryEffect.GetChildCount()).IsEqual(0);
		harness.SelectEffect(2); await runner.SimulateFrames(2);
		AssertBool(RowVisible("Reveal glow")).IsFalse(); AssertBool(RowVisible("Wall variant")).IsFalse();
		AssertBool(harness.Controls.FindChildren("*", "ColorPickerButton", true, false).Any()).IsFalse();
		AssertBool(harness.PreviewRoot.GetChildren().OfType<LootBeam>().Any()).IsTrue();
		harness.SelectEffect(3); await runner.SimulateFrames(2);
		AssertBool(RowVisible("Wall variant")).IsFalse();
		AssertBool(RowVisible("Reveal glow")).IsFalse();
		AssertBool(harness.PreviewRoot.GetChildren().OfType<HitEffect>().Any()).IsTrue();
		harness.SelectEffect(0); AssertBool(harness.PreviewTrap.DiscoveryEffect.IsPlaying).IsFalse();
	}
	[TestCase]
	public async Task RevealGlowHasOnePeakAndKeepsTheSameMaterialAcrossRenderingChanges()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2);
		var harness = (WorldEffectHarness)runner.Scene();
		harness.SelectEffect(1); harness.Replay();
		var door = harness.PreviewDoor;
		var mesh = (MeshInstance3D)door.DoorVisual;
		var material = (BaseMaterial3D)mesh.GetActiveMaterial(0);
		float previous = 0, peak = 0; bool decreasing = false;
		for (int frame = 0; frame < 80 && door.Secret.DiscoveryEffect.IsPlaying; frame++)
		{
			await runner.SimulateFrames(1, 20);
			if (!door.Secret.DiscoveryEffect.IsPlaying) break;
			AssertObject(mesh.GetActiveMaterial(0)).IsSame(material);
			AssertObject(mesh.MaterialOverlay).IsNull();
			float strength = material.Emission.R;
			if (strength < previous - 0.001f) decreasing = true;
			if (decreasing) AssertFloat(strength).IsLessEqual(previous + 0.001f);
			peak = Mathf.Max(peak, strength); previous = strength;
		}
		AssertBool(decreasing).IsTrue(); AssertFloat(peak).IsGreater(0);
		AssertBool(door.Secret.DiscoveryEffect.IsPlaying).IsFalse();
		AssertObject(mesh.GetActiveMaterial(0)).IsNotSame(material);
	}
	[TestCase]
	public void RogueAloneStartsWithTwoPicks()
	{
		var catalog = TestFactories.Load<CharacterCatalog>("res://scenes/player/classes/character_catalog.tres");
		foreach (var character in catalog.Classes)
		{
			var inventory = character.CreateStartingInventory(20);
			AssertInt(inventory.Items.Where(s => s.Item is LockpickItem).Sum(s => s.Quantity)).IsEqual(character.Id == "rogue" ? 2 : 0);
		}
	}
	[TestCase]
	public void RarityPolicyAllowsEmptyFloorsAndSecretsAreMoreFrequent()
	{
		var profile = (LevelAccessProfile)TestFactories.Load<LevelAccessProfile>("res://scenes/levels/access/garrison_access.tres").Duplicate(true);
		profile.Validate();
		AssertInt(profile.MaximumLockedDoors).IsEqual(1);
		int locked = 0, secret = 0;
		for (ulong seed = 0; seed < 200; seed++)
		{
			using var locks = GenerationRandom.Create(seed, "access-locks", "0");
			using var secrets = GenerationRandom.Create(seed, "access-secrets", "0");
			locked += profile.RollLockedRoomBudget(locks);
			secret += profile.RollSecretRoomBudget(secrets);
		}
		AssertInt(locked).IsGreater(0); AssertInt(locked).IsLess(200);
		AssertInt(secret).IsGreater(locked); AssertInt(secret).IsLess(200);
		profile.LockedRoomChance = profile.SecretRoomChance = 0;
		using var rng = GenerationRandom.Create(42, "none");
		AssertInt(profile.RollLockedRoomBudget(rng)).IsEqual(0);
		AssertInt(profile.RollSecretRoomBudget(rng)).IsEqual(0);
	}
	[TestCase]
	public async Task DoorCapAppliesAcrossMultiEntranceRoomsAndFloorsSpawnNoPicks()
	{
		var (runner, map) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		TestFactories.WireLootScenes(map);
		map.AccessProfile = (LevelAccessProfile)TestFactories.Load<LevelAccessProfile>("res://scenes/levels/access/garrison_access.tres").Duplicate(true);
		map.AccessProfile.LockedRoomChance = 1; map.AccessProfile.MaximumLockedRooms = 3;
		map.MapWidth = map.MapDepth = 30;
		for (ulong seed = 1; seed <= 4; seed++)
		{
			map.Seed = seed; map.GenerateMap();
			var doors = map.NavigationRegion.FindChildren("*", "", true, false).OfType<Door>().Where(d => !d.IsQueuedForDeletion());
			AssertInt(doors.Count(d => d.Lock?.IsLocked == true)).IsLessEqual(map.AccessProfile.MaximumLockedDoors);
			AssertBool(map.NavigationRegion.GetChildren().OfType<LootableItem>().All(i => i.Item is not LockpickItem)).IsTrue();
			await runner.SimulateFrames(1);
		}
	}
}