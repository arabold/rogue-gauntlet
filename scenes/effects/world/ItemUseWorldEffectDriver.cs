using Godot;
using System;

/// <summary>Maps committed consumption notifications to recipient bursts, independently of buff ticking.</summary>
[GlobalClass]
public partial class ItemUseWorldEffectDriver : Node
{
	[Export] public WorldEffectHost Host { get; set; }
	[Export] public WorldEffectDefinition ScrollReading { get; set; }
	public IdentificationService Identification { get; set; }
	[Export] public Godot.Collections.Dictionary<StringName, WorldEffectDefinition> Cues { get; set; } = [];
	private Inventory _inventory;
	private Action _unsubscribe;
	private uint _cosmeticSequence;
	public Inventory Inventory
	{
		get => _inventory;
		set { if (_inventory == value) return; _inventory = value; if (IsInsideTree()) Bind(); }
	}
	public override void _Ready() => Bind();
	private void Bind()
	{
		_unsubscribe?.Invoke();
		_unsubscribe = this.SubscribeUntilExit(_inventory, i => i.ItemConsumed += OnConsumed, i => i.ItemConsumed -= OnConsumed);
	}
	private void OnConsumed(ConsumableItem item)
	{
		if (Host == null || item == null) return;
		// Reading confirmation is independent of a scroll's resulting buff or world operation.
		if (item is Scroll scroll && ScrollReading != null)
		{
			ItemAppearance appearance = Identification?.GetAppearance(scroll);
			if (appearance?.GlyphMesh != null)
				Host.PlayBurstConfigured(ScrollReading, ++_cosmeticSequence, instance => instance is RuneRingEffect ring && ring.Configure(appearance));
			else GD.PushWarning($"{Name}: scroll reading has no assigned glyph appearance.");
		}
		if (item.UseCue != null && !item.UseCue.IsEmpty && Cues.TryGetValue(item.UseCue, out var definition)) Host.PlayBurst(definition, ++_cosmeticSequence);
	}
}