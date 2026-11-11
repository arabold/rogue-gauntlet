using Godot;

/// <summary>
/// An authored hero preset the debug tools, harness scenarios and tests apply by path: the class whose stat profile and
/// attributes the hero takes, optional attribute overrides, and the exact equipment and backpack. Applying a build
/// replaces the hero's attributes and inventory but keeps their model; the run's class is unchanged.
/// </summary>
[Tool]
[GlobalClass]
public partial class HeroBuildProfile : Resource
{
	[Export] public string DisplayName { get; set; } = "";

	/// <summary>Class whose stat profile and attributes the build uses; unassigned keeps the hero's.</summary>
	[Export] public CharacterClass CharacterClass { get; set; }

	/// <summary>Uses the attributes below instead of the class's.</summary>
	[Export]
	public bool OverrideAttributes
	{
		get;
		set
		{
			field = value;
			NotifyPropertyListChanged();
		}
	}

	[Export] public float Strength { get; set; } = 10;
	[Export] public float Dexterity { get; set; } = 10;
	[Export] public float Vitality { get; set; } = 10;
	[Export] public float Intelligence { get; set; } = 10;

	/// <summary>Worn items, each in its entry's slot.</summary>
	[Export] public HeroBuildItem[] Equipment { get; set; } = [];

	/// <summary>Packed items.</summary>
	[Export] public HeroBuildItem[] Backpack { get; set; } = [];

	/// <summary>Hides the attribute fields unless the build overrides the class's.</summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		StringName name = property["name"].AsStringName();
		if (!OverrideAttributes && (name == PropertyName.Strength || name == PropertyName.Dexterity
			|| name == PropertyName.Vitality || name == PropertyName.Intelligence))
		{
			property["usage"] = (int)(property["usage"].As<PropertyUsageFlags>() & ~PropertyUsageFlags.Editor);
		}
	}
}
