using Godot;
using System.Linq;

/// <summary>
/// In-game debug controls: hosts the <see cref="DebugTool"/>s a scene wires, one submenu each, plus an FPS and node-count
/// readout. Visible only in debug builds; the tools' methods work either way, for harnesses and tests.
/// </summary>
public partial class DebugMenu : HBoxContainer
{
	/// <summary>The tools listed in the menu, in order.</summary>
	[Export] public DebugTool[] Tools { get; set; } = [];

	/// <summary>The world the tools act on: the gameplay <see cref="Level"/>, or a harness arena. Must implement <see cref="IDebugWorld"/>.</summary>
	[Export] public Node World { get; set; }

	/// <summary>The world as tools use it; null when <see cref="World"/> is unset or does not implement <see cref="IDebugWorld"/>.</summary>
	public IDebugWorld DebugWorld => World as IDebugWorld;

	/// <summary>The generated floor, for floor-only actions such as revealing the map; null in a harness arena.</summary>
	public Level Level => World as Level;

	/// <summary>The current player; null while there is none.</summary>
	public Player Player => IsInsideTree() ? GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault() : null;

	private Label _fpsLabel;
	private double _fpsUpdateSeconds;

	/// <summary>The wired tool of type <typeparamref name="T"/>; throws when the scene does not wire one.</summary>
	public T GetTool<T>() where T : DebugTool =>
		Tools.OfType<T>().FirstOrDefault() ?? throw new System.InvalidOperationException($"{Name} has no {typeof(T).Name}.");

	public override void _Ready()
	{
		foreach (DebugTool tool in Tools)
		{
			if (tool == null) { GD.PushError($"{Name}: a Tools entry is unassigned."); continue; }
			tool.Menu = this;
		}
		if (World != null && DebugWorld == null)
		{
			GD.PushError($"{Name}: World {World.Name} is not an {nameof(IDebugWorld)}; world actions are unavailable.");
		}

		Visible = OS.IsDebugBuild();
		if (!Visible)
		{
			return;
		}

		_fpsLabel = GetNode<Label>("%FpsLabel");
		PopupMenu root = GetNode<MenuButton>("%DebugMenuButton").GetPopup();
		foreach (DebugTool tool in Tools.Where(tool => tool?.IsAvailable == true))
		{
			var submenu = new PopupMenu { Name = $"{tool.Name}Menu" };
			tool.Popup = submenu;
			// Built when first opened and refreshed on every opening, so checks follow changes made by code and other tools.
			submenu.AboutToPopup += tool.Refresh;
			root.AddChild(submenu);
			root.AddSubmenuNodeItem(tool.Title, submenu);
		}
		UpdateFpsLabel();
	}

	public override void _Process(double delta)
	{
		if (!Visible)
		{
			return;
		}

		_fpsUpdateSeconds += delta;
		if (_fpsUpdateSeconds >= 0.25)
		{
			_fpsUpdateSeconds = 0;
			UpdateFpsLabel();
		}
	}

	private string GetRuntimeDebugSummary()
	{
		int enemies = GetTree().GetNodesInGroup("enemy").Count;
		int nodes = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		int drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		return $"Enemies: {enemies}  Nodes: {nodes}  Draw: {drawCalls}\n{ScenePool.GetDebugSummary()}";
	}

	private void UpdateFpsLabel()
	{
		_fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}\n{GetRuntimeDebugSummary()}";
	}
}
