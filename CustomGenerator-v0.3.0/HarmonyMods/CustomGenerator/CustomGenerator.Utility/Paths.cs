using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CustomGenerator.Utility;

internal static class Paths
{
	public const string WorkspaceArg = "-cgen.workspace";

	public static readonly string Root = Resolve(out IsWorkspace);

	public static readonly bool IsWorkspace;

	public static string Get(params string[] parts)
	{
		return Path.Combine(new string[1] { Root }.Concat(parts).ToArray());
	}

	public static string Resolve(string path)
	{
		return Path.Combine(Root, path ?? "");
	}

	public static string Relative(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string text = Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
		return (fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? fullPath.Substring(text.Length) : fullPath).Replace('\\', '/');
	}

	private static string Resolve(out bool workspace)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		int num = Array.IndexOf(commandLineArgs, "-cgen.workspace");
		workspace = num >= 0 && num + 1 < commandLineArgs.Length && Directory.Exists(commandLineArgs[num + 1]);
		if (num >= 0 && !workspace)
		{
			Debug.LogError((object)("[CGen] -cgen.workspace folder not found: '" + ((num + 1 < commandLineArgs.Length) ? commandLineArgs[num + 1] : "") + "'. Files go to the server folder instead"));
		}
		return Path.GetFullPath(workspace ? commandLineArgs[num + 1] : ".");
	}
}
