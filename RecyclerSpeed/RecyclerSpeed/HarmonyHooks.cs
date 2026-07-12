namespace RecyclerSpeed;

internal class HarmonyHooks : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		HarmonyConfig.LoadConfig();
		var mod = new RecyclerSpeedMod();
		mod.OnLoaded(args);
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		RecyclerSpeedMod.Instance?.OnUnloaded(args);
	}
}
