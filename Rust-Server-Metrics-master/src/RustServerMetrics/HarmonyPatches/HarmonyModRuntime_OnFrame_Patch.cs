using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch]
    public static class HarmonyModRuntime_OnFrame_Patch
    {
        const string HarmonyCore_AssemblyName = "Harmony.Core";

        const string HarmonyPluginType_FullName = "Harmony.Core.Plugins.Plugin";
        const string HarmonyInterfaceType_FullName = "Harmony.Core.HarmonyModInterface";
        const string HarmonyModRuntimeType_FullName = "Harmony.Core.HarmonyModRuntime";
        const string HarmonyModRuntimeManagerType_FullName = "Harmony.Core.Plugins.ModManager";

        static Assembly _harmonyCoreAssembly = null;
        public static float nextTick = 0f;

        [HarmonyPrepare]
        public static bool Prepare()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (!string.Equals(assembly.GetName().Name, HarmonyCore_AssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                _harmonyCoreAssembly = assembly;
                
                break;
            }

            if (_harmonyCoreAssembly == null)
            {
                return false;
            }

            return true;
        }
        
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(Harmony harmonyInstance)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (!string.Equals(assembly.GetName().Name, HarmonyCore_AssemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                _harmonyCoreAssembly = assembly;
                break;
            }

            if (_harmonyCoreAssembly == null)
                return Array.Empty<MethodBase>();

            var harmonyModRuntimeType = _harmonyCoreAssembly.GetType(HarmonyModRuntimeType_FullName, false);

            if (harmonyModRuntimeType == null)
                return Array.Empty<MethodBase>();

            var targetMethod = harmonyModRuntimeType.GetMethod("OnFrame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(float) }, null);

            if (targetMethod == null)
                return Array.Empty<MethodBase>();

            return new MethodBase[] { targetMethod };
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, ILGenerator ilGenerator)
        {
            var skipProcessingLabel = ilGenerator.DefineLabel();
            var loopHeadLabel = ilGenerator.DefineLabel();
            var loopBodyLabel = ilGenerator.DefineLabel();

            var harmonyPluginType = _harmonyCoreAssembly.GetType(HarmonyPluginType_FullName, true);
            var enumerableType = typeof(IEnumerable<>);
            var harmonyPluginEnumerableType = enumerableType.MakeGenericType(harmonyPluginType);
            var enumeratorType = typeof(IEnumerator<>);
            var harmonyPluginEnumeratorType = enumeratorType.MakeGenericType(harmonyPluginType);

            var enumeratorLocal = ilGenerator.DeclareLocal(harmonyPluginEnumeratorType);
            var dictionaryLocal = ilGenerator.DeclareLocal(typeof(Dictionary<string, double>));
            var pluginLocal = ilGenerator.DeclareLocal(harmonyPluginType);
            var currentTimeLocal = ilGenerator.DeclareLocal(typeof(float));

            var nextTickFieldInfo = typeof(HarmonyModRuntime_OnFrame_Patch).GetField(nameof(nextTick), BindingFlags.Public | BindingFlags.Static);

            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            retList[0].labels.Add(skipProcessingLabel);

            var unityGetTimeMethodInfo = typeof(UnityEngine.Time).GetProperty(nameof(UnityEngine.Time.time)).GetGetMethod();
            var harmonyModGetterMethodInfo = _harmonyCoreAssembly.GetType(HarmonyInterfaceType_FullName, true).GetProperty("Mods").GetGetMethod();
            var rootModManagerGetterMethodInfo = _harmonyCoreAssembly.GetType(HarmonyModRuntimeType_FullName, true).GetProperty("RootModManager").GetGetMethod();
            var getPluginsMethodInfo = _harmonyCoreAssembly.GetType(HarmonyModRuntimeManagerType_FullName, true).GetMethod("GetPlugins");
            var getPluginNameGetterMethodInfo = harmonyPluginType.GetProperty("Name").GetGetMethod();
            var getPluginTotalHookTimeGetterMethodInfo = harmonyPluginType.GetProperty("TotalHookTime").GetGetMethod();
            var getEnumeratorMethodInfo = harmonyPluginEnumerableType.GetMethod("GetEnumerator");

            var hookFieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), BindingFlags.Static | BindingFlags.Public);

            var hookMethodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnHarmonyPluginMetrics), BindingFlags.Instance | BindingFlags.NonPublic);

            retList.InsertRange(0, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldsfld, hookFieldInfo),
                new CodeInstruction(OpCodes.Brfalse_S, skipProcessingLabel),

                new CodeInstruction(OpCodes.Call, unityGetTimeMethodInfo),
                new CodeInstruction(OpCodes.Stloc, currentTimeLocal.LocalIndex),

                new CodeInstruction(OpCodes.Ldsfld, nextTickFieldInfo),
                new CodeInstruction(OpCodes.Ldloc, currentTimeLocal.LocalIndex),
                new CodeInstruction(OpCodes.Bgt_S, skipProcessingLabel),

                new CodeInstruction(OpCodes.Ldloc, currentTimeLocal.LocalIndex),
                new CodeInstruction(OpCodes.Ldc_R4, 1f),
                new CodeInstruction(OpCodes.Add),
                new CodeInstruction(OpCodes.Stsfld, nextTickFieldInfo),

                new CodeInstruction(OpCodes.Call, harmonyModGetterMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, rootModManagerGetterMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, getPluginsMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, getEnumeratorMethodInfo),
                new CodeInstruction(OpCodes.Stloc, enumeratorLocal.LocalIndex),

                new CodeInstruction(OpCodes.Newobj, typeof(Dictionary<string, double>).GetConstructor(Type.EmptyTypes)),
                new CodeInstruction(OpCodes.Stloc, dictionaryLocal.LocalIndex),

                // Jump to Loop Head
                new CodeInstruction(OpCodes.Br_S, loopHeadLabel),

                // Loop Body Start
                new CodeInstruction(OpCodes.Ldloc, enumeratorLocal.LocalIndex) { labels = { loopBodyLabel } },
                new CodeInstruction(OpCodes.Callvirt, harmonyPluginEnumeratorType.GetProperty(nameof(IEnumerator.Current)).GetGetMethod()),
                new CodeInstruction(OpCodes.Stloc, pluginLocal.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc, dictionaryLocal.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc, pluginLocal.LocalIndex),
                new CodeInstruction(OpCodes.Callvirt, getPluginNameGetterMethodInfo),
                new CodeInstruction(OpCodes.Ldloc, pluginLocal.LocalIndex),
                new CodeInstruction(OpCodes.Callvirt, getPluginTotalHookTimeGetterMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, dictionaryLocal.LocalType.GetMethod("set_Item")),
                // Loop Body End

                // Loop Head Start
                new CodeInstruction(OpCodes.Ldloc, enumeratorLocal.LocalIndex) { labels = { loopHeadLabel } },
                new CodeInstruction(OpCodes.Callvirt, typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext))),
                new CodeInstruction(OpCodes.Brtrue_S, loopBodyLabel),
                // Loop Head End
                
                // Dispose of IEnumerator
                new CodeInstruction(OpCodes.Ldloc, enumeratorLocal.LocalIndex),
                new CodeInstruction(OpCodes.Callvirt, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))),

                // Call MetricsLogger.OnHarmonyPluginMetrics
                new CodeInstruction(OpCodes.Ldsfld, hookFieldInfo),
                new CodeInstruction(OpCodes.Ldloc, dictionaryLocal.LocalIndex),
                new CodeInstruction(OpCodes.Call, hookMethodInfo)
            });

            return retList;
        }
    }
}