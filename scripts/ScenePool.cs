using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reuses short-lived runtime scenes such as projectiles and VFX to reduce allocation spikes during combat.
/// </summary>
public partial class ScenePool : Node
{
	[Export] public int MaxInactivePerScene { get; set; } = 32;

	public static ScenePool Instance { get; private set; }

	private readonly Dictionary<string, PoolBucket> _pools = [];
	private readonly Dictionary<Node, string> _nodeKeys = [];
	private readonly HashSet<Node> _inactiveNodes = [];
	private readonly HashSet<Node> _poolMoves = [];

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public static T Spawn<T>(PackedScene scene, Node parent) where T : Node
	{
		if (Instance == null)
		{
			T fallbackNode = scene.Instantiate<T>();
			parent.AddChild(fallbackNode);
			return fallbackNode;
		}

		return Instance.SpawnInternal<T>(scene, parent);
	}

	public static void Despawn(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
		{
			return;
		}

		if (Instance == null || !Instance._nodeKeys.ContainsKey(node))
		{
			node.QueueFree();
			return;
		}

		Instance.DespawnInternal(node);
	}

	public static bool IsTracked(Node node)
	{
		return Instance != null && node != null && Instance._nodeKeys.ContainsKey(node);
	}

	public static string GetDebugSummary()
	{
		return Instance == null ? "Pool: off" : Instance.BuildDebugSummary();
	}

	private T SpawnInternal<T>(PackedScene scene, Node parent) where T : Node
	{
		string key = GetSceneKey(scene);
		PoolBucket bucket = GetBucket(key, scene);
		bool reused = bucket.Inactive.Count > 0;
		Node node;
		if (reused)
		{
			node = bucket.Inactive[^1];
			bucket.Inactive.RemoveAt(bucket.Inactive.Count - 1);
		}
		else
		{
			node = scene.Instantiate<T>();
			Track(node, key, bucket);
		}
		bucket.ActiveCount++;
		_inactiveNodes.Remove(node);

		node.ProcessMode = ProcessModeEnum.Inherit;
		SetVisible(node, true);
		Move(node, parent);
		if (reused)
		{
			NotifySpawned(node);
		}
		return (T)node;
	}

	private void DespawnInternal(Node node)
	{
		if (_inactiveNodes.Contains(node))
		{
			return;
		}

		string key = _nodeKeys[node];
		PoolBucket bucket = GetBucket(key, null);
		if (bucket.ActiveCount > 0)
		{
			bucket.ActiveCount--;
		}
		NotifyDespawned(node);

		if (bucket.Inactive.Count >= MaxInactivePerScene)
		{
			_nodeKeys.Remove(node);
			_inactiveNodes.Remove(node);
			node.QueueFree();
			return;
		}

		node.ProcessMode = ProcessModeEnum.Disabled;
		SetVisible(node, false);
		Move(node, this);
		_inactiveNodes.Add(node);
		bucket.Inactive.Add(node);
	}

	/// <summary>External scene teardown must release membership and its managed references immediately.</summary>
	private void Track(Node node, string key, PoolBucket bucket)
	{
		_nodeKeys.Add(node, key);
		node.TreeExiting += OnTreeExiting;
		void OnTreeExiting()
		{
			if (_poolMoves.Contains(node)) return;
			node.TreeExiting -= OnTreeExiting;
			if (!_nodeKeys.Remove(node)) return;
			if (_inactiveNodes.Remove(node)) bucket.Inactive.Remove(node);
			else bucket.ActiveCount--;
		}
	}

	/// <summary>Pool-owned reparenting keeps membership while ordinary tree exits relinquish it.</summary>
	private void Move(Node node, Node parent)
	{
		_poolMoves.Add(node);
		try
		{
			node.GetParent()?.RemoveChild(node);
			parent.AddChild(node);
		}
		finally { _poolMoves.Remove(node); }
	}

	private PoolBucket GetBucket(string key, PackedScene scene)
	{
		if (!_pools.TryGetValue(key, out PoolBucket bucket))
		{
			bucket = new PoolBucket(scene);
			_pools[key] = bucket;
		}
		else if (bucket.Scene == null && scene != null)
		{
			bucket.Scene = scene;
		}

		return bucket;
	}

	private static string GetSceneKey(PackedScene scene)
	{
		return string.IsNullOrEmpty(scene.ResourcePath)
			? scene.GetInstanceId().ToString()
			: scene.ResourcePath;
	}

	private string BuildDebugSummary()
	{
		int activeCount = 0;
		int inactiveCount = 0;
		foreach (PoolBucket bucket in _pools.Values)
		{
			activeCount += bucket.ActiveCount;
			inactiveCount += bucket.Inactive.Count;
		}

		string[] topActiveScenes = _pools
			.Where(pair => pair.Value.ActiveCount > 0)
			.OrderByDescending(pair => pair.Value.ActiveCount)
			.Take(3)
			.Select(pair => $"{GetShortSceneName(pair.Key)}:{pair.Value.ActiveCount}")
			.ToArray();

		string summary = $"Pool: {activeCount} active / {inactiveCount} cached";
		return topActiveScenes.Length == 0 ? summary : $"{summary} ({string.Join(", ", topActiveScenes)})";
	}

	private static string GetShortSceneName(string key)
	{
		int slashIndex = key.LastIndexOf('/');
		string filename = slashIndex >= 0 ? key[(slashIndex + 1)..] : key;
		return filename.EndsWith(".tscn") ? filename[..^5] : filename;
	}

	private static void NotifySpawned(Node node)
	{
		if (node is IPooledNode pooledNode)
		{
			pooledNode.OnSpawnedFromPool();
		}

		foreach (Node child in node.GetChildren())
		{
			NotifySpawned(child);
		}
	}

	private static void NotifyDespawned(Node node)
	{
		if (node is IPooledNode pooledNode)
		{
			pooledNode.OnDespawnedToPool();
		}

		foreach (Node child in node.GetChildren())
		{
			NotifyDespawned(child);
		}
	}

	private static void SetVisible(Node node, bool visible)
	{
		if (node is Node3D node3D)
		{
			node3D.Visible = visible;
		}
		else if (node is CanvasItem canvasItem)
		{
			canvasItem.Visible = visible;
		}
	}

	private sealed class PoolBucket(PackedScene scene)
	{
		public PackedScene Scene = scene;
		public int ActiveCount;
		public List<Node> Inactive { get; } = [];
	}
}

public interface IPooledNode
{
	void OnSpawnedFromPool();
	void OnDespawnedToPool();
}