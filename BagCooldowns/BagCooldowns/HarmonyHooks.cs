namespace BagCooldowns;

internal class HarmonyHooks : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		HarmonyConfig.LoadConfig();
		HarmonyMethods.SetBagTimers();
		GrimmCoreBridge.RegisterSpawnPostfix("BagCooldowns", 100, BaseNetworkable_Spawn.Postfix);
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		GrimmCoreBridge.UnregisterSpawnMod("BagCooldowns");
		HarmonyMethods.ResetBagTimers();
	}
}
