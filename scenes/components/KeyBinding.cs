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
		get => _actionBinding;
		set
		{
			_actionBinding = value;
			if (IsNodeReady())
			{
				Update();
			}
		}
	}

	[Export] public Dictionary<string, Texture2D> KeyboardDict { get; private set; } = new Dictionary<string, Texture2D>();
	[Export] public Dictionary<string, Texture2D> XboxDict { get; private set; } = new Dictionary<string, Texture2D>();

	private string _actionBinding;

	public override void _Ready()
	{
		Update();
	}

	private void Update()
	{
		if (KeyboardDict.ContainsKey(ActionBinding))
		{
			Texture = KeyboardDict[ActionBinding];
		}
		else
		{
			Texture = null;
		}
	}
}
