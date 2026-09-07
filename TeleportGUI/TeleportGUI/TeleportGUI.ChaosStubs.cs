using System;
using System.Collections.Generic;
using Ext.Chaos.UIFramework;
using Network;
using UnityEngine;

namespace TeleportGUI
{
    /// <summary>
    /// Adapters so Chaos <c>TeleportGUI.UI.cs</c> compiles against the current Harmony mod
    /// without the full Lifecycle / Conditions / MonumentsPayments ports.
    /// </summary>
    public partial class TeleportGUIMod
    {
        private readonly Dictionary<ulong, WarpForm> _pendingWarpForms = new Dictionary<ulong, WarpForm>();
        private readonly Dictionary<ulong, object> _outgoingRequests = new Dictionary<ulong, object>();
        private readonly Dictionary<ulong, object> _incomingRequests = new Dictionary<ulong, object>();

        public string Title => "TeleportGUI";

        public CommandCallbackHandler CallbackHandler => m_CallbackHandler;

        private void SendLang(BasePlayer player, string key, params object[] args) =>
            SendMessage(player, Lang(key, player, args));

        private int GetMaxHomesForPlayer(BasePlayer player)
        {
            if (_config?.AdminsBypass == true && player != null && player.IsAdmin)
                return 0;
            return GetMaxHomes();
        }

        private enum TeleportPaymentKind { Teleport, Home, Warp }

        private bool HasReachedDailyLimit(BasePlayer player, TeleportGUIData.UserData userData, TeleportPaymentKind kind)
        {
            if (userData == null) return false;
            int limit;
            int used;
            switch (kind)
            {
                case TeleportPaymentKind.Home:
                    limit = GetHomeDailyLimit();
                    used = userData.HomeUsesToday;
                    break;
                case TeleportPaymentKind.Warp:
                    limit = GetWarpDailyLimit();
                    used = userData.WarpUsesToday;
                    break;
                default:
                    limit = GetTPDailyLimit();
                    used = userData.TPUsesToday;
                    break;
            }
            return limit > 0 && used >= limit;
        }

        private void CmdTpr(BasePlayer player, string[] args, bool tphere)
        {
            if (!tphere)
            {
                CmdTP(player, args);
                return;
            }

            if (args == null || args.Length == 0) return;
            BasePlayer target = FindPlayer(args[0]);
            if (target == null || !target.IsConnected)
            {
                SendMessage(player, "Player not found.");
                return;
            }

            Vector3 dest = player.transform.position;
            target.MovePosition(dest);
            target.ClientRPC(RpcTarget.Player("ForcePositionTo", target), dest);
            SendMessage(player, "Pulled " + target.displayName + " to you.");
            SendMessage(target, player.displayName + " pulled you.");
        }

        private enum InvalidBagReason { None, IsPublic, NotAssigned }

        private bool IsInvalidBagSpawn(TeleportGUIData.UserData.HomePoint home, BasePlayer player, out InvalidBagReason reason)
        {
            reason = InvalidBagReason.None;
            return false;
        }

        private bool IsHomePointValid(TeleportGUIData.UserData.HomePoint home) =>
            home != null && home.TryGetPosition(out _);

        private bool IsInsideEntity(Vector3 position) => false;

        private bool MeetsPositionConditions(BasePlayer player, Vector3 position, bool isWarp) => true;

        private IEnumerable<KeyValuePair<string, TeleportGUIData.WarpPoint>> EnumerateAllWarps()
        {
            if (_data?.WarpPoints == null)
                yield break;
            foreach (var kvp in _data.WarpPoints)
                yield return kvp;
        }

        private void RegisterWarpChatCommands() { }

        private void CmdWarpAdd(BasePlayer player, string[] args)
        {
            if (player == null || !player.IsAdmin) return;
            string name = args != null && args.Length > 0 ? SanitizeWarpName(args[0]) : "";
            if (string.IsNullOrEmpty(name))
            {
                SendMessage(player, "Invalid warp name.");
                return;
            }

            string perm = args != null && args.Length > 1 ? (args[1] ?? "").Trim() : "";
            string command = args != null && args.Length > 2 ? (args[2] ?? "").Trim() : "";
            Vector3 pos = _pendingWarpPosition.TryGetValue(player.userID, out var p) ? p : player.transform.position;
            _pendingWarpPosition.Remove(player.userID);

            if (_data.WarpPoints == null)
                _data.WarpPoints = new Dictionary<string, TeleportGUIData.WarpPoint>();

            var point = TeleportGUIData.WarpPoint.FromVector3(pos);
            point.Permission = perm;
            point.Command = command;
            _data.WarpPoints[name] = point;
            _warpData = _data.WarpPoints;
            SaveData();
            SendMessage(player, "Warp '" + name + "' added.");
        }
    }
}
