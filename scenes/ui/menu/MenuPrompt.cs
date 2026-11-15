using Godot;

/// <summary>
/// One thing the open run-menu tab offers for the selected item: a short label and the input action that does it.
/// Several actions make a range ("1–4 Quick Slot") whose keys each do it for their own slot.
/// </summary>
public sealed record MenuPrompt(string Label, params StringName[] Actions);