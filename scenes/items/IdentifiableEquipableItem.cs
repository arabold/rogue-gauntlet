using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Equipable item whose true identity is hidden until discovered, e.g. rings and
/// amulets ("a jade ring" until worn or identified). Mirrors <see cref="IdentifiableItem"/>'s
/// exports rather than sharing a base class with it, since <see cref="EquipableItem"/>
/// and <see cref="IdentifiableItem"/> are sibling branches of <see cref="BuffedItem"/>;
/// both implement <see cref="IIdentifiable"/> so the identification system can treat
/// them uniformly.
/// </summary>
[Tool]
[GlobalClass]
public partial class IdentifiableEquipableItem : EquipableItem, IIdentifiable
{
	/// <summary>Stable identity key, e.g. "ring.strength". Independent of file path.</summary>
	[Export] public string TypeId { get; protected set => SetValue(ref field, value); } = "";

	/// <summary>Identity category selecting the appearance pool, e.g. "ring".</summary>
	[Export] public string IdentityCategory { get; protected set => SetValue(ref field, value); } = "ring";

	/// <summary>Name shown once identified, e.g. "Ring of Strength".</summary>
	[Export] public string TrueName { get; protected set => SetValue(ref field, value); } = "";

	/// <summary>Unidentified label template; "{descriptor}" is replaced at runtime and the quality word leads it.</summary>
	[Export] public string UnidentifiedNameTemplate { get; protected set => SetValue(ref field, value); } = "{descriptor} ring";

	/// <summary>Jewelry has one model per appearance; its material belongs to the disguise, never to its quality.</summary>
	public string AppearanceForm => "";

	/// <summary>
	/// Stats this item grants intrinsically (its "effect") at scale 1. A Poor or Superior quality scales every one,
	/// harmful types included, so a superior Ring of Frailty is the worst of its kind. A plain modifier list rather than a
	/// <see cref="Buff"/> so two worn instances of the same type register under distinct item sources and reverse
	/// independently.
	/// </summary>
	[Export] public StatModifier[] IntrinsicModifiers { get; protected set => SetValue(ref field, value); } = [];

	public bool HasIdentity => !string.IsNullOrEmpty(TypeId);

	protected override IEnumerable<StatModifier> BuildIntrinsicModifiers() =>
		IntrinsicModifiers?.Where(modifier => modifier != null).Select(Scale) ?? [];

	/// <summary>
	/// Jewelry names end in the signed primary value: "Ring of Strength +3", "Shiny Ring of Strength +4", "Keen Ring of
	/// Strength +3". Suffixes only show in the tooltip, since the true name already reads "… of …".
	/// </summary>
	protected override string NameTail => IntrinsicModifiers?.FirstOrDefault(modifier => modifier != null) is { } primary
		? StatModifier.FormatValue(ScaledValue(primary), primary.IsFraction) : null;
}
