/// <summary>Door interaction operation ids, shared by <see cref="Door"/> offers/commits and <see cref="DoorLockComponent"/>.</summary>
public static class DoorOperation
{
	public const string Open = "open";
	public const string Close = "close";
	public const string KeyOpen = "key_open";
	public const string PickOpen = "pick_open";
	/// <summary>Shown but not actionable: the actor lacks a matching key or usable lockpick.</summary>
	public const string Locked = "locked";
}