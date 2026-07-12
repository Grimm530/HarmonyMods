using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;

namespace MapVoter.Patches;

/// <summary>
/// Intercept server console commands before ConsoleSystem.Run. When the user types "mvote" or "mvoteready"
/// the game may still report "command not found" (Run doesn't use Find). We handle our commands here and
/// remove them from the queue so they are never passed to Run; other commands are re-enqueued.
/// No ReplyWith - only Debug.Log to avoid deadlock.
/// </summary>
[HarmonyPatch(typeof(ServerConsole), "Update")]
public static class Patch_ServerConsole_Update
{
    private static FieldInfo _queuedCommandsField;
    private static readonly System.Collections.Generic.List<string> RequeueBuffer = new System.Collections.Generic.List<string>(16);

    [HarmonyPrefix]
    public static void Prefix(ServerConsole __instance)
    {
        if (__instance == null || MapVoterMod.Instance == null) return;

        if (_queuedCommandsField == null)
            _queuedCommandsField = AccessTools.Field(typeof(ServerConsole), "queuedCommands");
        if (_queuedCommandsField?.GetValue(__instance) is not ConcurrentQueue<string> queue)
            return;

        // Hot path: most frames have no console lines — avoid List allocation and drain loop.
        if (queue.IsEmpty)
            return;

        RequeueBuffer.Clear();
        while (queue.TryDequeue(out string line))
        {
            if (MapVoterMod.Instance.TryRunServerConsoleCommand(line))
                continue; // we handled it; don't re-enqueue
            RequeueBuffer.Add(line);
        }

        for (int i = 0; i < RequeueBuffer.Count; i++)
            queue.Enqueue(RequeueBuffer[i]);
    }
}
