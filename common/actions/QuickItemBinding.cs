using Godot;

public enum QuickItemBindingKind
{
	Unassigned = 0,
	Variety,
	Instance
}

public sealed class QuickItemBinding
{
	public QuickItemBindingKind Kind { get; set; } = QuickItemBindingKind.Unassigned;
	public string DefinitionPath { get; set; } = string.Empty;
	public string InstanceId { get; set; } = string.Empty;

	public bool IsAssigned => Kind != QuickItemBindingKind.Unassigned;

	public static QuickItemBinding Unassigned() => new() { Kind = QuickItemBindingKind.Unassigned };

	public static QuickItemBinding ForVariety(string definitionPath) => new()
	{
		Kind = QuickItemBindingKind.Variety,
		DefinitionPath = definitionPath ?? string.Empty
	};

	public static QuickItemBinding ForInstance(string instanceId, string definitionPath) => new()
	{
		Kind = QuickItemBindingKind.Instance,
		InstanceId = instanceId ?? string.Empty,
		DefinitionPath = definitionPath ?? string.Empty
	};

	public QuickItemBinding Clone() => new()
	{
		Kind = Kind,
		DefinitionPath = DefinitionPath,
		InstanceId = InstanceId
	};
}