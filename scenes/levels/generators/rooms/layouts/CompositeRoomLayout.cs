using Godot;
using System.Linq;
using System.Collections.Generic;

/// <summary>Run reusable furnishing functions in order against one shared room reservation context. Empty is an explicit unfurnished room.</summary>
[Tool, GlobalClass]
public partial class CompositeRoomLayout : RoomContentLayout
{
	[Export] public RoomContentLayout[] Steps { get; set; } = [];
	public override RoomArrangementDefinition[] Definitions => HasCycle(new()) ? [] : (Steps ?? []).Where(step => step != null)
		.SelectMany(step => step.Definitions).DistinctBy(definition => definition.Id).ToArray();
	public override int MaximumCount => HasCycle(new()) ? 0 : Steps?.Where(step => step != null).Sum(step => step.MaximumCount) ?? 0;
	public override string[] Validate() => Steps == null || Steps.Any(step => step == null) || HasCycle(new())
		? ["Composite layout: assign valid, nonrecursive furnishing steps."] : Steps.SelectMany(step => step.Validate()).ToArray();
	public override string[] ValidateBaseLayout(RoomBaseLayout layout) => HasCycle(new()) ? [] : Steps?.Where(step => step != null)
		.SelectMany(step => step.ValidateBaseLayout(layout)).ToArray() ?? [];

	private bool HasCycle(HashSet<CompositeRoomLayout> path)
	{
		if (!path.Add(this)) return true;
		bool cycle = (Steps ?? []).OfType<CompositeRoomLayout>().Any(step => step.HasCycle(path));
		path.Remove(this);
		return cycle;
	}

	public override bool Populate(RoomFurnishingContext context, string key)
	{
		for (int index = 0; index < Steps.Length; index++)
			if (!Steps[index].Populate(context, $"{key}:step:{index}")) return false;
		return true;
	}
}