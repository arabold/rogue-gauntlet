/// <summary>
/// The quality of one equipment instance, rolled once at creation and always visible as its color and quality word.
/// Poor and Superior scale base stats; Magic and Rare add affixes instead. Ordered worst to best; a future Unique
/// quality goes after Rare.
/// </summary>
public enum ItemQuality
{
	Poor,
	Normal,
	Superior,
	Magic,
	Rare,
}
