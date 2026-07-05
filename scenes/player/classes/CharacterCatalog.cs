using System.Linq;
using Godot;

/// <summary>
/// Ordered list of the playable character classes. The character selector iterates it and
/// GameSession resolves saved class ids against it; the first entry is the fallback for
/// pre-class saves and unknown ids.
/// </summary>
[GlobalClass]
public partial class CharacterCatalog : Resource
{
	/// <summary>
	/// Well-known catalog location. GameSession is a script autoload without exported
	/// fields, so it loads the catalog by path (the same pattern IdentificationService uses
	/// for the identity catalog).
	/// </summary>
	public const string DefaultCatalogPath = "res://scenes/player/classes/character_catalog.tres";

	[Export] public Godot.Collections.Array<CharacterClass> Classes { get; set; } = new();

	public CharacterClass DefaultClass => Classes.Count > 0 ? Classes[0] : null;

	public CharacterClass FindById(string id) =>
		string.IsNullOrEmpty(id) ? null : Classes.FirstOrDefault(c => c != null && c.Id == id);

	public static CharacterCatalog LoadDefault() => ResourceLoader.Load<CharacterCatalog>(DefaultCatalogPath);
}
