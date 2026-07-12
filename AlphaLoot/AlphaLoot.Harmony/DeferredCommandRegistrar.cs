using System;
using UnityEngine;

namespace AlphaLoot.Harmony;

internal class DeferredCommandRegistrar : MonoBehaviour
{
	public Action OnReady;

	private void Update()
	{
		if (ConsoleSystem.Index.All == null)
		{
			return;
		}
		try
		{
			OnReady?.Invoke();
		}
		finally
		{
			OnReady = null;
			Object.Destroy((Object)(object)((Component)this).gameObject);
		}
	}
}
