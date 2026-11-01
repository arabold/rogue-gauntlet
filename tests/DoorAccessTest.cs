namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Production door completion, stack costs, concealment and seeded access guarantees.</summary>
[TestSuite]
[RequireGodotRuntime]
public class DoorAccessTest
{
	private static KeyItem Gold => TestFactories.Load<KeyItem>("res://scenes/items/keys/key/key_gold_treasury.tres");
	private static KeyItem Silver => TestFactories.Load<KeyItem>("res://scenes/items/keys/key/key_silver_crypt.tres");
	private static LockpickItem Pick => TestFactories.Load<LockpickItem>("res://scenes/items/tools/tool/tool_lockpick_set.tres");
	private static ActorCapabilities Capabilities => new() { CanOpenDoors = true, CanPickMechanicalLocks = true };
	private static ActorInteractionContext Context(Inventory inventory) => new(null, Capabilities, inventory);
	/// <summary>The production policy shape: a guaranteed key, optionally also pickable.</summary>
	private static DoorLockDefinition LockDefinition(bool pickable = true, float pickDuration = 5) => new()
	{
		RequiredKey = Gold,
		Methods = pickable ? [new KeyUnlockMethod(), new LockpickUnlockMethod { Duration = pickDuration }] : [new KeyUnlockMethod()],
	};
	private static DoorLockComponent Lock(bool pickable = true)
	{
		var node = new DoorLockComponent();
		node.Initialize(LockDefinition(pickable, 0.1f));
		return node;
	}
	[TestCase]
	public void KeyPreferenceConsumptionAndWrongColor()
	{
		var inventory = new Inventory(); inventory.AddItem(Silver, 2); inventory.AddItem(Pick, 3);
		var node = Lock();
		try
		{
			AssertString(node.SelectMethod(Context(inventory))?.OperationId).IsEqual("pick_open");
			AssertBool(node.TryUnlock(Context(inventory), "key_open")).IsFalse();
			inventory.AddItem(Gold, 2);
			AssertString(node.SelectMethod(Context(inventory))?.OperationId).IsEqual("key_open");
			bool observerSawUnlocked = false;
			inventory.Changed += () => observerSawUnlocked = !node.IsLocked;
			AssertBool(node.TryUnlock(Context(inventory), "key_open")).IsTrue();
			AssertBool(observerSawUnlocked).IsTrue();
			AssertInt(inventory.Items.Single(s => s.Item is KeyItem k && k.DefinitionPath == Gold.DefinitionPath).Quantity).IsEqual(1);
			AssertInt(inventory.Items.Single(s => s.Item is LockpickItem).Quantity).IsEqual(3);
			AssertBool(node.TryUnlock(Context(inventory), "key_open")).IsFalse();
		}
		finally { node.Free(); }
	}
	[TestCase]
	public void PickingRequiresPermissionAndHonorsUnpickableFlag()
	{
		var inventory = new Inventory(); inventory.AddItem(Pick, 2);
		var node = Lock(false);
		try
		{
			AssertBool(node.TryUnlock(Context(inventory), "pick_open")).IsFalse();
			node.Initialize(LockDefinition());
			AssertBool(node.TryUnlock(new ActorInteractionContext(null, new ActorCapabilities { CanOpenDoors = true }, inventory), "pick_open")).IsFalse();
			AssertBool(node.TryUnlock(Context(inventory), "pick_open")).IsTrue();
			AssertInt(inventory.Items.Single().Quantity).IsEqual(1);
		}
		finally { node.Free(); }
	}
	[TestCase]
	public void AccessItemsStackSplitAndRoundTripWithoutDefinitionMutation()
	{
		var inventory = new Inventory(); inventory.AddItem(Gold, 19); inventory.AddItem(Gold, 3); inventory.AddItem(Silver, 1); inventory.AddItem(Pick, 3);
		AssertInt(inventory.Items.Count).IsEqual(4);
		AssertInt(inventory.Items[0].Quantity).IsEqual(20);
		AssertBool(inventory.TrySplit(inventory.Items.Last(), 1).Success).IsTrue();
		var json = JsonSerializer.Serialize(InventorySaveCodec.Capture(inventory));
		var data = JsonSerializer.Deserialize<InventorySaveData>(json);
		AssertInt(data.Items.Sum(e => e.Quantity)).IsEqual(26);
		foreach (var entry in data.Items)
		{
			var restored = ItemSaveCodec.Restore(entry);
			AssertBool(restored is KeyItem or LockpickItem).IsTrue();
			AssertBool(restored.IsRuntimeInstance).IsTrue();
		}
		AssertBool(Gold.IsRuntimeInstance).IsFalse();
		AssertInt(Pick.MaxStackSize).IsEqual(20);
	}
	[TestCase]
	public async Task TimedProductionDoorCancelsWithoutCostThenCommitsOnce()
	{
		var door = TestFactories.Load<PackedScene>("res://scenes/props/door.tscn").Instantiate<Door>();
		TestFactories.AddToSceneTree(door);
		door.Lock.Initialize(LockDefinition(true, 0.1f));
		var actor = TestFactories.AddToSceneTree(new CharacterBody3D { Position = new Vector3(0, 0, -1) });
		var execution = new ActorInteractionComponent { Actor = actor }; actor.AddChild(execution);
		var inventory = new Inventory(); inventory.AddItem(Pick, 2);
		var context = new ActorInteractionContext(actor, Capabilities, inventory);
		var target = door.GetNode<InteractiveComponent>("InteractiveComponent");
		AssertBool(execution.TryBegin(target.GetOffer(context), context) == InteractionRequestResult.Success).IsTrue();
		execution.CancelCurrent();
		AssertBool(door.Lock.IsLocked).IsTrue(); AssertInt(inventory.Items.Single().Quantity).IsEqual(2);
		AssertBool(execution.TryBegin(target.GetOffer(context), context) == InteractionRequestResult.Success).IsTrue();
		execution._PhysicsProcess(0.2);
		AssertBool(door.IsOpen).IsTrue(); AssertBool(door.Lock.IsLocked).IsFalse();
		AssertInt(inventory.Items.Single().Quantity).IsEqual(1);
		execution._PhysicsProcess(1); AssertInt(inventory.Items.Single().Quantity).IsEqual(1);
		await Task.CompletedTask;
	}
	[TestCase]
	public void RemovedToolAndForgedReservationCannotSpendOrUnlock()
	{
		var door = TestFactories.Load<PackedScene>("res://scenes/props/door.tscn").Instantiate<Door>(); TestFactories.AddToSceneTree(door);
		door.Lock.Initialize(LockDefinition());
		var actor = TestFactories.AddToSceneTree(new Node3D());
		var inventory = new Inventory(); inventory.AddItem(Gold, 1);
		var context = new ActorInteractionContext(actor, Capabilities, inventory);
		var target = door.GetNode<InteractiveComponent>("InteractiveComponent");
		AssertBool(target.TryReserve(target.GetOffer(context), actor, out var reservation)).IsTrue();
		inventory.RemoveItem(inventory.Items.Single(), 1);
		AssertBool(target.TryCommit(reservation, context) == InteractionCommitResult.PrerequisitesFailed).IsTrue();
		AssertBool(door.Lock.IsLocked).IsTrue();
		AssertBool(target.TryCommit(reservation, context) != InteractionCommitResult.Success).IsTrue();
		AssertBool(target.TryCommit(default, context) != InteractionCommitResult.Success).IsTrue();
	}
	[TestCase]
	public void AllPublishedKeyModelsLoadAsCanonicalAccessItems()
	{
		var ids = new System.Collections.Generic.HashSet<string>();
		foreach (var name in new[] { "gold_treasury", "silver_crypt", "bronze_ornate", "bone_skull", "iron_prison", "arcane_crystal" })
		{
			var key = TestFactories.Load<KeyItem>($"res://scenes/items/keys/key/key_{name}.tres");
			AssertBool(ids.Add(key.DefinitionPath)).IsTrue(); AssertBool(key.CanStack).IsTrue();
			var model = key.Scene.Instantiate<Node3D>(); AssertBool(model.GetChildCount() > 0).IsTrue(); model.Free();
		}
	}
	[TestCase]
	public void DoorPromptShowsOnlyAvailableActionOrMissingKeyRequirement()
	{
		var door = TestFactories.Load<PackedScene>("res://scenes/props/door.tscn").Instantiate<Door>(); TestFactories.AddToSceneTree(door);
		door.Lock.Initialize(LockDefinition());
		var target = door.GetNode<InteractiveComponent>("InteractiveComponent");
		var prompt = target.GetNode<FloatingLabel>("FloatingLabel");
		var glyph = prompt.GetNode<KeyBinding>("%KeyBinding");
		var label = prompt.GetNode<Label>("%Label");
		var inventory = new Inventory();
		// The focus controller hands each frame's offer to the prompt; querying an offer alone changes nothing.
		InteractionOffer Focus() { var offer = target.GetOffer(Context(inventory)); target.SetFocused(true, offer); return offer; }
		AssertString(Focus().VerbLabel).IsEqual(Gold.Name + " needed");
		AssertBool(prompt.IsAvailable).IsFalse(); AssertBool(glyph.Visible).IsFalse();
		AssertBool(label.Modulate.R > label.Modulate.G).IsTrue();
		inventory.AddItem(Pick, 1);
		AssertString(target.GetOffer(Context(inventory)).VerbLabel).IsEqual("Pick lock");
		AssertBool(prompt.IsAvailable).IsFalse(); // Unchanged until the prompt is refreshed.
		AssertString(Focus().VerbLabel).IsEqual("Pick lock");
		AssertBool(prompt.IsAvailable).IsTrue(); AssertBool(glyph.Visible).IsTrue();
		inventory.AddItem(Gold, 1);
		AssertString(Focus().VerbLabel).IsEqual("Unlock");
		AssertString(target.Text).IsEqual("Unlock");
		AssertBool(glyph.Visible).IsTrue(); AssertBool(label.Modulate == Colors.White).IsTrue();
	}
	[TestCase]
	public void MarkerMatchesRequiredKeyAndHidesAfterUnlock()
	{
		var door = TestFactories.Load<PackedScene>("res://scenes/props/door.tscn").Instantiate<Door>(); TestFactories.AddToSceneTree(door);
		door.Lock.Initialize(LockDefinition());
		door.SetIndicatorVisible(true);
		AssertBool(door.KeyMarker.Visible).IsTrue();
		AssertBool(((StandardMaterial3D)door.KeyMarker.MaterialOverride).AlbedoColor == Gold.MarkerColor).IsTrue();
		AssertBool(LootVisuals.ResolveColor(Gold) == Gold.MarkerColor).IsTrue();
		var inventory = new Inventory(); inventory.AddItem(Gold, 1);
		door.Lock.TryUnlock(Context(inventory), "key_open"); // The lock's Changed signal refreshes the door.
		AssertBool(door.KeyMarker.Visible).IsFalse();
	}
	[TestCase]
	public void DetectionUsesResolvedStatAndRejectsOppositeSide()
	{
		var profile = new SecretDetectionProfile(); profile.Validate();
		var stats = new PlayerStats { BaseIntelligence = 10 };
		AssertFloat(profile.Chance(profile.Resolve(stats))).IsEqualApprox(0.1f, 0.001f);
		stats.BaseIntelligence = 20;
		AssertFloat(profile.Chance(profile.Resolve(stats))).IsGreater(0.1f);
		AssertFloat(profile.Chance(1000)).IsEqualApprox(0.9f, 0.001f);
	}
	[TestCase]
	public async Task GeneratedPairsAreReachableAndSecretsRestoreAsClosedDoors()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.RoomPool());
		var generator = level.MapGenerator;
		TestFactories.WireLootScenes(generator);
		generator.AccessProfile = (LevelAccessProfile)TestFactories.Load<LevelAccessProfile>("res://scenes/levels/access/garrison_access.tres").Duplicate(true);
		// Solvability coverage deliberately requests dense content; player-facing scarcity is tested separately.
		generator.AccessProfile.LockedRoomChance = generator.AccessProfile.SecretRoomChance = 1;
		generator.AccessProfile.MaximumLockedRooms = 2;
		generator.AccessProfile.MaximumLockedDoors = 3;
		generator.MapWidth = generator.MapDepth = 30; generator.MaxRooms = 6;
		int lockedCount = 0, secretCount = 0;
		for (ulong seed = 1; seed <= 8; seed++)
		{
			generator.Seed = seed; level.Generate();
			var doors = generator.NavigationRegion.FindChildren("*", "", true, false).OfType<Door>().Where(d => !d.IsQueuedForDeletion()).ToArray();
			var locked = doors.Where(d => d.Lock?.IsLocked == true).ToArray(); lockedCount += locked.Length;
			var blocked = generator.GeneratedSecrets.Select(s => s.Connector).ToHashSet();
			foreach (var door in locked)
			{
				var id = door.ConnectionId.Split(':'); blocked.Add(new Vector2I(int.Parse(id[1]), int.Parse(id[2])));
			}
			var reached = AccessSolutionValidator.Reachable(generator.Map, generator.Map.WorldToTile(generator.Floor.Arrival.GlobalPosition), blocked);
			AssertBool(generator.NavigationRegion.GetChildren().OfType<LootableItem>().All(p => p.Item is not LockpickItem)).IsTrue();
			var keys = generator.NavigationRegion.GetChildren().OfType<LootableItem>().Where(p => p.Item is KeyItem).ToArray();
			foreach (var key in keys) AssertBool(reached.Contains(generator.Map.WorldToTile(key.GlobalPosition))).IsTrue();
			foreach (var secret in generator.GeneratedSecrets) AssertBool(generator.Floor.FreelyAccessibleTiles().Contains(secret.Connector)).IsFalse();
			if (generator.Floor.TryPickRandomFreePosition(out var landing)) AssertBool(generator.Floor.FreelyAccessibleTiles().Contains(generator.Map.WorldToTile(landing))).IsTrue();
			foreach (var color in locked.GroupBy(d => d.Lock.Definition.RequiredKey.DefinitionPath))
				AssertInt(keys.Where(p => p.Item.DefinitionPath == color.Key).Sum(p => p.Quantity)).IsEqual(color.Count());
			foreach (var secret in generator.GeneratedSecrets)
			{
				secretCount++;
				AssertBool(secret.Door.IsDiscovered).IsFalse(); AssertBool(secret.Door.KeyMarker.Visible).IsFalse();
				AssertBool(secret.Door.GetNode<InteractiveComponent>("InteractiveComponent").GetOffer(default).IsValid).IsFalse();
				var side = secret.Host - secret.Connector;
				var approach = new Vector3(side.X, 0, side.Y);
				AssertBool(secret.Door.Secret.CanBeObservedFrom(secret.Door.GlobalPosition + approach)).IsTrue();
				AssertBool(secret.Door.Secret.CanBeObservedFrom(secret.Door.GlobalPosition - approach)).IsFalse();
				AssertBool(secret.Door.Secret.AcceptsSightHit(generator.WallGridMap, secret.Door.GlobalPosition + Vector3.Up * 2)).IsTrue();
				AssertBool(secret.Door.Secret.AcceptsSightHit(generator.WallGridMap, secret.Door.GlobalPosition + approach * 4)).IsFalse();
				secret.Door.Secret.Reveal(); AssertBool(secret.Door.IsOpen).IsFalse();
				var state = level.CaptureExploration();
				level.RestoreExploration(JsonSerializer.Deserialize<RevealedLevelSaveData>(JsonSerializer.Serialize(state)));
				AssertBool(secret.Door.IsDiscovered).IsTrue(); AssertBool(secret.Door.IsOpen).IsFalse();
			}
			if (locked.Length > 0)
			{
				var carried = new Inventory(); carried.AddItem(locked[0].Lock.Definition.RequiredKey, 1);
				AssertBool(locked[0].Lock.TryUnlock(Context(carried), "key_open")).IsTrue();
			}
			foreach (var trap in generator.NavigationRegion.GetChildren().OfType<FloorTrap>()) trap.Reveal(false);
			level.Discovery.RevealRoom(0);
			var snapshot = JsonSerializer.Deserialize<RevealedLevelSaveData>(JsonSerializer.Serialize(level.CaptureExploration()));
			string signature = string.Join("|", doors.Select(d => d.ConnectionId + ":" + d.Lock?.Definition?.RequiredKey?.DefinitionPath));
			level.Generate();
			var regenerated = generator.NavigationRegion.FindChildren("*", "", true, false).OfType<Door>().Where(d => !d.IsQueuedForDeletion());
			AssertString(string.Join("|", regenerated.Select(d => d.ConnectionId + ":" + d.Lock?.Definition?.RequiredKey?.DefinitionPath))).IsEqual(signature);
			level.RestoreExploration(snapshot);
			var recaptured = level.CaptureExploration();
			AssertInt(recaptured.ExploredSamples.Count).IsEqual(snapshot.ExploredSamples.Count);
			AssertString(string.Join("|", recaptured.DiscoveredTrapIds)).IsEqual(string.Join("|", snapshot.DiscoveredTrapIds));
			foreach (var state in snapshot.Doors)
			{
				var restored = regenerated.Single(d => d.ConnectionId == state.ConnectionId);
				AssertBool(restored.IsDiscovered == state.IsDiscovered && restored.Lock.IsLocked == state.IsLocked && restored.IsOpen == state.IsOpen).IsTrue();
			}
			await runner.SimulateFrames(1);
		}
		GD.Print($"ACCESS_COUNTS locked={lockedCount} secret={secretCount}");
		AssertInt(lockedCount).IsGreater(0);
		AssertInt(secretCount).IsGreater(0);
	}
}