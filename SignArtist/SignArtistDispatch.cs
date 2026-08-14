using System;
using UnityEngine;

namespace Oxide.Plugins
{
    internal partial class SignArtist
    {
        internal static SignArtist Instance { get; private set; }

        internal static SignArtist GetModInstance() => Instance;
        internal static void SetInstance(SignArtist inst) => Instance = inst;
        internal static void ClearInstance() => Instance = null;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[SignArtist] Init: " + ex.Message); }
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[SignArtist] LoadDefaultMessages: " + ex.Message); }
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[SignArtist] OnServerInitialized: " + ex); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[SignArtist] Unload: " + ex.Message); }
        }
    }
}
