using Godot;

/// <summary>A presentation-only impulse requested at a world position, independent of damage contacts.</summary>
public readonly record struct CameraImpulseRequest(float Strength, float Radius, Vector3 Position);

public delegate void CameraImpulseRequestHandler(in CameraImpulseRequest request);
