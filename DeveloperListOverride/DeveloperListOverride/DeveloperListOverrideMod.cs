using Network;
using UnityEngine;

namespace DeveloperListOverride
{
    public class DeveloperListOverrideMod : IHarmonyModHooks
    {
        public static DeveloperListOverrideMod Instance { get; private set; }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            DeveloperListOverrideConfig.LoadConfig();
            ApplyToConnectedPlayers();
            UnityEngine.Debug.Log("[DeveloperListOverride] Loaded. Add Steam IDs to HarmonyConfig/DeveloperListOverride.json to grant developer (orange name + tools).");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Instance = null;
        }

        /// <summary>
        /// Make the vanilla developer gates true for this player: IsDeveloper flag,
        /// auth level 3, and client.skins_access so GetSkinsAccessLevel returns 1.
        /// Chat color can work from its own patch even when this was never applied.
        /// </summary>
        public static void ApplyDeveloperPrivileges(BasePlayer player)
        {
            if (player == null || player.IsNpc) return;
            if (!DeveloperListOverrideConfig.IsOverrideDeveloper(player.UserIDString)) return;

            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsDeveloper, true);

            if (player.net?.connection != null)
            {
                player.net.connection.authLevel = 3u;
                player.SetInfo("client.skins_access", "1");
            }

            player.SendNetworkUpdateImmediate();

            if (player.IsConnected && player.net?.connection != null)
                ConsoleNetwork.SendClientCommandImmediate(player.net.connection, "client.skins_access", "1");
        }

        private static void ApplyToConnectedPlayers()
        {
            var list = BasePlayer.activePlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                ApplyDeveloperPrivileges(list[i]);
        }
    }
}
