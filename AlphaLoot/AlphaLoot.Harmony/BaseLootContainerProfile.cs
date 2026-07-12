namespace AlphaLoot.Harmony;

public class BaseLootContainerProfile : BaseLootProfile
{
	public bool DestroyOnEmpty = true;

	public bool ShouldRefreshContents;

	public bool IsItemLoot;

	public bool IsLootFill;

	public int MinSecondsBetweenRefresh = 3600;

	public int MaxSecondsBetweenRefresh = 7200;
}
