using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomGenerator.Utility;

internal static class ConfigEditor
{
	private const string Resource = "ConfigEditor.html";

	private const string Token = "__CG_DATA__";

	private static readonly string SwapFolder = Paths.Get("maps", "prefabs");

	private static string _path;

	private static string _configJson;

	private static string _customFolder;

	private static JObject _schema;

	public static void Write(string path, JObject schema, string configJson, string customFolder)
	{
		_path = path;
		_schema = schema;
		_configJson = configJson;
		_customFolder = customFolder;
		Refresh();
	}

	public static void Refresh()
	{
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Expected O, but got Unknown
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Expected O, but got Unknown
		if (_path == null)
		{
			return;
		}
		string text;
		using (Stream stream = typeof(ConfigEditor).Assembly.GetManifestResourceStream("ConfigEditor.html"))
		{
			if (stream == null)
			{
				Logging.Warning("Config editor template 'ConfigEditor.html' is missing from the mod, editor not written");
				return;
			}
			using StreamReader streamReader = new StreamReader(stream);
			text = streamReader.ReadToEnd();
		}
		JObject val = new JObject();
		val["schema"] = (JToken)(object)_schema;
		val["config"] = (JToken)(object)JObject.Parse(_configJson);
		val["savedAt"] = JToken.op_Implicit(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
		val["lastRun"] = ReadJson(GenerationReport.LastRunLocation);
		val["prefabs"] = ReadJson(GenerationReport.PrefabsLocation);
		JObject val2 = new JObject();
		val2["custom"] = (JToken)(object)ListFiles(Paths.Resolve(_customFolder), ".map", ".prefab");
		val2["swap"] = (JToken)(object)ListFiles(SwapFolder, ".map");
		val["files"] = (JToken)(object)val2;
		string newValue = ((JToken)val).ToString((Formatting)0, Array.Empty<JsonConverter>()).Replace("</", "<\\/");
		File.WriteAllText(_path, text.Replace("__CG_DATA__", newValue));
	}

	private static JToken ReadJson(string path)
	{
		try
		{
			return File.Exists(path) ? JToken.Parse(File.ReadAllText(path)) : null;
		}
		catch (Exception ex)
		{
			Logging.Warning("Config editor: can't read " + path + ": " + ex.Message);
			return null;
		}
	}

	private static JArray ListFiles(string folder, params string[] extensions)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
		{
			return new JArray((object)(from x in (from x in Directory.GetFiles(folder)
					where extensions.Any((string e) => x.EndsWith(e, StringComparison.OrdinalIgnoreCase))
					select x).Select(Path.GetFileName)
				orderby x
				select x));
		}
		return new JArray();
	}
}
