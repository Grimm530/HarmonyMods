using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace IndustrialRecyclerHarmony.Patches
{
    /// <summary>
    /// Dedicated server console queues lines on ServerConsole and ConsoleSystem.Run often
    /// treats the whole line as the command name (Command 'giveindustrialrecycler 7656…' not found).
    /// Intercept our give commands here, same pattern as MapVoter.
    /// </summary>
    [HarmonyPatch(typeof(ServerConsole), "Update")]
    public static class Patch_ServerConsole_Update
    {
        private static FieldInfo _queuedCommandsField;
        private static readonly List<string> RequeueBuffer = new List<string>(16);

        [HarmonyPrefix]
        public static void Prefix(ServerConsole __instance)
        {
            if (__instance == null || IndustrialRecyclerMod.Instance == null) return;

            if (_queuedCommandsField == null)
                _queuedCommandsField = AccessTools.Field(typeof(ServerConsole), "queuedCommands");
            if (_queuedCommandsField?.GetValue(__instance) is not ConcurrentQueue<string> queue)
                return;
            if (queue.IsEmpty)
                return;

            RequeueBuffer.Clear();
            while (queue.TryDequeue(out string line))
            {
                if (IndustrialRecyclerMod.Instance.TryRunServerConsoleCommand(line))
                    continue;
                RequeueBuffer.Add(line);
            }

            for (int i = 0; i < RequeueBuffer.Count; i++)
                queue.Enqueue(RequeueBuffer[i]);
        }
    }
}
