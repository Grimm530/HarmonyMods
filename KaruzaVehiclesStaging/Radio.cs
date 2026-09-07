using System;
using System.Reflection;
using UnityEngine;

namespace KaruzaVehicles
{
    /// <summary>
    /// Radio API used by Karuza vehicle controllers. Full Radio + VehicleRadio
    /// live in a separate Harmony mod; this forwards Register/Remove when that
    /// mod is loaded, otherwise no-ops so vehicles still compile and drive.
    /// </summary>
    public static class Radio
    {
        public interface IRadio
        {
            bool CanTransmitRadio();
            bool CanReceiveRadioCommunication(BasePlayer player, IRadio transmittingRadio);
            void ReceiveRadioCommunication(byte[] data);
            int GetRadioFrequency();
        }

        public static void RegisterRadio(BasePlayer player, IRadio radio)
        {
            if (player == null || player.IsNpc || player.IsBot || radio == null) return;
            TryInvoke("RegisterRadio", player, radio);
        }

        public static void RemoveRadio(BasePlayer player)
        {
            if (player == null || player.IsNpc || player.IsBot) return;
            TryInvoke("RemoveRadio", player);
        }

        private static void TryInvoke(string method, params object[] args)
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetData("Radio_ApiType") as Type;
                if (type == null) return;
                var types = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    types[i] = args[i]?.GetType() ?? typeof(object);
                var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, types, null);
                if (mi == null && args.Length == 2 && args[0] is BasePlayer)
                    mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BasePlayer), typeof(object) }, null);
                if (mi == null)
                    mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                mi?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KaruzaVehicles] Radio." + method + ": " + ex.Message);
            }
        }
    }
}
