namespace CraftingSpeed;

internal class HarmonyHooks : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		HarmonyConfig.LoadConfig();
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
	}
}
