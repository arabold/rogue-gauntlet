using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Stages trap gauntlets ahead of the hero for designing and tuning them: <see cref="Rows"/> rows of one hazard across
/// the hero's path on the chosen <see cref="Pattern"/>, in an arena or on a floor. A wall spout hangs on the first wall
/// beside the path, or on a stand-in wall where there is none; any other hazard, such as a pendulum, stands on the path
/// with its X across it. Every hazard scene in <see cref="TrapDirectory"/> is
/// listed, so a new one needs no menu edit. Staged gauntlets carry no persistent identity, so a floor never saves them.
/// In an arena it also stages whole gauntlet rooms from <see cref="RoomDirectory"/>, entrance first, just ahead of the
/// hero, so a room can be played before any floor places it.
/// </summary>
[GlobalClass]
public partial class TrapTool : DebugTool
{
	private static readonly int[] RowCounts = [1, 3, 5];
	private static readonly float[] RowSpacings = [4f, 6f, 8f];
	private static readonly StringName PositionKey = "position";
	/// <summary>World and wall layers: what a spout may hang on.</summary>
	private const uint WallMask = (1 << 0) | (1 << 1);
	/// <summary>Distance ahead of the hero to the first row.</summary>
	private const float FirstRowDistance = 4f;
	private const float MountProbeHeight = 1.1f;
	private static readonly Vector3 StandInWallSize = new(1.6f, 2.4f, .4f);

	/// <summary>Folder whose hazard scenes the stage submenu lists.</summary>
	[Export] public string TrapDirectory { get; set; } = "res://scenes/props/traps";
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

	/// <summary>The live gauntlets this tool staged, in staging order.</summary>
	public IReadOnlyList<TrapGauntlet> Staged
	{
		get
		{
			_staged.RemoveAll(gauntlet => !IsInstanceValid(gauntlet) || !gauntlet.IsInsideTree());
			return _staged;
		}
	}

	/// <summary>The live gauntlet rooms this tool staged, in staging order.</summary>
	public IReadOnlyList<Room> StagedRooms
	{
		get
		{
			_rooms.RemoveAll(room => !IsInstanceValid(room) || !room.IsInsideTree());
			return _rooms;
		}
	}

	private readonly List<TrapGauntlet> _staged = [];
	private readonly List<Room> _rooms = [];
	private PopupMenu _stageMenu, _roomMenu;
	private int _clearId, _roomsIndex;

	protected override void BuildMenu(PopupMenu menu)
	{
		// Checking a scene's type instantiates it, so the list is filled when the submenu first opens.
		_stageMenu = AddSubmenu(menu, "Stage", "StageTrapMenu");
		_stageMenu.AboutToPopup += FillStageMenu;
		_roomMenu = AddSubmenu(menu, "Rooms", "StageTrapRoomMenu");
		_roomsIndex = menu.ItemCount - 1;
		_roomMenu.AboutToPopup += FillRoomMenu;
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
		SetDisabled(Popup, _clearId, Staged.Count == 0 && StagedRooms.Count == 0);
		Popup.SetItemDisabled(_roomsIndex, Menu?.World is not DebugArena);
	}

	/// <summary>
	/// Stages one gauntlet of <paramref name="hazardScene"/> ahead of the hero and returns it; null without a hero, a
	/// world or a scene.
	/// </summary>
	public TrapGauntlet Stage(PackedScene hazardScene)
	{
		Player player = Menu?.Player;
		IDebugWorld world = Menu?.DebugWorld;
		if (hazardScene == null || player == null || world == null) return null;
		Vector3 origin = player.GlobalPosition;
		Vector3 forward = Heading(player);
		Vector3 right = forward.Cross(Vector3.Up);
		var space = player.GetWorld3D().DirectSpaceState;
		var gauntlet = new TrapGauntlet { Name = "StagedGauntlet" };
		for (int row = 0; row < Rows; row++)
		{
			Vector3 center = origin + forward * (FirstRowDistance + row * RowSpacing);
			var hazard = hazardScene.Instantiate<Node3D>();
			gauntlet.AddChild(hazard);
			if (hazard is WallSpout) HangBesidePath(gauntlet, hazard, center - origin, right, space, origin);
			else hazard.Transform = new Transform3D(Basis.LookingAt(forward), center - origin);
		}
		gauntlet.ApplyPattern(Pattern, Period, forward, player.Stats.Speed);
		world.AddWorldNode(gauntlet, origin);
		_staged.Add(gauntlet);
		return gauntlet;
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
		_rooms.Add(room);
		return room;
	}

	/// <summary>Removes every gauntlet and room this tool staged.</summary>
	public void Clear()
	{
		foreach (TrapGauntlet gauntlet in Staged) gauntlet.QueueFree();
		foreach (Room room in StagedRooms) room.QueueFree();
		_staged.Clear();
		_rooms.Clear();
	}

	/// <summary>
	/// Hangs <paramref name="spout"/> facing the path on the first wall to its <paramref name="right"/>, or on a stand-in
	/// wall <see cref="LaneOffset"/> away. Positions are relative to the gauntlet at <paramref name="origin"/>.
	/// </summary>
	private void HangBesidePath(TrapGauntlet gauntlet, Node3D spout, Vector3 center, Vector3 right,
		PhysicsDirectSpaceState3D space, Vector3 origin)
	{
		Vector3 probe = origin + center + Vector3.Up * MountProbeHeight;
		var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(probe, probe + right * LaneOffset * 2, WallMask));
		Vector3 mount = hit.Count > 0 ? hit[PositionKey].AsVector3() - origin : center + right * LaneOffset;
		mount.Y = center.Y;
		// A spout fires along its front (+Z), so its back (-Z) faces the wall to the right.
		spout.Transform = new Transform3D(Basis.LookingAt(right), mount);
		if (hit.Count == 0) gauntlet.AddChild(StandInWall(mount + right * StandInWallSize.Z / 2, right));
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

	private static Vector3 Heading(Player player)
	{
		Vector3 facing = (player.MovementComponent?.LookAtDirection ?? -player.GlobalBasis.Z) with { Y = 0 };
		return facing.LengthSquared() > 1e-6f ? facing.Normalized() : Vector3.Forward;
	}

	private void FillRoomMenu()
	{
		if (_roomMenu.ItemCount > 0) return;
		foreach (var (type, path) in DebugCatalog.Resources<RoomType>(RoomDirectory))
		{
			AddAction(_roomMenu, type.Id.Replace('_', ' ').Capitalize(), () => StageRoom(type));
			_roomMenu.SetItemTooltip(_roomMenu.ItemCount - 1, path);
		}
	}

	private void FillStageMenu()
	{
		if (_stageMenu.ItemCount > 0) return;
		foreach (var (scene, name) in DebugCatalog.Hazards(TrapDirectory))
		{
			AddAction(_stageMenu, name, () => Stage(scene));
			_stageMenu.SetItemTooltip(_stageMenu.ItemCount - 1, scene.ResourcePath);
		}
	}
}
