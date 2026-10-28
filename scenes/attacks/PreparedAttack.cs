using Godot;

/// <summary>Immutable playback values captured when an attack is accepted.</summary>
public sealed record AttackPlayback(StringName Style, StringName Clip, double ClipLength, float ContactStart, float ContactEnd,
	StringName WindUpClip = null, double WindUpClipLength = 0);

/// <summary>
/// One actor-owned execution snapshot; never writes timing back into authored Resources. A player combo step
/// records its index and damage scale; other attacks keep -1 and 1.
/// </summary>
public sealed record PreparedAttack(AttackDefinition Definition, AttackPlayback Playback, double Duration,
	int ComboStep = -1, float DamageScale = 1);