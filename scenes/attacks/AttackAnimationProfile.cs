using Godot;
using System.Linq;

/// <summary>Shared immutable animation mappings, authored per actor rig.</summary>
[GlobalClass]
public partial class AttackAnimationProfile : Resource
{
	[Export] public AttackAnimationBinding[] Bindings { get; set; } = [];

	/// <summary>Duplicate keys invalidate the profile rather than silently selecting an arbitrary clip.</summary>
	public bool TryGetBinding(StringName style, out AttackAnimationBinding binding)
	{
		binding = null;
		if (Bindings == null || Bindings.Any(value => value == null || !value.IsValid)
			|| Bindings.Select(value => value.Style).Distinct().Count() != Bindings.Length)
		{
			return false;
		}
		binding = Bindings.FirstOrDefault(value => value.Style == style);
		return binding != null;
	}
}