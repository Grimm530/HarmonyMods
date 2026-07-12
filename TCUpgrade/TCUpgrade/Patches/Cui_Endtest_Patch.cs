namespace TCUpgrade.Patches;

/// <summary>
/// Historical note: TCUpgrade previously patched cui.endtest to bridge CUI button clicks.
/// BetterTC 1.6.3 switched buttons back to direct SENDCMD commands, so this patch is intentionally disabled.
/// </summary>
public static class Cui_Endtest_Patch
{
}
