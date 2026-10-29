using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class KeyBinding : TextureRect
{
	/// <summary>
	/// The action name associated with this key binding
	/// </summary>
	[Export]
	public string ActionBinding
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Update(); }
		}
	}

	[Export]
	public bool IsDisabled
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Update(); }
		}
	}

	[Export] public Dictionary<string, Texture2D> KeyboardDict { get; set; } = [];
	[Export] public Dictionary<string, Texture2D> XboxDict { get; set; } = [];

	public override void _Ready()
	{
		Update();
	}

	private void Update()
	{
		string key = ActionBinding;
		if (key != null && key.StartsWith("quick_item_"))
		{
			string altKey = "action_" + key.Substring("quick_item_".Length);
			if (!KeyboardDict.ContainsKey(key) && KeyboardDict.ContainsKey(altKey))
			{
				key = altKey;
			}
		}

		if (key != null && KeyboardDict.TryGetValue(key, out var tex))
		{
			Texture = tex;
			Modulate = IsDisabled ? new Color(0.5f, 0.5f, 0.5f, 1) : Colors.White;
		}
		else
		{
			Texture = null;
		}
	}
}