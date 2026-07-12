using UnityEngine;

namespace CommandHistory;

public class EntryPoint : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Debug.Log((object)"Loaded CommandHistory by turner1337");
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		Debug.Log((object)"Unloaded CommandHistory by turner1337");
	}
}
