using Godot;

public static class NodeTreeExtensions
{
	/// <summary>
	/// Returns the closest ancestor of the requested type, or null when none exists.
	/// </summary>
	public static T GetAncestorOrNull<T>(this Node node) where T : Node
	{
		Node current = node.GetParent();
		while (current != null)
		{
			if (current is T typed)
			{
				return typed;
			}

			current = current.GetParent();
		}

		return null;
	}
}
