namespace CustomGenerator.Server;

internal class ModHooks : IHarmonyModHooks
{
	void IHarmonyModHooks.OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Log.Info("Loaded");
	}

	void IHarmonyModHooks.OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		Log.Info("Unloaded");
	}
}
