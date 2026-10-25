using Godot;

/// <summary>
/// Slows the whole game, player and monsters alike, while active. The player's
/// <see cref="TimeScaleComponent"/> applies the slowest active <see cref="TimeScale"/> to
/// <see cref="Engine.TimeScale"/>, so overlapping sources never fight over the global value.
/// <see cref="Buff.Duration"/> counts game time, so a slowed buff also lasts longer in real time.
/// </summary>
[Tool]
[GlobalClass]
public partial class TimeScaleBuff : Buff
{
	/// <summary>Game speed while active; 0.5 runs the game at half speed.</summary>
	[Export(PropertyHint.Range, "0.1,1,0.05")] public float TimeScale { get; set; } = 0.5f;
}