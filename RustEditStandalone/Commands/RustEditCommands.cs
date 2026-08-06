using System;
using System.Collections.Generic;
using RustEditStandalone.Features;
using UnityEngine;

namespace RustEditStandalone.Commands;

public static class RustEditCommands
{
    private static readonly List<ConsoleSystem.Command> Registered = new();

    public static void Register()
    {
        RegisterOne("rustedit", Help);
        RegisterOne("rustedit.apc.status", ApcStatus);
        RegisterOne("rustedit.apc.killall", ApcKillAll);
        RegisterOne("rustedit.apc.respawn", ApcRespawn);
        RegisterOne("rustedit.io.reset", IoReset);
        RegisterOne("rustedit.vending.restockall", VendingRestockAll);
        RegisterOne("rustedit.vending.restock", VendingRestockLook);
        RegisterOne("rustedit.resource.respawnall", ResourceRespawnAll);
        RegisterOne("rustedit.resource.info", ResourceInfo);
        RegisterOne("rustedit.loot.respawnall", LootRespawnAll);
        RegisterOne("rustedit.loot.info", LootInfo);
        RegisterOne("rustedit.junkpile.respawnall", JunkRespawnAll);
        RegisterOne("rustedit.junkpile.info", JunkInfo);
        RegisterOne("rustedit.desk.populate", DeskPopulate);
        RegisterOne("rustedit.spawns.show", SpawnsShow);
        RegisterOne("rustedit.ocean.show", OceanShow);
        RegisterOne("rustedit.checkupdate", CheckUpdate);
        RegisterOne("rustedit.downloadupdate", DownloadUpdate);
    }

    public static void Unregister()
    {
        try
        {
            var dict = ConsoleSystem.Index.Server.Dict;
            var global = ConsoleSystem.Index.Server.GlobalDict;
            for (int i = 0; i < Registered.Count; i++)
            {
                var cmd = Registered[i];
                dict?.Remove(cmd.FullName);
                if (!string.IsNullOrEmpty(cmd.Parent))
                    dict?.Remove(cmd.Parent + "." + cmd.Name);
                global?.Remove(cmd.Name);
            }
        }
        catch { }
        Registered.Clear();
    }

    private static void RegisterOne(string name, Action<ConsoleSystem.Arg> handler)
    {
        bool hasDot = name.Contains(".");
        string parent = hasDot ? name.Split('.')[0] : "global";
        string cmdName = hasDot ? name.Substring(name.IndexOf('.') + 1) : name;
        string fullName = hasDot ? name : "global." + name;
        string dictKey = hasDot ? name : fullName;

        var cmd = new ConsoleSystem.Command
        {
            Name = cmdName,
            Parent = parent,
            FullName = fullName,
            Variable = false,
            ServerAdmin = false,
            AllowRunFromServer = true,
            Call = arg =>
            {
                try
                {
                    if (!IsAdmin(arg))
                    {
                        Reply(arg, "Admin only.");
                        return;
                    }
                    handler(arg);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[RustEditStandalone] cmd " + name + ": " + ex.Message);
                }
            }
        };

        if (ConsoleSystem.Index.Server.Dict != null)
            ConsoleSystem.Index.Server.Dict[dictKey] = cmd;
        if (!hasDot && ConsoleSystem.Index.Server.GlobalDict != null)
            ConsoleSystem.Index.Server.GlobalDict[cmdName] = cmd;
        Registered.Add(cmd);
    }

    private static bool IsAdmin(ConsoleSystem.Arg arg)
    {
        if (arg == null) return true;
        if (arg.Connection == null) return true; // RCON / server console
        var player = arg.Player();
        return player != null && player.IsAdmin;
    }

    private static void Reply(ConsoleSystem.Arg arg, string msg)
    {
        if (arg?.Connection != null)
            arg.ReplyWith(msg);
        else
            Debug.Log("[RustEditStandalone] " + msg);
    }

    private static void Help(ConsoleSystem.Arg arg)
    {
        Reply(arg,
            "RustEditStandalone commands:\n" +
            "rustedit.apc.status|killall|respawn\n" +
            "rustedit.io.reset\n" +
            "rustedit.vending.restock|restockall\n" +
            "rustedit.resource.respawnall|info\n" +
            "rustedit.loot.respawnall|info\n" +
            "rustedit.junkpile.respawnall|info\n" +
            "rustedit.desk.populate\n" +
            "rustedit.spawns.show [time]\n" +
            "rustedit.ocean.show [time]\n" +
            "(updater commands unsupported)");
    }

    private static void ApcStatus(ConsoleSystem.Arg arg) => Reply(arg, ApcFeature.Status());
    private static void ApcKillAll(ConsoleSystem.Arg arg) => Reply(arg, "Killed APCs: " + ApcFeature.KillAll());
    private static void ApcRespawn(ConsoleSystem.Arg arg) => Reply(arg, "Respawned APCs: " + ApcFeature.RespawnAll());
    private static void IoReset(ConsoleSystem.Arg arg)
    {
        IoFeature.ProcessIOEntities();
        Reply(arg, "IO connections reset from map data.");
    }
    private static void VendingRestockAll(ConsoleSystem.Arg arg) => Reply(arg, "Restocked: " + VendingFeature.RestockAll());
    private static void VendingRestockLook(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) { Reply(arg, "Player only."); return; }
        if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 10f))
        {
            Reply(arg, "No vending machine in sight.");
            return;
        }
        var vm = hit.GetEntity() as NPCVendingMachine;
        if (vm == null) { Reply(arg, "Look at a vending machine."); return; }
        Reply(arg, VendingFeature.RestockOne(vm) ? "Restocked." : "No profile.");
    }
    private static void ResourceRespawnAll(ConsoleSystem.Arg arg) => Reply(arg, "Resources respawned: " + ResourceFeature.RespawnAll());
    private static void ResourceInfo(ConsoleSystem.Arg arg) => Reply(arg, ResourceFeature.Info());
    private static void LootRespawnAll(ConsoleSystem.Arg arg) => Reply(arg, "Loot respawned: " + LootFeature.RespawnAll());
    private static void LootInfo(ConsoleSystem.Arg arg) => Reply(arg, LootFeature.Info());
    private static void JunkRespawnAll(ConsoleSystem.Arg arg) => Reply(arg, "Junk piles respawned: " + JunkPileFeature.RespawnAll());
    private static void JunkInfo(ConsoleSystem.Arg arg) => Reply(arg, JunkPileFeature.Info());
    private static void DeskPopulate(ConsoleSystem.Arg arg) => Reply(arg, "Keycards populated: " + DeskKeycardFeature.PopulateAll());
    private static void SpawnsShow(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) { Reply(arg, "Player only."); return; }
        float t = arg.HasArgs() ? arg.GetFloat(0, 30f) : 30f;
        SpawnFeature.Show(player, t);
        Reply(arg, "Showing " + SpawnFeature.SpawnPoints.Count + " spawn points.");
    }
    private static void OceanShow(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null) { Reply(arg, "Player only."); return; }
        float t = arg.HasArgs() ? arg.GetFloat(0, 30f) : 30f;
        OceanFeature.Show(player, t);
        Reply(arg, "Showing ocean path.");
    }
    private static void CheckUpdate(ConsoleSystem.Arg arg) => Reply(arg, "AutoUpdater omitted in RustEditStandalone (no Oxide Managed target).");
    private static void DownloadUpdate(ConsoleSystem.Arg arg) => Reply(arg, "AutoUpdater omitted in RustEditStandalone.");
}
