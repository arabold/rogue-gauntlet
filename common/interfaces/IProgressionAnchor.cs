/// <summary>
/// Marks a progression-critical node such as the arrival point, a level exit or the gatekeeper arena. Generation never
/// locks its room behind optional access, and ordinary enemies never spawn on top of it.
/// </summary>
public interface IProgressionAnchor { }