using Godot;
using System.Collections.Generic;

/// <summary>A weighted pairing of compatible, independently reusable footprint and content policies.</summary>
[Tool, GlobalClass]
public partial class RoomLayoutVariant : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public RoomBaseLayout BaseLayout { get; set; }
	[Export] public RoomContentLayout Contents { get; set; }
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || !float.IsFinite(Weight) || Weight <= 0) errors.Add("Layout variant: assign a stable ID and positive weight.");
		errors.AddRange(BaseLayout?.Validate() ?? ["Layout variant: assign a base layout."]);
		errors.AddRange(Contents?.Validate() ?? ["Layout variant: assign contents."]);
		if (BaseLayout != null && Contents != null) errors.AddRange(Contents.ValidateBaseLayout(BaseLayout));
		return errors.ToArray();
	}
}