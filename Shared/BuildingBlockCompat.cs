using System.Reflection;
using UnityEngine;

/// <summary>
/// Access to BuildingBlock.playerCustomColourToApply (private in current game assembly).
/// </summary>
internal static class BuildingBlockCompat
{
    private static readonly FieldInfo PlayerCustomColourField = typeof(BuildingBlock).GetField(
        "playerCustomColourToApply",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void SetPlayerCustomColourToApply(BuildingBlock block, uint value)
    {
        if (block == null || PlayerCustomColourField == null)
            return;
        PlayerCustomColourField.SetValue(block, value);
    }
}
