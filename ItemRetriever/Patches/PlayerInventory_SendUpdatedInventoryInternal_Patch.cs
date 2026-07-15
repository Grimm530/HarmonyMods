using HarmonyLib;
using ProtoBuf;

namespace ItemRetrieverHarmony
{
    /// <summary>
    /// Oxide OnInventoryNetworkUpdate — reimplements SendUpdatedInventoryInternal with the hook after Save.
    /// Returning false skips vanilla so we can mutate UpdateItemContainer before ClientRPC.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.SendUpdatedInventoryInternal))]
    [HarmonyPriority(Priority.First)]
    internal static class PlayerInventory_SendUpdatedInventoryInternal_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerInventory __instance, PlayerInventory.Type type, ItemContainer container,
            PlayerInventory.NetworkInventoryMode mode)
        {
            var plugin = ItemRetrieverHost.Instance?.Plugin;
            if (plugin == null)
                return true;

            try
            {
                using (UpdateItemContainer updateItemContainer = Facepunch.Pool.Get<UpdateItemContainer>())
                {
                    updateItemContainer.type = (int)type;
                    var networkMode = mode;
                    if (__instance.baseEntity != null && __instance.baseEntity.IsSpectating())
                        networkMode = PlayerInventory.NetworkInventoryMode.LocalPlayer;

                    if (container != null)
                    {
                        container.dirty = false;
                        updateItemContainer.container = Facepunch.Pool.Get<System.Collections.Generic.List<ProtoBuf.ItemContainer>>();
                        bool bIncludeContainer = type != PlayerInventory.Type.Wear
                            || networkMode == PlayerInventory.NetworkInventoryMode.LocalPlayer;
                        updateItemContainer.container.Add(container.Save(bIncludeContainer));
                    }

                    plugin.OnInventoryNetworkUpdate(__instance, container, updateItemContainer, type);

                    switch (networkMode)
                    {
                        case PlayerInventory.NetworkInventoryMode.Everyone:
                            __instance.baseEntity.ClientRPC(RpcTarget.NetworkGroup("UpdatedItemContainer"), updateItemContainer);
                            break;
                        case PlayerInventory.NetworkInventoryMode.LocalPlayer:
                            __instance.baseEntity.ClientRPC(RpcTarget.Player("UpdatedItemContainer", __instance.baseEntity), updateItemContainer);
                            break;
                        case PlayerInventory.NetworkInventoryMode.EveryoneButLocal:
                            if (__instance.baseEntity.net?.group?.subscribers == null)
                                break;
                            foreach (var subscriber in __instance.baseEntity.net.group.subscribers)
                            {
                                if (subscriber.player is BasePlayer basePlayer && basePlayer != __instance.baseEntity)
                                    __instance.baseEntity.ClientRPC(RpcTarget.Player("UpdatedItemContainer", basePlayer), updateItemContainer);
                            }
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ItemRetriever] OnInventoryNetworkUpdate: " + ex.Message);
                return true;
            }

            return false;
        }
    }
}
