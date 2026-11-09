using Godot;

/// <summary>
/// A presentation-only camera impulse at a world position, fading out over <see cref="Radius"/>, with its envelope
/// (see <see cref="CameraShakeProfile"/>). Independent of damage contacts.
/// </summary>
public readonly record struct CameraImpulseRequest(float Strength, float Radius, Vector3 Position,
	float GrowthTime, float HoldTime, float DecayTime);

public delegate void CameraImpulseRequestHandler(in CameraImpulseRequest request);
