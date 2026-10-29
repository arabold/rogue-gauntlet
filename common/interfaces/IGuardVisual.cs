/// <summary>Presentation contract for an individually configured sustained guard.</summary>
public interface IGuardVisual
{
	bool ConfigureGuard(int level, ulong cosmeticSeed);
	void FlashGuard();
}
