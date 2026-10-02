using ConVar;
using Rust.Ai.Gen2;

namespace CowFix;

internal static class FalseBond
{
    private static int cleared;

    /// <summary>
    /// A penned animal has a cupboard home. An animal on a lead is in somebody's hands.
    /// Anything else that is already tame got there from the sleep-tick bug, or has not
    /// been kept yet. Drop it back under the bond line so it can be killed.
    /// </summary>
    public static bool TryClear(LivestockAnimal animal)
    {
        if (animal == null || animal.IsDestroyed || animal.IsDead())
            return false;

        float bond = Livestock.trustToBond;
        if (bond <= 0f || !animal.IsTame)
            return false;

        if (animal.IsLeading() || animal.TryGetHomeCupboard(out _))
            return false;

        for (int i = 0; i < 8; i++)
        {
            float seconds = animal.BestFamiliarity(out ulong userId);
            if (userId == 0L || seconds < bond)
                break;
            animal.SetFamiliarity(userId, 0f);
        }

        if (animal.IsTame)
            return false;

        cleared++;
        if (cleared == 1 || cleared % 100 == 0)
            UnityEngine.Debug.Log($"[CowFix] Cleared false livestock bonds: {cleared}.");
        return true;
    }
}
