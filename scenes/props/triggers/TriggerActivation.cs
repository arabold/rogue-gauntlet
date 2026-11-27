using Godot;

/// <summary>Who changed a trigger source: the actor that pressed, pulled or left it, null for a source that changed by itself.</summary>
public readonly record struct TriggerActivation(Node3D Activator);