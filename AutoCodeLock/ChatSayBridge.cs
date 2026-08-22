using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyChat
{
    /// <summary>
    /// Shared chat.say command dispatcher. Handler list lives in AppDomain so Shop, SkillTree,
    /// BetterChat, and other mods all see the same registrations across renamed assemblies.
    /// </summary>
    public static class ChatSayBridge
    {
        public const string AppDomainHandlersKey = "HarmonyMods_ChatSayHandlers";

        public static void Register(string id, Func<BasePlayer, string, bool> handler)
        {
            if (string.IsNullOrEmpty(id) || handler == null) return;
            var list = GetOrCreateList();
            lock (list)
            {
                RemoveIdUnlocked(list, id);
                list.Add(new object[] { id, handler });
            }
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var list = AppDomain.CurrentDomain.GetData(AppDomainHandlersKey) as IList;
            if (list == null) return;
            lock (list)
            {
                RemoveIdUnlocked(list, id);
            }
        }

        public static bool Dispatch(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrWhiteSpace(message)) return false;

            var list = AppDomain.CurrentDomain.GetData(AppDomainHandlersKey) as IList;
            if (list == null || list.Count == 0) return false;

            object[] snapshot;
            lock (list)
            {
                snapshot = new object[list.Count];
                list.CopyTo(snapshot, 0);
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i] as object[];
                if (entry == null || entry.Length < 2) continue;
                var id = entry[0] as string ?? "?";
                var handler = entry[1] as Func<BasePlayer, string, bool>;
                if (handler == null) continue;
                try
                {
                    if (handler(player, message))
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[HarmonyChat] ChatSayBridge handler '" + id + "': " + ex.Message);
                }
            }

            return false;
        }

        private static IList GetOrCreateList()
        {
            var list = AppDomain.CurrentDomain.GetData(AppDomainHandlersKey) as IList;
            if (list != null) return list;
            list = new List<object>();
            AppDomain.CurrentDomain.SetData(AppDomainHandlersKey, list);
            return list;
        }

        private static void RemoveIdUnlocked(IList list, string id)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i] as object[];
                if (entry == null || entry.Length < 1) continue;
                if (string.Equals(entry[0] as string, id, StringComparison.OrdinalIgnoreCase))
                    list.RemoveAt(i);
            }
        }
    }
}
