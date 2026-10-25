using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Level-local baseline comparison. Only removals, overrides, added objects, and activated
/// reward-source descriptors reach disk. Untouched items are reconstructed from source seeds.
/// </summary>
[Tool] // Referenced by the tool Level script; it has no editor-time behavior.
public partial class LevelItemPersistence : Node
{
	[Export] public PackedScene PickupScene { get; set; }
	public bool IsCommitting { get; internal set; }
	private Level _level;
	private readonly Dictionary<string, WorldItemSaveData> _baseline = new();
	private readonly Dictionary<string, RewardSourceSaveData> _rewards = new();

	public void Initialize(Level level)
	{
		_level = level;
		PickupScene ??= level.MapGenerator.LooseItemScene;
		_baseline.Clear();
		_rewards.Clear();
	}

	public static Vector3SaveData SavePosition(Vector3 position) => new() { X = position.X, Y = position.Y, Z = position.Z };
	public static Vector3 Position(Vector3SaveData position) => new(position.X, position.Y, position.Z);
	private IEnumerable<LootableItem> Pickups() => _level.FindChildren("*", "", true, false)
		.OfType<LootableItem>().Where(p => !p.IsCollected && !p.IsQueuedForDeletion());
	private static void ValidatePosition(Vector3SaveData position)
	{
		if (position == null || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
		{ throw new InvalidOperationException("Invalid saved item position."); }
	}
	private static WorldItemSaveData CapturePickup(LootableItem pickup) => new()
	{
		Item = ItemSaveCodec.Capture(pickup.Item, pickup.Quantity), Position = SavePosition(pickup.GlobalPosition),
	};

	public void RecordInitialBaseline()
	{
		foreach (var pickup in Pickups())
		{
			if (!pickup.Item.IsRuntimeInstance) { throw new InvalidOperationException("World item was not initialized."); }
			_baseline.Add(pickup.Item.InstanceId, CapturePickup(pickup));
		}
	}

	public void RegisterReward(RewardSourceSaveData source, IReadOnlyList<ItemQuantity> entries)
	{
		if (!_rewards.TryAdd(source.SourceId, source)) { throw new InvalidOperationException("Reward source activated twice."); }
		foreach (var entry in entries)
		{
			_baseline.Add(entry.Item.InstanceId, new WorldItemSaveData
			{
				Item = ItemSaveCodec.Capture(entry.Item, entry.Quantity), Position = source.Position,
			});
		}
	}

	public LevelItemDeltaSaveData Capture()
	{
		if (IsCommitting) { throw new InvalidOperationException("Item transfer is still committing."); }
		var current = Pickups().ToDictionary(p => p.Item.InstanceId, CapturePickup);
		var data = new LevelItemDeltaSaveData { Depth = _level.MapGenerator.DungeonDepth, ActivatedSources = _rewards.Values.ToList() };
		foreach (var pair in _baseline)
		{
			if (!current.TryGetValue(pair.Key, out var record)) { data.RemovedItemIds.Add(pair.Key); }
			else if (JsonSerializer.Serialize(record) != JsonSerializer.Serialize(pair.Value)) { data.Overrides.Add(record); }
		}
		foreach (var pair in current.Where(p => !_baseline.ContainsKey(p.Key))) { data.AddedItems.Add(pair.Value); }
		return data;
	}

	/// <summary>Validates and prepares final world objects before replacing the generated representations.</summary>
	public void Apply(LevelItemDeltaSaveData changes)
	{
		if (changes == null) { return; }
		if (changes.Depth != _level.MapGenerator.DungeonDepth) { throw new InvalidOperationException("Wrong floor item delta."); }
		foreach (var source in changes.ActivatedSources)
		{
			ValidatePosition(source.Position);
			if (string.IsNullOrEmpty(source.SourceId) || !float.IsFinite(source.Chance) || source.Chance < 0 || source.Chance > 1
				|| source.CountMin < 0 || source.CountMax < source.CountMin) { throw new InvalidOperationException("Invalid reward source."); }
			LootTable table = string.IsNullOrEmpty(source.TablePath) ? null : ResourceLoader.Load<LootTable>(source.TablePath)
				?? throw new InvalidOperationException("Missing reward table.");
			ItemCreationPolicy policy = string.IsNullOrEmpty(source.PolicyPath) ? null : ResourceLoader.Load<ItemCreationPolicy>(source.PolicyPath)
				?? throw new InvalidOperationException("Missing creation policy.");
			using var rng = _level.MapGenerator.CreateSourceLootRng(source.SourceId);
			RegisterReward(source, LootTableComponent.GenerateOutcome(table, policy, source.Chance, source.CountMin,
				source.CountMax, changes.Depth, rng, source.SourceId));
		}
		var final = new Dictionary<string, WorldItemSaveData>(_baseline);
		var removed = new HashSet<string>();
		foreach (string id in changes.RemovedItemIds)
		{
			if (!removed.Add(id) || !final.Remove(id)) { throw new InvalidOperationException("Unknown or duplicate removed item."); }
		}
		var overrides = new HashSet<string>();
		foreach (var record in changes.Overrides)
		{
			string id = record.Item.InstanceId;
			if (!final.ContainsKey(id) || !overrides.Add(id)) { throw new InvalidOperationException("Invalid item override."); }
			final[id] = record;
		}
		foreach (var record in changes.AddedItems)
		{
			if (_baseline.ContainsKey(record.Item.InstanceId) || !final.TryAdd(record.Item.InstanceId, record)) { throw new InvalidOperationException("Duplicate added item."); }
		}
		if (PickupScene == null && final.Count > 0) { throw new InvalidOperationException("No pickup scene configured."); }
		var prepared = new List<(LootableItem Node, Vector3 Position)>();
		try
		{
			foreach (var record in final.Values)
			{
				ValidatePosition(record.Position);
				Item item = ItemSaveCodec.Restore(record.Item);
				var node = PickupScene.Instantiate<LootableItem>();
				node.Item = item; node.Quantity = record.Item.Quantity;
				prepared.Add((node, Position(record.Position)));
			}
		}
		catch { foreach (var entry in prepared) { entry.Node.Free(); } throw; }
		foreach (var pickup in Pickups().ToArray()) { pickup.GetParent().RemoveChild(pickup); pickup.QueueFree(); }
		foreach (var entry in prepared) { _level.AddWorldNode(entry.Node, entry.Position); }
		foreach (var source in _level.FindChildren("*", "", true, false).OfType<LootTableComponent>())
		{
			if (!_rewards.ContainsKey(source.ResolvedSourceId)) { continue; }
			source.RestoreClaimed(true);
			if (source.GetOwner() is Chest chest) { chest.RestoreOpenState(); }
		}
	}
}