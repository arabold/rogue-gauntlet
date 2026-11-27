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
		var scene = TestFactories.Load<PackedScene>("res://scenes/props/traps/pressure_plate_spikes.tscn");
		var plate = TestFactories.AddToSceneTree(scene.Instantiate<PressurePlate>());
		var spikes = plate.GetNode<SpikeBurst>("SpikeBurst");
		AssertBool(plate.IsDiscovered).IsFalse(); AssertBool(plate.Visible).IsFalse();
		AssertBool(plate.DiscoveryEffect.Visible).IsFalse();
		AssertBool(plate.Reveal()).IsTrue(); AssertBool(plate.Reveal()).IsFalse();
		AssertBool(plate.GetNodeOrNull("DangerMarker") == null).IsTrue(); AssertBool(plate.DiscoveryEffect.IsPlaying).IsTrue();
		AssertBool(spikes.IsRaised).IsFalse();
		// Plates answer only the hero; the stand-in leaves the group before it goes, so later suites never find it.
		var victim = TestFactories.AddToSceneTree(new Victim());
		victim.AddToGroup("player");
		try
		{
			plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, victim);
			AssertBool(spikes.IsRaised).IsTrue(); AssertFloat(victim.DamageTaken).IsEqual(spikes.CurrentDamage);
			plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, victim);
			AssertFloat(victim.DamageTaken).IsEqual(spikes.CurrentDamage);
		}
		finally
		{
			victim.RemoveFromGroup("player");
			victim.QueueFree();
		}
		var restored = TestFactories.AddToSceneTree(scene.Instantiate<PressurePlate>());
		restored.RestoreDiscovered(true);
		AssertBool(restored.Visible).IsTrue(); AssertBool(restored.DiscoveryEffect.IsPlaying).IsFalse();
	}
	[TestCase]
	public async Task SurfaceRevealsGlowOnceFinishCleanlyAndHarnessReplaysAllProductionEffects()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2, 20);
		var harness = (WorldEffectHarness)runner.Scene();
		AssertBool(harness.PreviewTrap.IsDiscovered).IsFalse();
		await runner.SimulateFrames(30, 20);
		AssertBool(harness.PreviewTrap.IsDiscovered).IsFalse();
		bool RowVisible(string label) => harness.Controls.GetChildren().OfType<Container>()
			.Single(c => c.GetChild(0) is Label l && l.Text == label).Visible;
		AssertBool(RowVisible("Wall variant")).IsFalse(); AssertBool(RowVisible("Reveal glow")).IsTrue();
		harness.Replay();
		var trap = harness.PreviewTrap;
		AssertBool(trap.DiscoveryEffect.IsPlaying).IsTrue();
		// The spike holes fade in through their shader's reveal, then keep their authored material.
		var holes = trap.GetNode<MeshInstance3D>("Holes");
		float Reveal() => ((ShaderMaterial)holes.GetSurfaceOverrideMaterial(0)).GetShaderParameter("reveal").AsSingle();
		AssertObject(holes.MaterialOverlay).IsNull();
		AssertBool(holes.Visible).IsFalse();
		AssertFloat(Reveal()).IsEqual(0);
		await runner.SimulateFrames(12, 20);
		AssertFloat(Reveal()).IsGreater(0);
		AssertFloat(Reveal()).IsLess(1);
		await runner.SimulateFrames(30, 20);
		AssertBool(trap.DiscoveryEffect.IsPlaying).IsFalse();
		AssertObject(holes.MaterialOverlay).IsNull();
		AssertObject(holes.GetSurfaceOverrideMaterial(0)).IsNull();
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
		AssertBool(door.Secret.DiscoveryEffect.IsPlaying).IsTrue();
		var originalWall = door.Secret.DiscoveryEffect.GetNode<MeshInstance3D>("ConcealedSurface");
		// During the glow-only lead-in keep the original opaque depth-writing material.
		AssertObject(originalWall.GetSurfaceOverrideMaterial(0)).IsNotNull();
		AssertBool(((BaseMaterial3D)originalWall.GetActiveMaterial(0)).Transparency == BaseMaterial3D.TransparencyEnum.Disabled).IsTrue();
		AssertInt(harness.PreviewRoot.GetChildren().OfType<GridMap>().Last().GetCellItem(Vector3I.Zero)).IsEqual(-1);
		// The door's reveal glow rises to one peak and fades on the door's own material, never an overlay or a swap.
		var leaf = (MeshInstance3D)door.DoorVisual;
		var material = (BaseMaterial3D)leaf.GetActiveMaterial(0);
		float previous = 0, peak = 0; bool decreasing = false;
		for (int frame = 0; frame < 80 && door.Secret.DiscoveryEffect.IsPlaying; frame++)
		{
			await runner.SimulateFrames(1, 20);
			if (!door.Secret.DiscoveryEffect.IsPlaying) break;
			AssertObject(leaf.GetActiveMaterial(0)).IsSame(material);
			AssertObject(leaf.MaterialOverlay).IsNull();
			float strength = material.Emission.R;
			if (strength < previous - 0.001f) decreasing = true;
			if (decreasing) AssertFloat(strength).IsLessEqual(previous + 0.001f);
			peak = Mathf.Max(peak, strength); previous = strength;
		}
		AssertBool(decreasing).IsTrue(); AssertFloat(peak).IsGreater(0);
		AssertBool(door.Secret.DiscoveryEffect.IsPlaying).IsFalse();
		AssertObject(leaf.GetActiveMaterial(0)).IsNotSame(material);
		foreach (var mesh in door.DoorVisual.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => !VisualHelper.IsHelper(m)))
		{ AssertObject(mesh.MaterialOverlay).IsNull(); AssertFloat(mesh.Transparency).IsEqual(0); }
		AssertInt(door.Secret.DiscoveryEffect.GetChildCount()).IsEqual(0);
		harness.SelectEffect(2); await runner.SimulateFrames(2, 20);
		AssertBool(RowVisible("Reveal glow")).IsFalse(); AssertBool(RowVisible("Wall variant")).IsFalse();
		AssertBool(harness.Controls.FindChildren("*", "ColorPickerButton", true, false).Any()).IsFalse();
		AssertBool(harness.PreviewRoot.GetChildren().OfType<LootBeam>().Any()).IsTrue();
		harness.SelectEffect(3); await runner.SimulateFrames(2, 20);
		AssertBool(RowVisible("Wall variant")).IsFalse();
		AssertBool(RowVisible("Reveal glow")).IsFalse();
		AssertBool(harness.PreviewRoot.GetChildren().OfType<HitEffect>().Any()).IsTrue();
		harness.SelectEffect(0); AssertBool(harness.PreviewTrap.DiscoveryEffect.IsPlaying).IsFalse();
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
	public void RarityPolicyAllowsEmptyFloors()
	{
		var profile = (FloorAccessProfile)TestFactories.Load<FloorAccessProfile>("res://scenes/levels/access/floor_access_profiles/rare_lock.tres").Duplicate(true);
		profile.ValidateOrThrow();
		int locked = 0;
		for (ulong seed = 0; seed < 200; seed++)
		{
			using var locks = GenerationRandom.Create(seed, "access-locks", "0");
			locked += profile.RollLockedRoomBudget(locks);
		}
		AssertInt(locked).IsGreater(0); AssertInt(locked).IsLess(200);
		profile.LockedRoomChance = 0;
		using var rng = GenerationRandom.Create(42, "none");
		AssertInt(profile.RollLockedRoomBudget(rng)).IsEqual(0);
	}
}