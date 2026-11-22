using Godot;

/// <summary>Immutable playback values captured when an attack is accepted.</summary>
public sealed record AttackPlayback(StringName Style, StringName Clip, double ClipLength, float ContactStart, float ContactEnd,
	StringName WindUpClip = null, double WindUpClipLength = 0);

/// <summary>
/// A spell an attack casts once at its contact time, instead of melee contact or projectiles. <paramref name="Revalidate"/>
/// lets the source re-check its target at release (still alive, in range, visible, worth it); null casts whenever the
/// spell itself allows.
/// </summary>
public sealed record SpellRelease(ICombatSpell Spell, SpellCastContext Context, System.Func<SpellCastContext, bool> Revalidate = null);

/// <summary>
/// One actor-owned execution snapshot; never writes timing back into authored Resources. A player combo step
/// records its index and damage scale; other attacks keep -1 and 1. A spell cast carries its <see cref="SpellRelease"/>.
/// </summary>
public sealed record PreparedAttack(AttackDefinition Definition, AttackPlayback Playback, double Duration,
	int ComboStep = -1, float DamageScale = 1, SpellRelease Spell = null, CombatFeedbackSettings? Feedback = null,
	PreparedHitStatus[] OnHitStatuses = null, SoundCueProfile VoiceCue = null);