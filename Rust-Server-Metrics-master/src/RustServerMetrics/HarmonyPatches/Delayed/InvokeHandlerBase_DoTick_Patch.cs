using HarmonyLib;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace RustServerMetrics.HarmonyPatches.Delayed
{
    [DelayedHarmonyPatch]
    [HarmonyPatch]
    internal static class InvokeHandlerBase_DoTick_Patch
    {
        static System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();

        static readonly MethodInfo ActionInvoke = AccessTools.Method(typeof(Action), nameof(Action.Invoke));

        [HarmonyPrepare]
        public static bool Prepare()
        {
            if (!RustServerMetricsLoader.__serverStarted)
            {
                Debug.Log("Note: Cannot patch InvokeHandlerBase_DoTick_Patch yet. We will patch it upon server start.");
                return false;
            }

            return true;
        }

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(Harmony harmonyInstance)
        {
            yield return AccessTools.DeclaredMethod(typeof(InvokeHandlerBase<InvokeHandler>), nameof(InvokeHandlerBase<InvokeHandler>.DoTick));
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, MethodBase methodBase)
        {
            var body = methodBase.GetMethodBody();
            if (body == null)
                throw new Exception($"{nameof(InvokeHandlerBase_DoTick_Patch)}: no method body for {methodBase}");

            var invokeActionLocal = body.LocalVariables.FirstOrDefault(x => x.LocalType == typeof(InvokeAction));
            if (invokeActionLocal == null)
                throw new Exception($"{nameof(InvokeHandlerBase_DoTick_Patch)}: could not find a local of type {nameof(InvokeAction)}");

            var codes = new List<CodeInstruction>(originalInstructions);
            var invokeWrapper = typeof(InvokeHandlerBase_DoTick_Patch)
                .GetMethod(nameof(InvokeWrapper), BindingFlags.Static | BindingFlags.NonPublic);

            int idx = FindInvokeActionInvokeSequence(codes, invokeActionLocal);
            if (idx < 0)
                throw new Exception($"Failed to find Action.Invoke sequence for {nameof(InvokeHandlerBase_DoTick_Patch)} (game IL changed; update transpiler).");

            // Keep the load of InvokeAction, replace "ldfld action; callvirt Invoke" with our wrapper.
            codes.RemoveRange(idx + 1, 2);
            codes.Insert(idx + 1, new CodeInstruction(OpCodes.Call, invokeWrapper));
            return codes;
        }

        /// <summary>
        /// Finds "ldloc* (InvokeAction) / ldfld action / callvirt|call Action.Invoke" which is stable across DoTick refactors
        /// (older builds ended with br.s; current Facepunch.UnityEngine continues with profiler locals instead).
        /// </summary>
        static int FindInvokeActionInvokeSequence(List<CodeInstruction> codes, LocalVariableInfo invokeActionLocal)
        {
            int targetIndex = invokeActionLocal.LocalIndex;
            for (int i = 0; i < codes.Count - 2; i++)
            {
                if (!LoadsLocalSlot(codes[i], targetIndex))
                    continue;
                if (!IsLoadActionField(codes[i + 1]))
                    continue;
                if (!IsActionInvokeCall(codes[i + 2]))
                    continue;
                return i;
            }

            return -1;
        }

        static bool LoadsLocalSlot(CodeInstruction ins, int index)
        {
            if (ins.opcode == OpCodes.Ldloc_0) return index == 0;
            if (ins.opcode == OpCodes.Ldloc_1) return index == 1;
            if (ins.opcode == OpCodes.Ldloc_2) return index == 2;
            if (ins.opcode == OpCodes.Ldloc_3) return index == 3;
            if (ins.opcode == OpCodes.Ldloc_S || ins.opcode == OpCodes.Ldloc)
            {
                // LocalBuilder subclasses LocalVariableInfo — test the derived type first.
                if (ins.operand is LocalBuilder lb)
                    return lb.LocalIndex == index;
                if (ins.operand is LocalVariableInfo lvi)
                    return lvi.LocalIndex == index;
            }

            return false;
        }

        static bool IsLoadActionField(CodeInstruction ins)
        {
            if (ins.opcode != OpCodes.Ldfld || ins.operand is not FieldInfo f)
                return false;
            if (f.Name != nameof(InvokeAction.action))
                return false;
            return f.DeclaringType == typeof(InvokeAction)
                || (f.DeclaringType?.FullName == typeof(InvokeAction).FullName);
        }

        static bool IsActionInvokeCall(CodeInstruction ins)
        {
            if (ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Call)
                return false;
            if (ins.operand is not MethodInfo mi)
                return false;
            return mi == ActionInvoke
                || (mi.Name == nameof(Action.Invoke) && mi.DeclaringType == typeof(Action));
        }

        static void InvokeWrapper(InvokeAction invokeAction)
        {
            _stopwatch.Restart();
            try
            {
                invokeAction.action.Invoke();
            }
            finally
            {
                _stopwatch.Stop();
                MetricsLogger.Instance?.ServerInvokes.LogTime(invokeAction.action.Method, _stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }
}
