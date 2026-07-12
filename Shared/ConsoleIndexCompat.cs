using System;
using System.Linq;
using System.Reflection;

/// <summary>
/// ConsoleSystem.Index.All may be read-only to external assemblies; set via reflection when needed.
/// </summary>
internal static class ConsoleIndexCompat
{
    private static readonly PropertyInfo AllProperty = typeof(ConsoleSystem.Index).GetProperty(
        "All",
        BindingFlags.Public | BindingFlags.Static);

    public static void RebuildAllFromServerDict()
    {
        try
        {
            var dict = ConsoleSystem.Index.Server.Dict;
            if (dict == null || AllProperty == null || !AllProperty.CanWrite)
                return;
            AllProperty.SetValue(null, dict.Values.ToArray(), null);
        }
        catch
        {
            // Non-fatal: commands still registered in Dict/GlobalDict.
        }
    }
}
