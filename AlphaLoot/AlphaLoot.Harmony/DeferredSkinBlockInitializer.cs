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

	private const float MaxWaitSeconds = 120f;

	private void Update()
	{
		float num = Time.realtimeSinceStartup - _startedAt;
		bool ready = AlphaLootMod.AreSkinDefinitionsReady();
		if (!ready && num < MaxWaitSeconds)
		{
			if (num >= _nextStatusLogAt)
			{
				Debug.LogWarning((object)$"[AlphaLoot.Harmony] Still waiting {Mathf.RoundToInt(num)}s for item skin definitions...");
				_nextStatusLogAt += 30f;
			}
			return;
		}
		if (!ready)
			Debug.LogWarning((object)$"[AlphaLoot.Harmony] Proceeding without full skin definitions after {Mathf.RoundToInt(num)}s wait.");
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
