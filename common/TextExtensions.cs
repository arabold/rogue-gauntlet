using System.Linq;

/// <summary>Small helpers for composing display text, such as item names.</summary>
public static class TextExtensions
{
	/// <summary>The text with its first letter upper-cased; "" for null or empty text.</summary>
	public static string UpperFirst(this string text) =>
		string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text[1..];

	/// <summary>Joins the non-empty words with single spaces, e.g. ("Vicious", "Broadsword", null) → "Vicious Broadsword".</summary>
	public static string JoinWords(params string[] words) =>
		string.Join(" ", words.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()));
}
