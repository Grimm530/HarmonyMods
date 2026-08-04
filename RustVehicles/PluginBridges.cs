using System;
using System.Reflection;
using UnityEngine;

namespace RustVehiclesHarmony
{
    /// <summary>
    /// Resolves optional Oxide PluginReference fields against Harmony ports exposed via AppDomain.
    /// Missing deps resolve to null so existing null-checks behave like Oxide soft references.
    /// </summary>
    public static class PluginBridges
    {
        private sealed class AppDomainCallBridge : Plugin
        {
            private readonly object _wrapper;
            private readonly MethodInfo _call;

            public AppDomainCallBridge(object wrapper, MethodInfo call, string name)
            {
                _wrapper = wrapper;
                _call = call;
                Name = name;
                IsLoaded = true;
            }

            public override object Call(string method, params object[] args)
            {
                if (_wrapper == null || _call == null) return null;
                try
                {
                    return _call.Invoke(_wrapper, new object[] { method, args ?? Array.Empty<object>() });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RustVehicles] Bridge {Name}.Call({method}): " + (ex.InnerException?.Message ?? ex.Message));
                    return null;
                }
            }
        }

        private static AppDomainCallBridge Wrap(string appDomainKey, string name)
        {
            object wrapper;
            try { wrapper = AppDomain.CurrentDomain.GetData(appDomainKey); }
            catch { wrapper = null; }
            if (wrapper == null) return null;

            var call = wrapper.GetType().GetMethod("Call",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(string), typeof(object[]) }, null);
            if (call == null) return null;

            return new AppDomainCallBridge(wrapper, call, name);
        }

        public static Plugin Economics => Wrap("Economics_Plugin", "Economics");
        public static Plugin ServerRewards => Wrap("RustRewards_Plugin", "ServerRewards");

        public static Plugin Friends => null;
        public static Plugin Clans => null;
        public static Plugin NoEscape => null;
        public static Plugin LandOnCargoShip => null;
        public static Plugin RustTranslationAPI => null;
        public static Plugin ZoneManager => null;
        public static Plugin CustomEntities => null;
        public static Plugin RustCar => null;
        public static Plugin RustPlane => null;
        public static Plugin RustHelicopter => null;
        public static Plugin KaruzaVehicleChatCommand => null;

        public static Plugin Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name.ToLowerInvariant())
            {
                case "economics": return Economics;
                case "serverrewards": return ServerRewards;
                case "friends": return Friends;
                case "clans": return Clans;
                case "noescape": return NoEscape;
                case "landoncargoship": return LandOnCargoShip;
                case "rusttranslationapi": return RustTranslationAPI;
                case "zonemanager": return ZoneManager;
                case "customentities": return CustomEntities;
                case "rustcar": return RustCar;
                case "rustplane": return RustPlane;
                case "rusthelicopter": return RustHelicopter;
                case "karuzavehiclechatcommand": return KaruzaVehicleChatCommand;
                default: return null;
            }
        }

        /// <summary>Assign all soft plugin references onto a live RustVehicles instance.</summary>
        public static void Wire(RustVehicles plugin)
        {
            if (plugin == null) return;
            plugin.Economics = Economics;
            plugin.ServerRewards = ServerRewards;
            plugin.Friends = Friends;
            plugin.Clans = Clans;
            plugin.NoEscape = NoEscape;
            plugin.LandOnCargoShip = LandOnCargoShip;
            plugin.RustTranslationAPI = RustTranslationAPI;
            plugin.ZoneManager = ZoneManager;
            plugin.CustomEntities = CustomEntities;
            plugin.RustCar = RustCar;
            plugin.RustPlane = RustPlane;
            plugin.RustHelicopter = RustHelicopter;
            plugin.KaruzaVehicleChatCommand = KaruzaVehicleChatCommand;
        }
    }
}
