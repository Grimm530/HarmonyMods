using System;
using System.Linq;
using Facepunch;

/// <summary>
/// Helpers for Rust builds where ConsoleSystem.Arg.Args and related APIs use Facepunch.StringView.
/// </summary>
internal static class StringViewCompat
{
    public static string AsString(this StringView value) => value.ToString();

    public static string[] AsStringArray(this StringView[] args)
    {
        if (args == null || args.Length == 0)
            return Array.Empty<string>();
        return args.Select(a => a.ToString()).ToArray();
    }

    public static string ArgAt(this StringView[] args, int index)
    {
        if (args == null || index < 0 || index >= args.Length)
            return string.Empty;
        return args[index].ToString();
    }
}
