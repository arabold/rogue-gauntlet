using Godot;
using System;
using System.Collections.Generic;

/// <summary>Optional structured combat diagnostics. Listeners own output and scope; no gameplay state is retained.</summary>
public static class CombatTrace
{
	public static event Action<Node, string, IReadOnlyDictionary<string, object>> Recorded;
	public static bool Enabled => Recorded != null;

	public static void Write(Node source, string eventId, params (string Key, object Value)[] fields)
	{
		if (Recorded == null) return;
		var data = new Dictionary<string, object>();
		foreach (var (key, value) in fields) data[key] = value;
		foreach (Action<Node, string, IReadOnlyDictionary<string, object>> listener in Recorded.GetInvocationList())
		{
			try { listener(source, eventId, data); }
			catch (Exception error)
			{
				Recorded -= listener;
				GD.PushWarning($"Combat trace listener disabled: {error.Message}");
			}
		}
	}

	public static string Identity(Node node) => GodotObject.IsInstanceValid(node)
		? (node.IsInsideTree() ? node.GetPath().ToString() : node.Name.ToString()) : "";
	public static float[] Position(Node3D node) => Vector(node.IsInsideTree() ? node.GlobalPosition : node.Position);
	public static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
}