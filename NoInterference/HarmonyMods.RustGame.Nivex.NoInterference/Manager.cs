using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace HarmonyMods.RustGame.Nivex.NoInterference;

public class Manager : IHarmonyModHooks
{
	[HarmonyPatch(typeof(ServerMgr), "OpenConnection")]
	internal class ServerMgr_OpenConnection
	{
		[HarmonyPostfix]
		private static void Postfix()
        {
            foreach (BaseNetworkable current in BaseNetworkable.serverEntities)
            {
                if (current is AutoTurret autoTurret && !HandleInterference(autoTurret))
                {
                    autoTurret.SetFlag(BaseEntity.Flags.OnFire, b: false);
                }
            }
        }
	}

    // Game method names changed; target current interference flow
    [HarmonyPatch(typeof(AutoTurret), "RecalculateInterference")]
    internal class AutoTurret_RecalculateInterference
    {
        [HarmonyPrefix]
        internal static bool Prefix(AutoTurret __instance)
        {
            if (!HandleInterference(__instance))
            {
                __instance.SetFlag(BaseEntity.Flags.OnFire, b: false);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AutoTurret), "SetInterferenceEnabled")]
    internal class AutoTurret_SetInterferenceEnabled
    {
        [HarmonyPrefix]
        internal static bool Prefix(AutoTurret __instance, ref bool state)
        {
            if (!HandleInterference(__instance))
            {
                state = false;
            }
            return true;
        }
    }

	public class Config
	{
		[JsonProperty(PropertyName = "No interference on player turrets")]
		public bool players { get; set; }

		[JsonProperty(PropertyName = "No interference on other turrets")]
		public bool other { get; set; } = true;

		public Config()
		{
			if (!Directory.Exists("HarmonyConfig"))
			{
				Directory.CreateDirectory("HarmonyConfig");
			}
		}

		public void ReloadConfig()
		{
			string path = Path.Combine("HarmonyConfig", "NoInterference.json");
			if (File.Exists(path))
			{
				try
				{
					config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					config = new Config();
					return;
				}
			}
			SaveConfig(path);
		}

		public void SaveConfig(string path)
		{
			if (config == null)
			{
				config = new Config();
			}
			File.WriteAllText(path, JsonConvert.SerializeObject((object)config, (Formatting)1));
		}
	}

	public const string Name = "NoInterference";

	public const string Author = "nivex";

	private static Config config;

	private static Version Version => Assembly.GetExecutingAssembly().GetName().Version;

	void IHarmonyModHooks.OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Debug.LogWarning((object)string.Format("[Harmony] Loaded: {0} {1} by {2}", "NoInterference", Version, "nivex"));
		if (config == null)
		{
			config = new Config();
		}
		config.ReloadConfig();
	}

	void IHarmonyModHooks.OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		Debug.LogWarning((object)string.Format("[Harmony] Unloaded: {0} {1} by {2}", "NoInterference", Version, "nivex"));
		config = null;
	}

	private static bool HandleInterference(AutoTurret __instance)
	{
		if (__instance == null || config == null)
		{
			return true;
		}
		return (__instance.OwnerID > 76561197960265728L) ? (!config.players) : (!config.other);
	}
}
