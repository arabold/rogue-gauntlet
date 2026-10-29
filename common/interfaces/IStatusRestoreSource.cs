/// <summary>A source that tracks its targets can reconnect them when a status is restored.</summary>
public interface IStatusRestoreSource
{
	void StatusRestored(BuffController target);
}
