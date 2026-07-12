using UnityEngine;

namespace Facepunch.Harmony.GatherManager
{
    /// <summary>
    /// Appends gather description line to server description. No Steam tags.
    /// Called from GatherManagerMod.OnLoaded.
    /// </summary>
    internal static class ServerMgr_UpdateServerInformation
    {
        /// <summary>Call once from OnLoaded: append gather description to ConVar.Server.description.</summary>
        public static void AppendGatherDescription( string gatherDescriptionLine )
        {
            if ( !string.IsNullOrEmpty( gatherDescriptionLine ) )
                ConVar.Server.description += "\n" + gatherDescriptionLine;
        }
    }
}
