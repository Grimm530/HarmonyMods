using System;
using UnityEngine;

namespace AlphaLoot.Harmony;

internal class DeferredSkinBlockInitializer : MonoBehaviour
{
	public Action OnReady;

	private float _startedAt;

	private float _nextStatusLogAt = 30f;

	private void Awake()
	{
		_startedAt = Time.realtimeSinceStartup;
	}

	private void Update()
	{
		if (!AlphaLootMod.AreSkinDefinitionsReady())
		{
			float num = Time.realtimeSinceStartup - _startedAt;
			if (num >= _nextStatusLogAt)
			{
				Debug.LogWarning((object)$"[AlphaLoot.Harmony] Still waiting {Mathf.RoundToInt(num)}s for item skin and Steam inventory definitions...");
				_nextStatusLogAt += 30f;
			}
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
