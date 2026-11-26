using Godot;

/// <summary>
/// Who or what changed a trigger source: the source and the actor that pressed, pulled or left it. <see cref="Restored"/>
/// is set when a saved state is replayed on load, so receivers apply it without effects.
/// </summary>
public readonly record struct TriggerActivation(Node Source, Node3D Activator, bool Restored = false);