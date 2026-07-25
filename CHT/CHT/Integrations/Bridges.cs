using System;
using System.Reflection;
using UnityEngine;

namespace CHT
{
    public static class EconomicsBridge
    {
        private const string ApiKey = "Economics_ApiType";

        private static Type ApiType => AppDomain.CurrentDomain.GetData(ApiKey) as Type;

        public static bool IsAvailable
        {
            get
            {
                try
                {
                    var t = ApiType;
                    if (t == null) return false;
                    return t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) != null;
                }
                catch { return false; }
            }
        }

        private static object Call(string method, params object[] args)
        {
            try
            {
                Type t = ApiType;
                if (t == null) return null;
                object instance = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return null;

                MethodInfo call = instance.GetType().GetMethod("Call", new[] { typeof(string), typeof(object[]) });
                if (call != null)
                    return call.Invoke(instance, new object[] { method, args });

                MethodInfo staticMi = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                if (staticMi != null)
                    return staticMi.Invoke(null, args);

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CHT] Economics." + method + " failed: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        public static int Balance(BasePlayer player)
        {
            if (player == null) return 0;
            object r = Call("Balance", player.UserIDString);
            return r == null ? 0 : Convert.ToInt32(r);
        }

        public static void Deposit(BasePlayer player, int amount)
        {
            if (player == null || amount <= 0) return;
            Call("Deposit", player.UserIDString, (double)amount);
        }

        public static void Withdraw(BasePlayer player, int amount)
        {
            if (player == null || amount <= 0) return;
            Call("Withdraw", player.UserIDString, (double)amount);
        }
    }

    public static class AlphaLootBridge
    {
        private static object Instance
        {
            get
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Type t = asm.GetType("AlphaLoot.AlphaLootMod");
                        object v = t?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (v != null) return v;
                    }
                    catch { }
                }
                return null;
            }
        }

        public static bool ProfileExists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            object instance = Instance;
            if (instance == null) return false;
            try
            {
                MethodInfo mi = instance.GetType().GetMethod("TryGetLootProfile", BindingFlags.Public | BindingFlags.Instance);
                if (mi == null) return false;
                object[] args = { name, null };
                return mi.Invoke(instance, args) is bool ok && ok && args[1] != null;
            }
            catch { return false; }
        }

        public static bool PopulateLoot(LootContainer container, string name)
        {
            if (container == null || string.IsNullOrEmpty(name)) return false;
            object instance = Instance;
            if (instance == null) return false;
            try
            {
                MethodInfo tryGet = instance.GetType().GetMethod("TryGetLootProfile", BindingFlags.Public | BindingFlags.Instance);
                if (tryGet == null) return false;
                object[] args = { name, null };
                if (!(tryGet.Invoke(instance, args) is bool ok) || !ok || args[1] == null)
                    return false;

                MethodInfo populate = instance.GetType().GetMethod("PopulateLootContainer", BindingFlags.Public | BindingFlags.Instance);
                if (populate != null)
                {
                    populate.Invoke(instance, new[] { container, args[1] });
                    return true;
                }

                MethodInfo profilePopulate = args[1].GetType().GetMethod("PopulateLoot", new[] { typeof(ItemContainer) });
                if (profilePopulate != null && container.inventory != null)
                {
                    profilePopulate.Invoke(args[1], new object[] { container.inventory });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CHT] AlphaLoot populate failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
            return false;
        }
    }

    public static class SkillTreeBridge
    {
        public static void AwardXP(ulong userId, double xp, string plugin, bool noMod)
        {
            if (userId == 0 || xp <= 0) return;
            try
            {
                Type modType = AppDomain.CurrentDomain.GetData("SkillTree_ApiType") as Type;
                object pluginInstance = modType?.GetProperty("Plugin", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (pluginInstance == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = asm.GetType("SkillTree.SkillTreeMod") ?? asm.GetType("Oxide.Plugins.SkillTree");
                        if (t == null) continue;
                        pluginInstance = t.GetProperty("Plugin", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                            ?? t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (pluginInstance != null) break;
                    }
                }
                if (pluginInstance == null) return;

                MethodInfo mi = pluginInstance.GetType().GetMethod("AwardXP", new[] { typeof(ulong), typeof(double), typeof(string), typeof(bool) });
                if (mi != null)
                {
                    mi.Invoke(pluginInstance, new object[] { userId, xp, plugin, noMod });
                    return;
                }

                mi = pluginInstance.GetType().GetMethod("AwardXP", BindingFlags.Public | BindingFlags.Instance);
                mi?.Invoke(pluginInstance, new object[] { userId, xp, plugin, noMod });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CHT] SkillTree AwardXP failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }
    }
}
