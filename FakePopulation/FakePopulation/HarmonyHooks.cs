namespace FakePopulation;

internal class HarmonyHooks : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		UnityEngine.Debug.Log("[FakePopulation] Loaded - server browser shows inflated player count. Edit HarmonyConfig/FakePopulation.json (BonusPlayers) to change amount.");
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args) { }
}
