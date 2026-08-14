using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using CookingPlugin = Oxide.Plugins.Cooking;

namespace CookingHarmony.Patches
{
    [HarmonyPatch(typeof(ResourceDispenser), "GiveResourceFromItem")]
    public static class ResourceDispenser_GiveResourceFromItem_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                AccessTools.Method(typeof(ResourceDispenser_GiveResourceFromItem_Patch), nameof(Hook)),
                includePlayerArg: true);
        }

        public static void Hook(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (dispenser == null || player == null || item == null) return;
            try { CookingPlugin.Dispatch_OnDispenserGather(dispenser, player, item); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnDispenserGather: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResourceDispenser), nameof(ResourceDispenser.AssignFinishBonus))]
    public static class ResourceDispenser_AssignFinishBonus_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                AccessTools.Method(typeof(ResourceDispenser_AssignFinishBonus_Patch), nameof(Hook)),
                includePlayerArg: true,
                injectAllCreates: true);
        }

        public static void Hook(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (dispenser == null || player == null || item == null) return;
            try { CookingPlugin.Dispatch_OnDispenserBonus(dispenser, player, item); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnDispenserBonus: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(CollectibleEntity), nameof(CollectibleEntity.DoPickup), typeof(BasePlayer), typeof(bool))]
    public static class CollectibleEntity_DoPickup_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(CollectibleEntity __instance, BasePlayer reciever, bool eat)
        {
            try { CookingPlugin.Dispatch_OnCollectiblePickup(__instance, reciever, eat); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnCollectiblePickup: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(GrowableEntity), "GiveFruit", typeof(BasePlayer), typeof(int), typeof(bool), typeof(bool))]
    public static class GrowableEntity_GiveFruit_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            return YieldHookInjector.InjectAfterItemCreate(
                instructions,
                originalMethod,
                AccessTools.Method(typeof(GrowableEntity_GiveFruit_Patch), nameof(Hook)),
                includePlayerArg: true);
        }

        public static void Hook(GrowableEntity plant, BasePlayer player, Item item)
        {
            if (plant == null || player == null || item == null) return;
            try { CookingPlugin.Dispatch_OnGrowableGathered(plant, item, player); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnGrowableGathered: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseDiggableEntity), nameof(BaseDiggableEntity.Dig))]
    public static class BaseDiggableEntity_Dig_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseDiggableEntity __instance, BasePlayer player)
        {
            try { CookingPlugin.Dispatch_OnPlayerDig(player, __instance); }
            catch (System.Exception ex) { Debug.LogWarning("[Cooking] OnPlayerDig: " + ex.Message); }
        }
    }

    internal static class YieldHookInjector
    {
        public static IEnumerable<CodeInstruction> InjectAfterItemCreate(
            IEnumerable<CodeInstruction> instructions,
            MethodBase originalMethod,
            MethodInfo hookMethod,
            bool includePlayerArg,
            bool injectAllCreates = false)
        {
            var list = new List<CodeInstruction>(instructions);
            if (hookMethod == null) return list;

            var locals = originalMethod.GetMethodBody()?.LocalVariables;
            if (locals == null) return list;

            int itemLocalIndex = -1;
            for (int i = 0; i < locals.Count; i++)
            {
                if (locals[i].LocalType == typeof(Item))
                {
                    itemLocalIndex = locals[i].LocalIndex;
                    break;
                }
            }
            if (itemLocalIndex < 0) return list;

            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
            };
            if (includePlayerArg)
                toInject.Add(new CodeInstruction(OpCodes.Ldarg_1));
            toInject.Add(GetLdloc(itemLocalIndex));
            toInject.Add(new CodeInstruction(OpCodes.Call, hookMethod));

            var insertPoints = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var instr = list[i];
                if ((instr.opcode != OpCodes.Call && instr.opcode != OpCodes.Callvirt) || instr.operand is not MethodInfo method)
                    continue;
                if (method.DeclaringType != typeof(ItemManager))
                    continue;

                bool isCreate = method.Name == "CreateByItemID"
                    || (method.Name == "Create" && method.GetParameters().Length >= 2);
                if (!isCreate) continue;

                int insertAt = i + 1;
                while (insertAt < list.Count)
                {
                    var next = list[insertAt];
                    if (next.opcode == OpCodes.Stloc || next.opcode == OpCodes.Stloc_S || next.opcode == OpCodes.Stloc_0 ||
                        next.opcode == OpCodes.Stloc_1 || next.opcode == OpCodes.Stloc_2 || next.opcode == OpCodes.Stloc_3)
                    {
                        insertAt++;
                        break;
                    }
                    insertAt++;
                }
                insertPoints.Add(insertAt);
                if (!injectAllCreates) break;
            }

            for (int p = insertPoints.Count - 1; p >= 0; p--)
                list.InsertRange(insertPoints[p], toInject);

            return list;
        }

        static CodeInstruction GetLdloc(int index) => index switch
        {
            0 => new CodeInstruction(OpCodes.Ldloc_0),
            1 => new CodeInstruction(OpCodes.Ldloc_1),
            2 => new CodeInstruction(OpCodes.Ldloc_2),
            3 => new CodeInstruction(OpCodes.Ldloc_3),
            _ => new CodeInstruction(OpCodes.Ldloc_S, index)
        };
    }
}
