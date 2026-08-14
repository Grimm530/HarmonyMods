using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace PermissionsHarmony
{
    /// <summary>
    /// Harmony mods load at BeforeSceneLoad, before Bootstrap applies +server.identity.
    /// FileStorage's static ctor opens server/&lt;identity&gt;/sv.files.*.db. If identity is still
    /// the Facepunch default, SQLite error 14 poisons the type for the process and save load aborts.
    /// </summary>
    internal static class ServerIdentityGuard
    {
        internal const string DefaultIdentity = "my_server_identity";
        private static bool _logged;

        internal static void EnsureReady()
        {
            try
            {
                ApplyIdentityFromCommandLine();
                EnsureFilesFolder();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] FileStorage identity guard: " + ex.Message);
            }
        }

        private static void ApplyIdentityFromCommandLine()
        {
            var current = ConVar.Server.identity;
            if (!IsDefaultIdentity(current))
                return;

            var fromArgs = ReadIdentityFromCommandLine();
            if (string.IsNullOrEmpty(fromArgs) || !IsSafeIdentity(fromArgs))
                return;

            ConVar.Server.identity = fromArgs;
            if (!_logged)
            {
                _logged = true;
                Debug.Log("[Permissions] Applied server.identity from command line before FileStorage: " + fromArgs);
            }
        }

        private static void EnsureFilesFolder()
        {
            var folder = ConVar.Server.filesStorageFolder;
            if (string.IsNullOrEmpty(folder))
                return;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Debug.Log("[Permissions] Created FileStorage folder: " + folder);
            }
        }

        internal static bool IsDefaultIdentity(string identity)
        {
            return string.IsNullOrEmpty(identity) ||
                   string.Equals(identity, DefaultIdentity, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                return false;
            if (identity.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
            if (identity.IndexOfAny(new[] { '/', '\\' }) >= 0)
                return false;
            return identity.IndexOf("..", StringComparison.Ordinal) < 0;
        }

        private static string ReadIdentityFromCommandLine()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { return null; }
            if (args == null)
                return null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.IsNullOrEmpty(a))
                    continue;

                if (!StartsWithIdentitySwitch(a))
                    continue;

                var eq = a.IndexOf('=');
                if (eq >= 0)
                {
                    var v = Unquote(a.Substring(eq + 1));
                    if (!string.IsNullOrEmpty(v))
                        return v;
                }

                if (i + 1 < args.Length)
                {
                    var v = Unquote(args[i + 1]);
                    if (!string.IsNullOrEmpty(v) && v[0] != '+' && v[0] != '-')
                        return v;
                }
            }

            return null;
        }

        private static bool StartsWithIdentitySwitch(string arg)
        {
            if (arg.StartsWith("+server.identity", StringComparison.OrdinalIgnoreCase))
                return true;
            if (arg.StartsWith("server.identity", StringComparison.OrdinalIgnoreCase) &&
                (arg.Length == "server.identity".Length || arg["server.identity".Length] == '='))
                return true;
            return false;
        }

        private static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2);
            return value;
        }
    }

    /// <summary>
    /// Runs immediately before FileStorage opens sv.files.*.db, including the static field initializer.
    /// Do not touch FileStorage.server here — that is what we are initializing.
    /// </summary>
    [HarmonyPatch(typeof(FileStorage), MethodType.Constructor, new[] { typeof(string), typeof(bool) })]
    internal static class FileStorage_Ctor_Patch
    {
        static void Prefix()
        {
            ServerIdentityGuard.EnsureReady();
        }
    }
}
