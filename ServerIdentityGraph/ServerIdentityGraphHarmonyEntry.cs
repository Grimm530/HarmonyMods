using System;
using UnityEngine;

namespace ServerIdentityGraph
{
    /// <summary>
    /// Harmony mod entry. Writes current in-game team + clan to
    /// HarmonyData/ServerIdentityGraph for Discord /lookup.
    /// </summary>
    public class ServerIdentityGraphHarmonyEntry : IHarmonyModHooks
    {
        public static ServerIdentityGraphHarmonyEntry Instance { get; private set; }

        ConsoleSystem.Command _lookupCmd;
        ConsoleSystem.Command _flushCmd;

        public ServerIdentityGraphHarmonyEntry()
        {
            IdentityStore.Init();
        }

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            RegisterCommands();
            StartFlushLoop();
            var invokeHandler = SingletonComponent<InvokeHandler>.Instance;
            if (invokeHandler != null)
                InvokeHandler.Invoke(invokeHandler, RecaptureOnline, 3f);
            Debug.Log("[ServerIdentityGraph] Loaded. Data: HarmonyData/ServerIdentityGraph/players/{steamId}.json");
        }

        void RecaptureOnline()
        {
            IdentityCollector.RecordAllOnline();
            IdentityStore.Flush();
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            StopFlushLoop();
            UnregisterCommands();
            IdentityStore.Shutdown();
            Instance = null;
            Debug.Log("[ServerIdentityGraph] Unloaded.");
        }

        void StartFlushLoop()
        {
            var invokeHandler = SingletonComponent<InvokeHandler>.Instance;
            if (invokeHandler == null)
            {
                Debug.LogWarning("[ServerIdentityGraph] InvokeHandler not ready; identity files flush only on unload.");
                return;
            }

            float interval = IdentityStore.Config.FlushSeconds;
            if (interval < 0.5f)
                interval = 0.5f;
            InvokeHandler.InvokeRepeating(invokeHandler, FlushTick, interval, interval);
        }

        void StopFlushLoop()
        {
            var invokeHandler = SingletonComponent<InvokeHandler>.Instance;
            if (invokeHandler == null)
                return;
            try
            {
                InvokeHandler.CancelInvoke(invokeHandler, FlushTick);
            }
            catch
            {
                // ignore
            }
        }

        void FlushTick() => IdentityStore.Flush();

        void RegisterCommands()
        {
            try
            {
                _lookupCmd = new ConsoleSystem.Command
                {
                    Name = "identity.lookup",
                    FullName = "global.identity.lookup",
                    Variable = false,
                    ServerAdmin = true,
                    Call = CmdLookup
                };
                ConsoleSystem.Index.Server.Dict["global.identity.lookup"] = _lookupCmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict["identity.lookup"] = _lookupCmd;

                _flushCmd = new ConsoleSystem.Command
                {
                    Name = "identity.flush",
                    FullName = "global.identity.flush",
                    Variable = false,
                    ServerAdmin = true,
                    Call = CmdFlush
                };
                ConsoleSystem.Index.Server.Dict["global.identity.flush"] = _flushCmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict["identity.flush"] = _flushCmd;

                var recaptureCmd = new ConsoleSystem.Command
                {
                    Name = "identity.recapture",
                    FullName = "global.identity.recapture",
                    Variable = false,
                    ServerAdmin = true,
                    Call = arg =>
                    {
                        RecaptureOnline();
                        arg?.ReplyWith("OK");
                    }
                };
                ConsoleSystem.Index.Server.Dict["global.identity.recapture"] = recaptureCmd;
                if (ConsoleSystem.Index.Server.GlobalDict != null)
                    ConsoleSystem.Index.Server.GlobalDict["identity.recapture"] = recaptureCmd;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ServerIdentityGraph] Command registration failed: " + ex.Message);
            }
        }

        void UnregisterCommands()
        {
            try
            {
                ConsoleSystem.Index.Server.Dict?.Remove("global.identity.lookup");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("identity.lookup");
                ConsoleSystem.Index.Server.Dict?.Remove("global.identity.flush");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("identity.flush");
                ConsoleSystem.Index.Server.Dict?.Remove("global.identity.recapture");
                ConsoleSystem.Index.Server.GlobalDict?.Remove("identity.recapture");
                _lookupCmd = null;
                _flushCmd = null;
            }
            catch
            {
                // ignore
            }
        }

        void CmdLookup(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length < 1)
            {
                arg?.ReplyWith("USAGE: identity.lookup <steamid>");
                return;
            }

            string raw = arg.Args.ArgAt(0).Trim();
            ulong steamId;
            if (!ulong.TryParse(raw, out steamId) || steamId == 0)
            {
                arg.ReplyWith("Invalid SteamID64");
                return;
            }

            arg.ReplyWith(IdentityStore.DebugDump(steamId));
        }

        void CmdFlush(ConsoleSystem.Arg arg)
        {
            IdentityStore.Flush();
            arg?.ReplyWith("OK");
        }
    }
}
