using Godot;

/// <summary>A presentation-only camera impulse with an authored envelope, independent of damage contacts.</summary>
public readonly record struct CameraImpulseRequest(float Strength, float Radius, Vector3 Position,
	float? GrowthTime = null, float? Duration = null, float? DecayTime = null);

public delegate void CameraImpulseRequestHandler(in CameraImpulseRequest request);
