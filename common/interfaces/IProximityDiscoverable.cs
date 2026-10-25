using Godot;

/// <summary>
/// A hidden object that <see cref="ProximityDiscoveryComponent"/> can find. The object owns its
/// eligibility rules and what being discovered means; the component owns cadence, sight and the roll.
/// </summary>
public interface IProximityDiscoverable
{
	/// <summary>Still hidden and findable right now (e.g. not yet discovered, in explored space).</summary>
	bool IsDiscoverable { get; }
	/// <summary>Ground anchor measured against the detection radius and vertical tolerance.</summary>
	Vector3 DiscoveryAnchor { get; }
	/// <summary>Point an observer's eye ray must reach.</summary>
	Vector3 SightTarget { get; }
	/// <summary>Side or facing rules beyond range, e.g. a concealed door is only found from its approach side.</summary>
	bool CanBeObservedFrom(Vector3 observer) => true;
	/// <summary>Whether a sight-ray hit (collider, point) is the object's own concealing geometry rather than a blocker.</summary>
	bool AcceptsSightHit(GodotObject collider, Vector3 point) => false;
	void Discover();
}