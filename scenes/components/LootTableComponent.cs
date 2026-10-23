using Godot;

public partial class LootTableComponent : Node
{
	/// <summary>
	/// The chance of dropping loot from this table. Kept on the component (not the
	/// table) so the same shared <see cref="LootTable"/> can drop at different rates
	/// for different spawners.
	/// </summary>
	[Export] public float DropChance { get; private set; } = 1.0f;
	[Export] public Level Level { get; set; }
	/// <summary>Stable generated source key. Empty for genuinely dynamic reward sources.</summary>
	[Export] public string SourceId { get; set; } = "";

	[Export] public ItemCreationPolicy CreationPolicy { get; set; }
	public string ResolvedSourceId => !string.IsNullOrEmpty(SourceId) ? SourceId
		: GetOwner<Node>()?.HasMeta("persistent_id") == true ? GetOwner<Node>().GetMeta("persistent_id").AsString() : "";

	public RandomNumberGenerator CreateDropRng()
	{
		Level ??= this.GetAncestorOrNull<Level>();
		return !string.IsNullOrEmpty(ResolvedSourceId) && Level?.MapGenerator != null
			? Level.MapGenerator.CreateSourceLootRng(ResolvedSourceId)
			: GameSession.Instance?.CreateLootRng() ?? GenerationRandom.Create(42, "dynamic-loot");
	}
	[Export] public PackedScene LootableItemScene { get; private set; }
	/// <summary>The shared, weighted pool this spawner can drop from.</summary>
	[Export] public LootTable Table { get; private set; }
	/// <summary>Minimum number of separate drops rolled from <see cref="Table"/> per event.</summary>
	[Export] public int DropCountMin { get; private set; } = 1;
	/// <summary>Maximum number of separate drops rolled from <see cref="Table"/> per event.</summary>
	[Export] public int DropCountMax { get; private set; } = 1;

	/// <summary>Apply a monster definition before readiness without claiming or rerolling its loot.</summary>
	public void ConfigureTable(LootTable table) => Table = table;

	private bool _isDropped;
	public void RestoreClaimed(bool claimed) => _isDropped = claimed;

	/// <summary>Pure seeded reward generation, also used to rebuild activated sources before applying item deltas.</summary>
	public static System.Collections.Generic.IReadOnlyList<ItemQuantity> GenerateOutcome(LootTable table,
		ItemCreationPolicy policy, float chance, int min, int max, uint depth, RandomNumberGenerator rng, string sourceId)
	{
		var result = new System.Collections.Generic.List<ItemQuantity>();
		if (table == null || rng.Randf() > chance) { return result; }
		int rolls = rng.RandiRange(Mathf.Max(0, Mathf.Min(min, max)), Mathf.Max(0, Mathf.Max(min, max)));
		for (int i = 0; i < rolls; i++)
		{
			LootTableItem entry = table.PickEntry(depth, rng);
			int quantity = entry?.QuantityAt(depth) ?? 0;
			if (quantity > 0)
			{ result.AddRange(ItemFactory.CreateBatch(entry.Item, quantity, policy, depth, rng, $"{sourceId}:drop:{i}")); }
		}
		return result;
	}

	public void DropLoot()
	{
		if (_isDropped) { return; }
		Level ??= this.GetAncestorOrNull<Level>();
		if (Level == null || LootableItemScene == null) { GD.PushError("Loot source has no world destination."); return; }
		using var rng = CreateDropRng();
		uint depth = Level.MapGenerator.DungeonDepth;
		string sourceId = ResolvedSourceId;
		var entries = GenerateOutcome(Table, CreationPolicy, DropChance, DropCountMin, DropCountMax,
			depth, rng, string.IsNullOrEmpty(sourceId) ? System.Guid.NewGuid().ToString("N") : sourceId);
		Level.ItemPersistence.IsCommitting = true;
		try
		{
			_isDropped = true;
			Vector3 position = GetOwner<Node3D>().GlobalPosition;
			if (!string.IsNullOrEmpty(sourceId))
			{
				Level.ItemPersistence.RegisterReward(new RewardSourceSaveData
				{
					SourceId = sourceId, TablePath = Table?.ResourcePath ?? "", PolicyPath = CreationPolicy?.ResourcePath ?? "",
					Chance = DropChance, CountMin = DropCountMin, CountMax = DropCountMax,
					Position = LevelItemPersistence.SavePosition(position),
				}, entries);
			}
			foreach (var entry in entries)
			{
				var pickup = LootableItemScene.Instantiate<LootableItem>();
				pickup.Item = entry.Item;
				pickup.Quantity = entry.Quantity;
				Level.AddWorldNode(pickup, position);
			}
		}
		finally { Level.ItemPersistence.IsCommitting = false; }
	}
}