using Godot;

[Tool]
public partial class Level : Node
{
	[Export] public bool AutoGenerateOnReady { get; set; } = true;

	public MapGenerator MapGenerator { get; private set; }
	private Node _runtimeWorldRoot;

	public void AddWorldNode(Node node)
	{
		// Runtime spawns live under one root so regeneration can clear them without
		// touching authored children such as MapGenerator, cameras, or UI hooks.
		_runtimeWorldRoot ??= GetOrCreateRuntimeWorldRoot();
		_runtimeWorldRoot.AddChild(node);
	}

	public void AddWorldNode(Node3D node, Vector3 globalPosition)
	{
		AddWorldNode(node);
		node.GlobalPosition = globalPosition;
	}

	public override void _Ready()
	{
		MapGenerator = GetNode<MapGenerator>("MapGenerator");
		if (Engine.IsEditorHint())
		{
			return;
		}

		_runtimeWorldRoot = GetOrCreateRuntimeWorldRoot();

		if (AutoGenerateOnReady)
		{
			Generate();
		}
	}

	public void ConfigureGeneration(ulong seed, uint dungeonDepth)
	{
		MapGenerator.Seed = seed;
		MapGenerator.DungeonDepth = dungeonDepth;
	}

	public void Generate()
	{
		GD.Print("Initializing level...");
		// Clear previous runtime spawns before rebuilding the map. This matters if a
		// level is regenerated in place rather than replaced by a full scene reload.
		ClearWorldNodes();
		MapGenerator.GenerateMap();

		GD.Print("Level is ready");
		SignalBus.EmitLevelLoaded(this);
	}

	private Node GetOrCreateRuntimeWorldRoot()
	{
		Node root = GetNodeOrNull("RuntimeWorld");
		if (root != null)
		{
			return root;
		}

		// Create this at runtime only; the Level script is a tool script and should not
		// mutate the authored scene just because it was opened in the editor.
		root = new Node { Name = "RuntimeWorld" };
		AddChild(root);
		return root;
	}

	private void ClearWorldNodes()
	{
		_runtimeWorldRoot ??= GetOrCreateRuntimeWorldRoot();
		foreach (Node node in _runtimeWorldRoot.GetChildren())
		{
			// Detach first so group queries and signal listeners stop seeing stale nodes
			// immediately, even though QueueFree runs at the end of the frame.
			_runtimeWorldRoot.RemoveChild(node);
			node.QueueFree();
		}
	}
}
