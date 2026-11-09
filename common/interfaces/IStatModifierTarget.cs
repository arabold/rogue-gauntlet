using System.Collections.Generic;

/// <summary>A capability for applying reversible stat modifiers to an actor.</summary>
public interface IStatModifierTarget
{
	bool SupportsModifiers(IEnumerable<StatModifier> modifiers);
	bool AddModifiers(object source, IEnumerable<StatModifier> modifiers);
	void RemoveModifiersFrom(object source);
}
