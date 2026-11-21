using Godot;
using System;
using System.Linq;

/// <summary>
/// Values the information visible to the hero. Both sides of the counter use this retail valuation. A known type is
/// worth its item's <see cref="Item.Value"/>, the one price scale every item carries; an unknown one is priced by what
/// the hero sees (its identity category and appearance form, such as a bottle's size), so a price never gives away
/// what an item is.
/// </summary>
[GlobalClass]
public partial class ShopPricingProfile : Resource
{
	[ExportGroup("Unknown types")]
	/// <summary>Prices of unknown types by what the hero sees: "category:form" (a "potion:small"), else the bare category ("scroll").</summary>
	[Export] public Godot.Collections.Dictionary<string, int> UnknownPrices { get; set; } = new();
	/// <summary>An unknown type <see cref="UnknownPrices"/> does not list.</summary>
	[Export(PropertyHint.Range, "1,1000,1,or_greater")] public int UnknownItemPrice { get; set; } = 25;

	[ExportGroup("Equipment")]
	[Export] public Godot.Collections.Dictionary<ItemQuality, float> QualityMultipliers { get; set; } = new();
	/// <summary>Gold per visible unit of an affix's stat, such as one percentage point of critical chance.</summary>
	[Export] public Godot.Collections.Dictionary<StatType, float> AffixUnitPrices { get; set; } = new();
	[Export(PropertyHint.Range, "0,100,0.1,or_greater")] public float DefaultAffixUnitPrice { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float MaxAffixPremium { get; set; } = .5f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float KnownCurseMultiplier { get; set; } = .5f;

	[ExportGroup("Resale")]
	[Export(PropertyHint.Range, "0,100,1,suffix:%")] public int ResalePercent { get; set; } = 20;

	public int RetailPrice(Item item, IdentificationService knowledge)
	{
		bool known = ItemIdentity.IsTypeKnown(item, knowledge);
		double value = known ? item.Value : UnknownPrice(item);
		if (item is EquipableItem gear)
		{
			double premium = known && gear.AttributesKnown ? Math.Min(value * MaxAffixPremium, AffixPremium(gear)) : 0;
			float multiplier = QualityMultipliers.TryGetValue(gear.Quality, out float configured) ? configured : 1f;
			value = value * multiplier + premium;
			if (gear.CurseStatusKnown && gear.IsCursed) value *= KnownCurseMultiplier;
		}
		return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 1, int.MaxValue);
	}

	public int SalePrice(Item item, IdentificationService knowledge) => (int)((long)RetailPrice(item, knowledge) * ResalePercent / 100);

	private int UnknownPrice(Item item) => item is IIdentifiable identity
		&& (UnknownPrices.TryGetValue($"{identity.IdentityCategory}:{identity.AppearanceForm}", out int price)
			|| UnknownPrices.TryGetValue(identity.IdentityCategory, out price)) ? price : UnknownItemPrice;

	private float AffixPremium(EquipableItem gear)
	{
		float premium = 0;
		foreach (var modifier in gear.Affixes.SelectMany(affix => affix.Modifiers))
		{
			if (!modifier.IsValid || modifier.Value <= 0) continue;
			float unitPrice = AffixUnitPrices.TryGetValue(modifier.Stat, out float price) ? price : DefaultAffixUnitPrice;
			premium += modifier.Round(modifier.Value) / modifier.Unit * unitPrice;
		}
		return premium;
	}
}