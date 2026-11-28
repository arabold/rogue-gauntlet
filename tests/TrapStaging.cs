namespace RogueGauntlet.Tests;

using Godot;

/// <summary>
/// Stages traps in the combat arena ahead of the hero, along -Z, mounted as rooms and floors mount them but without a
/// room around them: a gauntlet of rows of one hazard prop, or a plate and the wall launcher it sets off. Gauntlet rooms
/// themselves play in the room generation harness.
/// </summary>
public static class TrapStaging
{
	/// <summary>From the hero to the first row, or to the plate.</summary>
	private const float Ahead = 4f;
	/// <summary>From the path to the wall a wall prop hangs on.</summary>
	private const float Lane = 4f;
	private static readonly Vector3 Forward = Vector3.Forward;
	/// <summary>The side wall props hang on; their front (+Z) faces back across the path.</summary>
	private static readonly Vector3 Side = Vector3.Right;

	/// <summary>
	/// <paramref name="rows"/> rows of <paramref name="hazard"/>, one tile apart, on <paramref name="pattern"/>'s beat of
	/// <paramref name="period"/> seconds: a wall prop hangs beside the path facing across it, a floor prop (a pendulum)
	/// stands on the path and swings across it.
	/// </summary>
	public static TrapGauntlet Gauntlet(CombatHarness harness, Prop hazard, int rows = 1, HazardPattern pattern = HazardPattern.Unison,
		float period = 1.2f)
	{
		var gauntlet = new TrapGauntlet { Name = "StagedGauntlet" };
		for (int row = 0; row < rows; row++)
		{
			var node = hazard.Scenes[0].Instantiate<TrapHazard>();
			node.Tune(new HazardBeat(period, TrapGauntlet.PhaseFor(pattern, row)));
			Vector3 center = Forward * (Ahead + row * Room.TileSize);
			node.Transform = hazard.Mount == PropMount.Wall
				? new Transform3D(Basis.LookingAt(Side), center + Side * Lane + Vector3.Up * hazard.MountHeight)
				: new Transform3D(Basis.LookingAt(Forward), center);
			gauntlet.AddChild(node);
		}
		harness.Arena.AddWorldNode(gauntlet, harness.Player.GlobalPosition);
		return gauntlet;
	}

	/// <summary>
	/// <paramref name="profile"/>'s plate, revealed, ahead of the hero, and its launcher on the wall beside it within the
	/// profile's lane, facing it; the plate sets the launcher off in <paramref name="mode"/>, or the profile's own.
	/// </summary>
	public static (PressurePlate Plate, WallLauncher Launcher) PlateTrap(CombatHarness harness, TriggeredTrapProfile profile,
		TriggerMode mode = null)
	{
		var trap = new Node3D { Name = "StagedPlateTrap" };
		Vector3 center = Forward * Ahead;
		var launcher = profile.Launcher.Scenes[0].Instantiate<WallLauncher>();
		launcher.Transform = new Transform3D(Basis.LookingAt(Side),
			center + Side * (profile.MinLane + profile.MaxLane) / 2 + Vector3.Up * profile.Launcher.MountHeight);
		trap.AddChild(launcher);
		var plate = profile.PlateScene.Instantiate<PressurePlate>();
		plate.Position = center;
		plate.Output.Mode = mode ?? profile.Mode;
		plate.Output.Targets = [launcher];
		trap.AddChild(plate);
		harness.Arena.AddWorldNode(trap, harness.Player.GlobalPosition);
		plate.RestoreDiscovered(true);
		return (plate, launcher);
	}
}