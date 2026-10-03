using ConVar;
using Rust.Ai.Gen2;

namespace CowFix;

internal static class FalseBond
{
    private static int cleared;

    /// <summary>
    /// TruePVE treats an animal as kept once any player has trustToLead seconds with it.
    /// The hotfix did not wipe trust already stored on wild animals, so they stay unkillable.
    /// A penned animal has a cupboard home. An animal on a lead is in somebody's hands.
    /// Anything else at or over the lead line is dropped back to zero trust.
    /// </summary>
    public static bool TryClear(LivestockAnimal animal)
    {
        if (animal == null || animal.IsDestroyed || animal.IsDead())
            return false;

        float lead = Livestock.trustToLead;
        if (lead <= 0f || !animal.IsLeadable)
            return false;

        if (animal.IsLeading() || animal.TryGetHomeCupboard(out _))
            return false;

        for (int i = 0; i < 8; i++)
        {
            float seconds = animal.BestFamiliarity(out ulong userId);
            if (userId == 0L || seconds < lead)
                break;
            animal.SetFamiliarity(userId, 0f);
        }

        if (animal.IsLeadable)
            return false;

        cleared++;
        if (cleared == 1 || cleared % 100 == 0)
            UnityEngine.Debug.Log($"[CowFix] Cleared false livestock bonds: {cleared}.");
        return true;
    }
}
