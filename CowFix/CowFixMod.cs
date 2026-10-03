using System;
using System.Collections.Generic;
using Rust.Ai.Gen2;
using UnityEngine;

namespace CowFix;

/// <summary>
/// Facepunch stopped runaway breeding, but animals that already bonded stay that way
/// in the save. TruePVE blocks damage once an animal is leadable. This drops that
/// stored trust when the animal has no player cupboard and is not on a lead.
/// A stalled sense tick can still bank a full bond in one frame, so that credit stays capped.
/// </summary>
public class CowFixMod : IHarmonyModHooks
{
    private readonly List<ConsoleSystem.Command> _commands = new List<ConsoleSystem.Command>();

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        RegisterCommands();
        int cleared = 0;
        foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
        {
            if (networkable is LivestockAnimal animal && FalseBond.TryClear(animal))
                cleared++;
        }

        Debug.Log(cleared > 0
            ? $"[CowFix] Loaded. Cleared stored trust on {cleared} livestock with no cupboard. Familiarity from a stalled sense tick is capped at one refresh."
            : "[CowFix] Loaded. Familiarity from a stalled sense tick is capped at one refresh.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        UnregisterCommands();
        Debug.Log("[CowFix] Unloaded.");
    }

    private void RegisterCommands()
    {
        UnregisterCommands();
        Register("cowfix.pop", arg =>
        {
            arg?.ReplyWith("[CowFix] " + Population.Report());
        });
        Register("cowfix.purge", arg =>
        {
            int killed = Population.PurgeUnowned();
            arg?.ReplyWith("[CowFix] Purged " + killed + " livestock with no player tool cupboard. " + Population.Report());
        });
    }

    private void Register(string name, Action<ConsoleSystem.Arg> handler)
    {
        var cmd = new ConsoleSystem.Command
        {
            Name = name,
            FullName = name,
            Variable = false,
            ServerAdmin = true,
            ServerUser = true,
            AllowRunFromServer = true,
            Call = handler
        };
        _commands.Add(cmd);
        if (ConsoleSystem.Index.Server.Dict != null)
            ConsoleSystem.Index.Server.Dict[name] = cmd;
    }

    private void UnregisterCommands()
    {
        var dict = ConsoleSystem.Index.Server.Dict;
        var globalDict = ConsoleSystem.Index.Server.GlobalDict;
        for (int i = 0; i < _commands.Count; i++)
        {
            string name = _commands[i].FullName;
            dict?.Remove(name);
            globalDict?.Remove(name);
        }
        _commands.Clear();
    }
}
