using Godot;
using Godot.Collections;

[Tool]
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

	[Export] public Dictionary<string, Texture2D> KeyboardDict { get; private set; } = new Dictionary<string, Texture2D>();
	[Export] public Dictionary<string, Texture2D> XboxDict { get; private set; } = new Dictionary<string, Texture2D>();

	public override void _Ready()
	{
		Update();
	}

	private void Update()
	{
		if (KeyboardDict.ContainsKey(ActionBinding))
		{
			Texture = KeyboardDict[ActionBinding];
			Modulate = IsDisabled ? new Color(1, 1, 1, 0.5f) : new Color(1, 1, 1, 1);
		}
		else
		{
			Texture = null;
		}
	}
}
