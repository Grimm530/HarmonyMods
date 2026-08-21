// Misc hooks: OnMlrsFire, OnCupboardAuthorize, OnTimedExplosiveExplode, OnCodeEntered,
// CanChangeGrade, OnEntityMarkHostile, supply-signal throw / cargo-plane signal.
using HarmonyLib;
using UnityEngine;
using TPVE = Oxide.Plugins.TruePVE;

namespace TruePVEHarmony.Patches
{
    /// <summary>OnMlrsFire - non-null blocks the MLRS from firing.</summary>
    [HarmonyPatch(typeof(MLRS), "Fire")]
    public static class Patch_MLRS_Fire
    {
        [HarmonyPrefix]
        public static bool Prefix(MLRS __instance, BasePlayer owner)
        {
            if (__instance == null) return true;
            try { return TPVE.Dispatch_OnMlrsFire(__instance, owner) == null; }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnMlrsFire: " + ex.Message); return true; }
        }
    }

    /// <summary>OnCupboardAuthorize - non-null blocks the authorization.</summary>
    [HarmonyPatch(typeof(BuildingPrivlidge), nameof(BuildingPrivlidge.AddPlayer))]
    public static class Patch_BuildingPrivlidge_AddPlayer
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingPrivlidge __instance, BasePlayer granter)
        {
            if (__instance == null || granter == null) return true;
            try { return TPVE.Dispatch_OnCupboardAuthorize(__instance, granter) == null; }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnCupboardAuthorize: " + ex.Message); return true; }
        }
    }

    /// <summary>OnTimedExplosiveExplode - notification (wallpaper / C4 handling).</summary>
    [HarmonyPatch(typeof(TimedExplosive), "Explode", new[] { typeof(Vector3) })]
    public static class Patch_TimedExplosive_Explode
    {
        [HarmonyPostfix]
        public static void Postfix(TimedExplosive __instance)
        {
            if (__instance == null) return;
            try { TPVE.Dispatch_OnTimedExplosiveExplode(__instance, __instance.transform.position); }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnTimedExplosiveExplode: " + ex.Message); }
        }
    }

    /// <summary>OnCodeEntered - codelock anti-grief. Do not read rpc.read (consumes the code string).</summary>
    [HarmonyPatch(typeof(CodeLock), "UnlockWithCode", new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_CodeLock_UnlockWithCode
    {
        [HarmonyPrefix]
        public static bool Prefix(CodeLock __instance, BaseEntity.RPCMessage rpc)
        {
            if (__instance == null || rpc.player == null) return true;
            try
            {
                // TruePVE OnCodeEntered does not use the code string (ally/owner check only).
                return TPVE.Dispatch_OnCodeEntered(__instance, rpc.player, string.Empty) == null;
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnCodeEntered: " + ex.Message); return true; }
        }
    }

    /// <summary>CanChangeGrade via player upgrade RPC. true/null allow; false blocks.</summary>
    [HarmonyPatch(typeof(BuildingBlock), "DoUpgradeToGrade", new[] { typeof(BaseEntity.RPCMessage) })]
    public static class Patch_BuildingBlock_DoUpgradeToGrade
    {
        [HarmonyPrefix]
        public static bool Prefix(BuildingBlock __instance, BaseEntity.RPCMessage msg)
        {
            if (__instance == null || msg.player == null) return true;
            try
            {
                object result = TPVE.Dispatch_CanChangeGrade(msg.player, __instance, __instance.grade, __instance.skinID);
                if (result is bool allow) return allow;
                return true;
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] CanChangeGrade: " + ex.Message); return true; }
        }
    }

    /// <summary>OnEntityMarkHostile - Prevent Players From Being Marked Hostile.</summary>
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.MarkHostileFor))]
    public static class Patch_BasePlayer_MarkHostileFor
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer __instance, float duration)
        {
            if (__instance == null) return true;
            try { return TPVE.Dispatch_OnEntityMarkHostile(__instance, duration) == null; }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnEntityMarkHostile: " + ex.Message); return true; }
        }
    }

    /// <summary>OnExplosiveThrown(SupplySignal) - supply drop lock / bypass cargo plane.</summary>
    [HarmonyPatch(typeof(ThrownWeapon), nameof(ThrownWeapon.DoThrowImpl))]
    public static class Patch_ThrownWeapon_DoThrowImpl
    {
        [HarmonyPostfix]
        public static void Postfix(ThrownWeapon __instance, BasePlayer owningPlayer, BaseEntity thrownEntity)
        {
            if (__instance == null || owningPlayer == null || thrownEntity == null) return;
            try
            {
                if (thrownEntity is SupplySignal ss)
                    TPVE.Dispatch_OnExplosiveThrown(owningPlayer, ss, __instance);
                else if (thrownEntity is TimedExplosive te)
                    TPVE.Dispatch_OnExplosiveDroppedTimed(owningPlayer, te, __instance);
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnExplosiveThrown: " + ex.Message); }
        }
    }

    /// <summary>OnCargoPlaneSignaled - when a supply signal creates a cargo plane.</summary>
    [HarmonyPatch(typeof(SupplySignal), nameof(SupplySignal.Explode))]
    public static class Patch_SupplySignal_Explode
    {
        [HarmonyPostfix]
        public static void Postfix(SupplySignal __instance)
        {
            if (__instance == null) return;
            try
            {
                // Plane is spawned in Explode at signal position +/- offset; find nearest fresh CargoPlane.
                CargoPlane plane = null;
                float best = float.MaxValue;
                Vector3 origin = __instance.transform.position;
                foreach (var ent in BaseNetworkable.serverEntities)
                {
                    if (!(ent is CargoPlane cp) || cp.IsDestroyed) continue;
                    float d = (cp.transform.position - origin).sqrMagnitude;
                    if (d < best && d < 2500f) // ~50m
                    {
                        best = d;
                        plane = cp;
                    }
                }
                if (plane != null)
                    TPVE.Dispatch_OnCargoPlaneSignaled(plane, __instance);
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnCargoPlaneSignaled: " + ex.Message); }
        }
    }

    /// <summary>
    /// CanAffordApartmentMasterKey — Oxide ReturnBehavior 1 on Conversation_CanAffordMasterKey.
    /// Non-null (false) skips the original and reports the player cannot afford a key.
    /// </summary>
    [HarmonyPatch(typeof(NPCApartmentSecurity), nameof(NPCApartmentSecurity.Conversation_CanAffordMasterKey))]
    public static class Patch_NPCApartmentSecurity_CanAffordMasterKey
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer player, ref bool __result)
        {
            if (player == null) return true;
            try
            {
                object result = TPVE.Dispatch_CanAffordApartmentMasterKey(player);
                if (result == null) return true;
                __result = false;
                return false;
            }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] CanAffordApartmentMasterKey: " + ex.Message); return true; }
        }
    }

    /// <summary>
    /// OnApartmentMasterKeyPurchase — Oxide ReturnBehavior 1 on OnPurchaseKey (static).
    /// Non-null skips the purchase (basement / PaidKey path after the 2.4.3 Rust update).
    /// </summary>
    [HarmonyPatch(typeof(NPCApartmentSecurity), nameof(NPCApartmentSecurity.OnPurchaseKey))]
    public static class Patch_NPCApartmentSecurity_OnPurchaseKey
    {
        [HarmonyPrefix]
        public static bool Prefix(BasePlayer player)
        {
            if (player == null) return true;
            try { return TPVE.Dispatch_OnApartmentMasterKeyPurchase(player) == null; }
            catch (System.Exception ex) { Debug.LogWarning("[TruePVE] OnApartmentMasterKeyPurchase: " + ex.Message); return true; }
        }
    }
}
