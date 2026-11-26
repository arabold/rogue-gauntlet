using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Stages trap gauntlets ahead of the hero for designing and tuning them: <see cref="Rows"/> rows of one hazard across
/// the hero's path on the chosen <see cref="Pattern"/>, in an arena or on a floor. Hazards are the props rooms place
/// (<see cref="TrapDirectory"/>), mounted as rooms mount them: a wall prop hangs on the first wall beside the path, or on
/// a stand-in wall where there is none; a floor prop, such as a pendulum, stands on the path with its X across it. New
/// hazard props show up without a menu edit. Staged gauntlets carry no persistent identity, so a floor never saves them.
/// In an arena it also stages whole gauntlet rooms from <see cref="RoomDirectory"/>, entrance first, just ahead of the
/// hero, so a room can be played before any floor places it. <b>Triggered</b> stages a plate trap from
/// <see cref="TriggeredDirectory"/>, the profiles floors place: its plate, revealed, ahead of the hero and its launcher on
/// the wall beside it, facing it, set off in <see cref="PlateMode"/>.
/// </summary>
[GlobalClass]
public partial class TrapTool : DebugTool
{
	private static readonly int[] RowCounts = [1, 3, 5];
	private static readonly float[] RowSpacings = [4f, 6f, 8f];
	private static readonly StringName PositionKey = "position";
	/// <summary>Distance ahead of the hero to the first row.</summary>
	private const float FirstRowDistance = 4f;
	private const float MountProbeHeight = 1.1f;
	private static readonly Vector3 StandInWallSize = new(1.6f, 2.4f, .4f);

	/// <summary>Folder whose hazard props the stage submenu lists.</summary>
	[Export] public string TrapDirectory { get; set; } = "res://scenes/levels/rooms/props/traps";
	[Export(PropertyHint.Range, "1,8")] public int Rows { get; set => field = Mathf.Max(1, value); } = 3;
	[Export] public HazardPattern Pattern { get; set; } = HazardPattern.Wave;
	/// <summary>Seconds per beat of a staged gauntlet.</summary>
	[Export(PropertyHint.Range, "0.7,6,0.1,suffix:s")] public float Period { get; set; } = 2.4f;
	/// <summary>Distance between rows along the path: one tile, the closest rows a hero can still stop between, by default.</summary>
	[Export(PropertyHint.Range, "2,16,0.5,suffix:m")] public float RowSpacing { get; set; } = 4f;
	/// <summary>Folder whose gauntlet room types the rooms submenu lists.</summary>
	[Export] public string RoomDirectory { get; set; } = "res://scenes/levels/rooms/room_types/gauntlets";
	/// <summary>Seed of the next staged room; each staging takes the next one, so restaging shows another layout.</summary>
	[Export] public ulong RoomSeed { get; set; } = 1;
	/// <summary>How far beside the path a wall spout hangs when no wall is closer.</summary>
	[Export(PropertyHint.Range, "1,10,0.5,suffix:m")] public float LaneOffset { get; set; } = 4f;
	/// <summary>Folder whose plate trap profiles the triggered submenu lists.</summary>
	[Export] public string TriggeredDirectory { get; set; } = "res://scenes/props/traps/triggered";
	/// <summary>Folder whose trigger modes the plate mode submenu lists.</summary>
	[Export] public string ModeDirectory { get; set; } = "res://scenes/props/triggers/modes";
	/// <summary>How a staged plate sets off its launcher; null keeps the profile's mode.</summary>
	[Export] public TriggerMode PlateMode { get; set; }

	/// <summary>The gauntlets and rooms this tool staged; freed ones drop out on the next refresh.</summary>
	private readonly List<Node3D> _staged = [];
	private PopupMenu _stageMenu, _roomMenu, _triggeredMenu, _modeMenu;
	private int _clearId, _roomsIndex;

	protected override void BuildMenu(PopupMenu menu)
	{
		// Listing loads every hazard prop and room type, so the lists are filled when their submenus first open.
		_stageMenu = AddSubmenu(menu, "Stage", "StageTrapMenu");
		_stageMenu.AboutToPopup += FillStageMenu;
		_roomMenu = AddSubmenu(menu, "Rooms", "StageTrapRoomMenu");
		_roomsIndex = menu.ItemCount - 1;
		_roomMenu.AboutToPopup += FillRoomMenu;
		_triggeredMenu = AddSubmenu(menu, "Triggered", "StageTriggeredTrapMenu");
		_triggeredMenu.AboutToPopup += FillTriggeredMenu;
		_modeMenu = AddSubmenu(menu, "Plate mode", "TrapPlateModeMenu");
		_modeMenu.AboutToPopup += FillModeMenu;
		PopupMenu rows = AddSubmenu(menu, "Rows", "TrapRowsMenu");
		foreach (int count in RowCounts)
		{
			AddRadio(rows, count.ToString(), () => Rows == count, () => Rows = count);
		}
		PopupMenu spacings = AddSubmenu(menu, "Spacing", "TrapSpacingMenu");
		foreach (float spacing in RowSpacings)
		{
			AddRadio(spacings, $"{spacing:0} m", () => Mathf.IsEqualApprox(RowSpacing, spacing), () => RowSpacing = spacing);
		}
		PopupMenu patterns = AddSubmenu(menu, "Pattern", "TrapPatternMenu");
		foreach (HazardPattern pattern in System.Enum.GetValues<HazardPattern>())
		{
			AddRadio(patterns, pattern.ToString(), () => Pattern == pattern, () => Pattern = pattern);
		}
		menu.AddSeparator();
		_clearId = AddAction(menu, "Clear traps", Clear);
	}

	protected override void OnRefresh()
	{
		_staged.RemoveAll(node => !IsInstanceValid(node) || !node.IsInsideTree());
		SetDisabled(Popup, _clearId, _staged.Count == 0);
		Popup.SetItemDisabled(_roomsIndex, Menu?.World is not DebugArena);
	}

	/// <summary>
	/// Stages one gauntlet of <paramref name="hazard"/>, a hazard prop, ahead of the hero and returns it; null without a
	/// hero, a world or a hazard scene.
	/// </summary>
	public TrapGauntlet Stage(Prop hazard)
	{
		Player player = Menu?.Player;
		IDebugWorld world = Menu?.DebugWorld;
		if (hazard?.Scenes.FirstOrDefault() is not { } scene || player == null || world == null) return null;
		Vector3 origin = player.GlobalPosition;
		Vector3 forward = Heading(player);
		Vector3 right = forward.Cross(Vector3.Up);
		var space = player.GetWorld3D().DirectSpaceState;
		var gauntlet = new TrapGauntlet { Name = "StagedGauntlet" };
		for (int row = 0; row < Rows; row++)
		{
			Vector3 center = forward * (FirstRowDistance + row * RowSpacing);
			var node = scene.Instantiate<TrapHazard>();
			node.Tune(new HazardBeat(Period, TrapGauntlet.PhaseFor(Pattern, row)));
			gauntlet.AddChild(node);
			if (hazard.Mount == PropMount.Wall) HangBesidePath(gauntlet, node, center + Vector3.Up * hazard.MountHeight, right, space, origin);
			else node.Transform = new Transform3D(Basis.LookingAt(forward), center);
		}
		world.AddWorldNode(gauntlet, origin);
		_staged.Add(gauntlet);
		return gauntlet;
	}

	/// <summary>
	/// Stages <paramref name="profile"/> ahead of the hero and returns its root: its plate, revealed,
	/// <see cref="FirstRowDistance"/> ahead and its launcher hung on the wall beside it, facing it. Null without a hero, a
	/// world or a valid profile.
	/// </summary>
	public Node3D StageTriggered(TriggeredTrapProfile profile)
	{
		Player player = Menu?.Player;
		IDebugWorld world = Menu?.DebugWorld;
		if (profile?.IsValid != true || player == null || world == null) return null;
		Vector3 origin = player.GlobalPosition;
		Vector3 forward = Heading(player);
		var trap = new Node3D { Name = "StagedTriggeredTrap" };
		var launcher = profile.Launcher.Scenes[0].Instantiate<WallLauncher>();
		trap.AddChild(launcher);
		Vector3 center = forward * FirstRowDistance;
		HangBesidePath(trap, launcher, center, forward.Cross(Vector3.Up), player.GetWorld3D().DirectSpaceState, origin);
		var plate = profile.PlateScene.Instantiate<PressurePlate>();
		plate.Position = center;
		plate.Output.Mode = PlateMode ?? profile.Mode;
		plate.Output.Targets = [launcher];
		trap.AddChild(plate);
		world.AddWorldNode(trap, origin);
		plate.RestoreDiscovered(true);
		_staged.Add(trap);
		return trap;
	}

	/// <summary>
	/// Assembles <paramref name="type"/> in its first realm's look and stands it in the arena with its -Z doorway one
	/// step ahead of the hero, so walking forward enters the gauntlet. Null outside an arena or without a hero.
	/// </summary>
	public Room StageRoom(RoomType type)
	{
		Player player = Menu?.Player;
		if (type == null || player == null || Menu.World is not DebugArena arena) return null;
		var blueprint = RoomAssembler.Build(type, RoomSeed++, new RoomAssemblyOptions { Realm = type.Realms.FirstOrDefault() });
		var room = blueprint.Room;
		var entrance = blueprint.Doorways.OrderBy(doorway => doorway.Direction == Vector2I.Up ? 0 : 1).First();
		Vector3 forward = Heading(player);
		// The room's +Z, its length, runs along the hero's heading; the entrance's outer edge stands 2 m ahead.
		var basis = Basis.LookingAt(-forward);
		Vector3 doorway = entrance.Edge + entrance.Outward * 2;
		room.Transform = new Transform3D(basis, Vector3.Zero);
		// Lifted a hair so the room's floor covers the arena's own tiles instead of flickering with them.
		arena.AddWorldNode(room, player.GlobalPosition - basis * doorway + Vector3.Up * .02f);
		_staged.Add(room);
		return room;
	}

	/// <summary>Removes every gauntlet, room and plate trap this tool staged.</summary>
	public void Clear()
	{
		foreach (var node in _staged.Where(IsInstanceValid)) node.QueueFree();
		_staged.Clear();
	}

	/// <summary>
	/// Hangs <paramref name="hazard"/> facing the path on the first wall to its <paramref name="right"/>, or on a stand-in
	/// wall <see cref="LaneOffset"/> away, at <paramref name="center"/>'s height. Positions are relative to
	/// <paramref name="parent"/>, a staged gauntlet or plate trap at <paramref name="origin"/>.
	/// </summary>
	private void HangBesidePath(Node3D parent, Node3D hazard, Vector3 center, Vector3 right,
		PhysicsDirectSpaceState3D space, Vector3 origin)
	{
		Vector3 probe = origin + center with { Y = MountProbeHeight };
		var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(probe, probe + right * LaneOffset * 2, WorldSight.Structure));
		Vector3 mount = hit.Count > 0 ? hit[PositionKey].AsVector3() - origin : center + right * LaneOffset;
		mount.Y = center.Y;
		// A wall prop's front (+Z) faces into the room, so its back (-Z) faces the wall to the right.
		hazard.Transform = new Transform3D(Basis.LookingAt(right), mount);
		if (hit.Count == 0) parent.AddChild(StandInWall(mount with { Y = 0 } + right * StandInWallSize.Z / 2, right));
	}

	/// <summary>A plain stone slab with world collision to hang a spout on in open space.</summary>
	private static StaticBody3D StandInWall(Vector3 position, Vector3 right)
	{
		var wall = new StaticBody3D
		{
			Name = "StandInWall",
			Transform = new Transform3D(Basis.LookingAt(right), position + Vector3.Up * StandInWallSize.Y / 2),
			CollisionLayer = 1,
			CollisionMask = 0,
		};
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = StandInWallSize } });
		wall.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh
			{
				Size = StandInWallSize,
				Material = new StandardMaterial3D { AlbedoColor = new Color(.45f, .43f, .4f), Roughness = .9f },
			},
		});
		return wall;
	}

	private static Vector3 Heading(Player player) =>
		(player.MovementComponent?.LookAtDirection ?? -player.GlobalBasis.Z).TryNormalizeHorizontal(out Vector3 facing) ? facing : Vector3.Forward;

	private void FillRoomMenu()
	{
		if (_roomMenu.ItemCount > 0) return;
		foreach (var (type, path) in DebugCatalog.Resources<RoomType>(RoomDirectory))
		{
			AddAction(_roomMenu, type.Id.Replace('_', ' ').Capitalize(), () => StageRoom(type));
			_roomMenu.SetItemTooltip(_roomMenu.ItemCount - 1, path);
		}
	}

	private void FillTriggeredMenu()
	{
		if (_triggeredMenu.ItemCount > 0) return;
		foreach (var (profile, path) in DebugCatalog.Resources<TriggeredTrapProfile>(TriggeredDirectory))
		{
			AddAction(_triggeredMenu, path.GetFile().GetBaseName().Capitalize(), () => StageTriggered(profile));
			_triggeredMenu.SetItemTooltip(_triggeredMenu.ItemCount - 1, path);
		}
	}

	private void FillModeMenu()
	{
		if (_modeMenu.ItemCount > 0) return;
		AddRadio(_modeMenu, "As authored", () => PlateMode == null, () => PlateMode = null);
		foreach (var (mode, path) in DebugCatalog.Resources<TriggerMode>(ModeDirectory))
		{
			AddRadio(_modeMenu, path.GetFile().GetBaseName().Capitalize(), () => PlateMode == mode, () => PlateMode = mode);
		}
	}

	private void FillStageMenu()
	{
		if (_stageMenu.ItemCount > 0) return;
		foreach (var (hazard, path) in DebugCatalog.Resources<Prop>(TrapDirectory))
		{
			AddAction(_stageMenu, hazard.Id.Replace('_', ' ').Capitalize(), () => Stage(hazard));
			_stageMenu.SetItemTooltip(_stageMenu.ItemCount - 1, path);
		}
	}
}
