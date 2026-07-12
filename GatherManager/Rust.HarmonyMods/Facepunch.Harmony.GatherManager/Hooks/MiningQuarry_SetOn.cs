using System;
using HarmonyLib;
using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    [HarmonyPatch(typeof(MiningQuarry), "SetOn")]
    internal class MiningQuarry_SetOn
    {
        [HarmonyPostfix]
        static void Postfix(MiningQuarry __instance, bool isOn)
        {
            try
            {
                if (!isOn || GatherManagerMod.Instance == null) return;
                var tickRate = GatherManagerMod.Instance.GetMiningQuarryTickRate();
                if (tickRate <= 0 || Math.Abs(tickRate - 5f) < 0.01f) return;
                __instance.CancelInvoke("ProcessResources");
                __instance.InvokeRepeating("ProcessResources", tickRate, tickRate);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
