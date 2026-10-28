using Godot;

/// <summary>
/// Ordered primary attacks of a weapon family, ending in a stronger finisher. The next step chains when the
/// player attacks again within <see cref="ChainWindow"/> of the previous step's recovery starting; otherwise
/// the combo starts over. Shared by every weapon of the family.
/// </summary>
[Tool]
[GlobalClass]
public partial class AttackCombo : Resource
{
	[Export] public Godot.Collections.Array<AttackComboStep> Steps { get; set; } = [];
	/// <summary>Seconds after a step's recovery starts during which the next step chains.</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float ChainWindow { get; set; } = 0.35f;

	public bool IsValid
	{
		get
		{
			if (Steps == null || Steps.Count == 0 || !float.IsFinite(ChainWindow) || ChainWindow < 0) return false;
			foreach (var step in Steps)
				if (step == null || !step.IsValid) return false;
			return true;
		}
	}
}
