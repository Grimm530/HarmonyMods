namespace FakePopulation;

internal class HarmonyHooks : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		int bonus = FakePopulationConfig.Load().BonusPlayers;
		UnityEngine.Debug.Log("[FakePopulation] Loaded. BonusPlayers=" + bonus + " applies to GameTags (loading screen / Session). Play Community uses Facepunch's Steam player-count snapshot and cannot be inflated from the server.");
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args) { }
}
