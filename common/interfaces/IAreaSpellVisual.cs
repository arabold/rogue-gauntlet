/// <summary>An authored visual that can represent a clipped, self-centered area spell.</summary>
public interface IAreaSpellVisual
{
	bool ConfigureArea(int level, float radius);
	void SetAreaVisibility(AreaVisibility visibility);
	/// <summary>How far the configured effect has travelled outward, so contacts wait until it arrives.</summary>
	AreaFront Front { get; }
}
