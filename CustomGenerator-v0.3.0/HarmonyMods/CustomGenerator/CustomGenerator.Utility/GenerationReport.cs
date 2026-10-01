using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomGenerator.Utility;

internal static class GenerationReport
{
	private sealed class Group
	{
		public string Name;

		public string Folder;

		public int Target;

		public List<string> Prefabs;
	}

	private sealed class Custom
	{
		public string Name;

		public int Placed;

		public int Count;

		public string Note;
	}

	private sealed class Swapped
	{
		public string File;

		public int Replaced;

		public string Note;
	}

	public static readonly string LastRunLocation = Paths.Get("HarmonyConfig", "CustomGenerator.lastrun.json");

	public static readonly string PrefabsLocation = Paths.Get("HarmonyConfig", "CustomGenerator.prefabs.json");

	private static readonly List<Group> Groups = new List<Group>();

	private static readonly List<Custom> CustomMonuments = new List<Custom>();

	private static readonly List<Swapped> Swaps = new List<Swapped>();

	private static readonly Dictionary<string, SortedSet<string>> GroupPrefabs = new Dictionary<string, SortedSet<string>>();

	private static string _swapSavedTo;

	private static string _imagePath;

	public static int Warnings;

	public static int Errors;

	public static string CurrentFolder;

	public static void MonumentGroup(string name, string folder, int target, IEnumerable<string> prefabs)
	{
		Groups.Add(new Group
		{
			Name = name,
			Folder = folder,
			Target = target,
			Prefabs = prefabs.ToList()
		});
	}

	public static void PrefabNames(IEnumerable<string> names)
	{
		if (CurrentFolder != null)
		{
			if (!GroupPrefabs.TryGetValue(CurrentFolder, out var value))
			{
				value = (GroupPrefabs[CurrentFolder] = new SortedSet<string>());
			}
			value.UnionWith(names);
		}
	}

	public static void CustomMonument(string name, int placed, int count, string note = null)
	{
		CustomMonuments.Add(new Custom
		{
			Name = name,
			Placed = placed,
			Count = count,
			Note = note
		});
	}

	public static void Swap(string file, int replaced, string note = null)
	{
		Swaps.Add(new Swapped
		{
			File = file,
			Replaced = replaced,
			Note = note
		});
	}

	public static void SwapSaved(string path)
	{
		_swapSavedTo = path;
	}

	public static void Image(string path)
	{
		_imagePath = path;
	}

	public static void Write(string mapPath)
	{
		TimeSpan elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
		string text = BuildText(mapPath, elapsed);
		string[] array = text.Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			Logging.Info(array[i].TrimEnd('\r'));
		}
		try
		{
			string text2 = Path.ChangeExtension(Path.GetFullPath(mapPath), ".report.txt");
			File.WriteAllText(text2, text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine);
			Logging.Info("Report saved to " + text2);
		}
		catch (Exception ex)
		{
			Logging.Error("Failed to save the report", ex);
		}
		try
		{
			File.WriteAllText(LastRunLocation, ((JToken)BuildJson(mapPath, elapsed, text)).ToString((Formatting)1, Array.Empty<JsonConverter>()));
			SavePrefabNames();
			ConfigEditor.Refresh();
		}
		catch (Exception ex2)
		{
			Logging.Error("Failed to save the last run data for the config editor", ex2);
		}
	}

	private static string BuildText(string mapPath, TimeSpan elapsed)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("===== CustomGenerator report =====");
		stringBuilder.AppendLine("Map:   " + Path.GetFullPath(mapPath));
		stringBuilder.AppendLine("Image: " + (_imagePath ?? "not rendered"));
		stringBuilder.AppendLine($"Size:  {World.Size}, seed {World.Seed}");
		stringBuilder.AppendLine("Time:  " + Format(elapsed) + " since server start");
		if (Groups.Count > 0)
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("Monuments:");
			int totalWidth = Groups.Max((Group x) => x.Name.Length);
			foreach (Group group in Groups)
			{
				string arg = ((group.Target >= 0) ? $"{group.Prefabs.Count}/{group.Target}" : group.Prefabs.Count.ToString());
				string arg2 = string.Join(", ", from x in @group.Prefabs
					group x by x into x
					select (x.Count() <= 1) ? x.Key : $"{x.Key} x{x.Count()}");
				stringBuilder.AppendLine($"  {group.Name.PadRight(totalWidth)}  {arg,5}  {arg2}".TrimEnd());
			}
		}
		if (CustomMonuments.Count > 0)
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("Custom monuments:");
			int totalWidth2 = CustomMonuments.Max((Custom x) => x.Name.Length);
			foreach (Custom customMonument in CustomMonuments)
			{
				stringBuilder.AppendLine(string.Format("  {0}  {1,5}  {2}", customMonument.Name.PadRight(totalWidth2), customMonument.Placed + "/" + customMonument.Count, customMonument.Note).TrimEnd());
			}
		}
		if (Swaps.Count > 0 || _swapSavedTo != null)
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("Swap:");
			int totalWidth3 = ((Swaps.Count > 0) ? Swaps.Max((Swapped x) => x.File.Length) : 0);
			foreach (Swapped swap in Swaps)
			{
				stringBuilder.AppendLine($"  {swap.File.PadRight(totalWidth3)}  {swap.Replaced,3} replaced  {swap.Note}".TrimEnd());
			}
			if (_swapSavedTo != null)
			{
				stringBuilder.AppendLine("  Saved to " + _swapSavedTo);
			}
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine(string.Format("Warnings: {0}, errors: {1}{2}", Warnings, Errors, (Warnings + Errors > 0) ? (" - see " + Logging.LogFilePath) : ""));
		stringBuilder.Append("==================================");
		return stringBuilder.ToString();
	}

	private static JObject BuildJson(string mapPath, TimeSpan elapsed, string text)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Expected O, but got Unknown
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Expected O, but got Unknown
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_0172: Expected O, but got Unknown
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Expected O, but got Unknown
		return new JObject
		{
			["finishedAt"] = JToken.op_Implicit(DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
			["map"] = JToken.op_Implicit(Relative(mapPath)),
			["image"] = JToken.op_Implicit((_imagePath == null) ? null : Relative(_imagePath)),
			["swapMap"] = JToken.op_Implicit((_swapSavedTo == null) ? null : Relative(_swapSavedTo)),
			["size"] = JToken.op_Implicit(World.Size),
			["seed"] = JToken.op_Implicit(World.Seed),
			["seconds"] = JToken.op_Implicit((int)elapsed.TotalSeconds),
			["groups"] = (JToken)new JArray((object)((IEnumerable<Group>)Groups).Select((Func<Group, JObject>)((Group x) => new JObject
			{
				["name"] = JToken.op_Implicit(x.Name),
				["folder"] = JToken.op_Implicit(x.Folder),
				["target"] = JToken.op_Implicit(x.Target),
				["prefabs"] = (JToken)new JArray((object)x.Prefabs)
			}))),
			["custom"] = (JToken)new JArray((object)((IEnumerable<Custom>)CustomMonuments).Select((Func<Custom, JObject>)((Custom x) => new JObject
			{
				["name"] = JToken.op_Implicit(x.Name),
				["placed"] = JToken.op_Implicit(x.Placed),
				["count"] = JToken.op_Implicit(x.Count),
				["note"] = JToken.op_Implicit(x.Note)
			}))),
			["swap"] = (JToken)new JArray((object)((IEnumerable<Swapped>)Swaps).Select((Func<Swapped, JObject>)((Swapped x) => new JObject
			{
				["file"] = JToken.op_Implicit(x.File),
				["replaced"] = JToken.op_Implicit(x.Replaced),
				["note"] = JToken.op_Implicit(x.Note)
			}))),
			["swapFiles"] = (JToken)(object)SwapFiles(),
			["warnings"] = JToken.op_Implicit(Warnings),
			["errors"] = JToken.op_Implicit(Errors),
			["log"] = JToken.op_Implicit(Relative(Logging.LogFilePath)),
			["text"] = JToken.op_Implicit(text)
		};
	}

	private static JArray SwapFiles()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected O, but got Unknown
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		JArray val = new JArray();
		string path = Paths.Get("maps", "prefabs");
		if (!Directory.Exists(path))
		{
			return val;
		}
		List<string> list = PrefabPaths();
		string[] files = Directory.GetFiles(path, "*.map");
		foreach (string path2 in files)
		{
			string name = Path.GetFileNameWithoutExtension(path2).ToLowerInvariant();
			val.Add((JToken)new JObject
			{
				["file"] = JToken.op_Implicit(Path.GetFileName(path2)),
				["known"] = ((list.Count == 0) ? null : JToken.op_Implicit(list.Any((string x) => x.Contains(name))))
			});
		}
		return val;
	}

	private static List<string> PrefabPaths()
	{
		return (typeof(StringPool).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((FieldInfo x) => x.FieldType == typeof(Dictionary<string, uint>))?.GetValue(null) as Dictionary<string, uint>)?.Keys.ToList() ?? new List<string>();
	}

	private static void SavePrefabNames()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Expected O, but got Unknown
		JObject val = (JObject)(File.Exists(PrefabsLocation) ? ((object)JObject.Parse(File.ReadAllText(PrefabsLocation))) : ((object)new JObject()));
		foreach (KeyValuePair<string, SortedSet<string>> groupPrefab in GroupPrefabs)
		{
			SortedSet<string> sortedSet = new SortedSet<string>(groupPrefab.Value);
			JToken obj = val[groupPrefab.Key];
			JArray val2 = (JArray)(object)((obj is JArray) ? obj : null);
			if (val2 != null)
			{
				sortedSet.UnionWith(((JContainer)val2).Values<string>());
			}
			val[groupPrefab.Key] = (JToken)new JArray((object)sortedSet);
		}
		File.WriteAllText(PrefabsLocation, ((JToken)val).ToString((Formatting)1, Array.Empty<JsonConverter>()));
	}

	private static string Relative(string path)
	{
		return Paths.Relative(path);
	}

	private static string Format(TimeSpan time)
	{
		if (!(time.TotalHours >= 1.0))
		{
			return $"{time.Minutes}m {time.Seconds}s";
		}
		return $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s";
	}
}
