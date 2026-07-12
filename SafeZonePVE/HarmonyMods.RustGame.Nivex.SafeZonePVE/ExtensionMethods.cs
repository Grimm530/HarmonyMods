using UnityEngine;

namespace HarmonyMods.RustGame.Nivex.SafeZonePVE
{
	public static class ExtensionMethods
	{
		public static bool IsSteamId(this string id)
		{
			if (ulong.TryParse(id, out var result))
			{
				return result > 76561197960265728L;
			}
			return false;
		}

		public static bool Cast<T>(this BaseNetworkable entity, out T component) where T : BaseNetworkable
		{
			if ((UnityEngine.Object)(object)entity == (UnityEngine.Object)null)
			{
				component = null;
				return false;
			}
			component = entity as T;
			return (UnityEngine.Object)(object)component != (UnityEngine.Object)null;
		}
	}
}
