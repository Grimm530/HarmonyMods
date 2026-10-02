using Rust.Ai.Gen2;
using UnityEngine;

namespace CowFix;

/// <summary>
/// Wild livestock AI sleeps while nobody is nearby. The first sense tick after wake
/// uses the whole sleep as deltaTime, so one player walking into range banks minutes
/// of familiarity and the animal becomes kept. TruePVE then blocks damage from players
/// and from other animals.
/// </summary>
public class CowFixMod : IHarmonyModHooks
{
    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        int cleared = 0;
        foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
        {
            if (networkable is LivestockAnimal animal && FalseBond.TryClear(animal))
                cleared++;
        }

        Debug.Log(cleared > 0
            ? $"[CowFix] Loaded. Cleared false bonds on {cleared} wild livestock. Familiarity from a stalled sense tick is capped at one refresh."
            : "[CowFix] Loaded. Familiarity from a stalled sense tick is capped at one refresh.");
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        Debug.Log("[CowFix] Unloaded.");
    }
}
