using Godot;

[GlobalClass]
public partial class LootTableComponent : Node
{
	/// <summary>What this source drops and how often. Monsters receive theirs from their definition.</summary>
	[Export] public LootDropProfile Profile { get; set; }
	/// <summary>The level holding this source: runtime context, resolved from the tree on entry rather than authored.</summary>
	public Level Level { get; private set; }
	/// <summary>Stable generated source key. Empty for genuinely dynamic reward sources.</summary>
	[Export] public string SourceId { get; set; } = "";

	public string ResolvedSourceId => !string.IsNullOrEmpty(SourceId) ? SourceId
		: GetOwner<Node>()?.HasMeta("persistent_id") == true ? GetOwner<Node>().GetMeta("persistent_id").AsString() : "";

	public override void _EnterTree() => Level = this.GetAncestorOrNull<Level>();

	/// <summary>A stable source recreates its own stream; a dynamic one draws the run's next gameplay roll on this floor.</summary>
	private RandomNumberGenerator CreateDropRng()
	{
		return !string.IsNullOrEmpty(ResolvedSourceId) ? Level.CreateSourceLootRng(ResolvedSourceId) : Level.Run.CreateLootRng(Level.Seed);
	}
	[Export] public PackedScene LootableItemScene { get; set; }

	/// <summary>Apply a monster definition before readiness without claiming or rerolling its loot.</summary>
	public void ConfigureProfile(LootDropProfile profile) => Profile = profile;

	private bool _isDropped;
	public void RestoreClaimed(bool claimed) => _isDropped = claimed;

	/// <summary>Rolls a drop profile: its table, chance, roll count and creation policy. An unset profile drops nothing.</summary>
	public static System.Collections.Generic.IReadOnlyList<ItemQuantity> GenerateOutcome(LootDropProfile profile, uint depth,
		RandomNumberGenerator rng, string sourceId) => GenerateOutcome(profile?.Table, profile?.CreationPolicy, profile?.Chance ?? 0f,
		profile?.CountMin ?? 0, profile?.CountMax ?? 0, depth, rng, sourceId);

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
		if (Level == null || LootableItemScene == null) { GD.PushError("Loot source has no world destination."); return; }
		using var rng = CreateDropRng();
		uint depth = Level.Depth;
		string sourceId = ResolvedSourceId;
		var entries = GenerateOutcome(Profile, depth, rng, string.IsNullOrEmpty(sourceId) ? System.Guid.NewGuid().ToString("N") : sourceId);
		Level.ItemPersistence.IsCommitting = true;
		try
		{
			_isDropped = true;
			Node3D source = GetOwner<Node3D>();
			Vector3 position = source.GlobalPosition;
			// Sibling drops line up along the source's side axis: along a chest's front, never into the wall behind it.
			Vector3 spreadAxis = (source.GlobalBasis.X with { Y = 0 }).Normalized();
			if (!string.IsNullOrEmpty(sourceId))
			{
				Level.ItemPersistence.RegisterReward(new RewardSourceSaveData
				{
					SourceId = sourceId, TablePath = Profile?.Table?.ResourcePath ?? "", PolicyPath = Profile?.CreationPolicy?.ResourcePath ?? "",
					Chance = Profile?.Chance ?? 0f, CountMin = Profile?.CountMin ?? 0, CountMax = Profile?.CountMax ?? 0,
					Position = LevelItemPersistence.SavePosition(position),
				}, entries);
			}
			for (int i = 0; i < entries.Count; i++)
			{
				var pickup = LootableItemScene.Instantiate<LootableItem>();
				pickup.Item = entries[i].Item;
				pickup.Quantity = entries[i].Quantity;
				pickup.RevealFromSource(i, entries.Count, spreadAxis);
				Level.AddWorldNode(pickup, position);
			}
		}
		finally { Level.ItemPersistence.IsCommitting = false; }
	}
}