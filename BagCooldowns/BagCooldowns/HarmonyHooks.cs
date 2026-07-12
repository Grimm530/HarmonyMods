namespace BagCooldowns;

internal class HarmonyHooks : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		HarmonyConfig.LoadConfig();
		HarmonyMethods.SetBagTimers();
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		HarmonyMethods.ResetBagTimers();
	}
}
