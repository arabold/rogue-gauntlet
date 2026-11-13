using Godot;

/// <summary>How much a food heals, sized by the balance model: fruit and drinks are small, bread and cheese medium, meat large.</summary>
public enum FoodPortion
{
	Small,
	Medium,
	Large,
}

[Tool]
[GlobalClass]
public partial class FoodItem : ConsumableItem
{
	/// <summary>Which of the balance model's food heals this restores; <c>simulate.py derive</c> writes its healing from it.</summary>
	[Export] public FoodPortion Portion { get; set => SetValue(ref field, value); }
	[Export] public float PoisonChance { get; set => SetValue(ref field, value); } = 0.2f;
	[Export] public PoisonBuff PoisonEffect { get; set => SetValue(ref field, value); }

	public bool IsPoisoned { get; private set; } = false;

	/// <summary>Only factory creation or save restoration sets contamination; loading a definition never rolls.</summary>
	internal void RestorePoison(bool poisoned) => IsPoisoned = poisoned;

	public override void PerformAction(Player player)
	{
		GD.Print($"Consuming food: {Name}");
		
		// Apply the beneficial effect (from ConsumableItem.ApplyBuff)
		base.PerformAction(player);

		// If the item was already marked as poisoned when it spawned
		if (IsPoisoned)
		{
			if (PoisonEffect != null)
			{
				GD.Print($"{player.Name} has been poisoned by {Name}!");
				player.BuffController.ApplyBuff(PoisonEffect);
			}
			else
			{
				GD.PushError($"FoodItem: {Name} has PoisonChance > 0 but no PoisonEffect assigned!");
			}
		}
	}
}