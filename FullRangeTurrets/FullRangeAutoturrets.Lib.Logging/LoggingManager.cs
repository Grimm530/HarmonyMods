using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FullRangeAutoturrets.Lib.Logging;

public class LoggingManager
{
	public static void Log(object arg)
	{
		if (arg is string || arg is int || arg is float || arg is double || arg is bool)
		{
			Debug.Log((object)("Full Range Autoturrets: " + arg.ToString()));
			return;
		}
		Debug.Log((object)("Full Range Autoturrets: " + arg.GetType().Name));
		Dump(arg, dumpProps: true, "Full Range Autoturrets");
	}

	public static void Dump(object obj, bool dumpProps = false, string prefix = "DUMP")
	{
		if (obj == null)
		{
			Debug.Log((object)("[" + prefix + "] NULL"));
			return;
		}
		Debug.Log((object)("[" + prefix + "] Hash: " + obj.GetHashCode() + " | Type: " + obj.GetType().ToString()));
		if (!dumpProps)
		{
			return;
		}
		Dictionary<string, string> properties = GetProperties(obj);
		if (properties.Count > 0)
		{
			Debug.Log((object)"-------------------------");
		}
		foreach (KeyValuePair<string, string> item in properties)
		{
			Debug.Log((object)(item.Key + ": " + item.Value));
		}
	}

	private static Dictionary<string, string> GetProperties(object obj)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		if (obj == null)
		{
			return dictionary;
		}
		Type type = obj.GetType();
		PropertyInfo[] properties = type.GetProperties();
		foreach (PropertyInfo propertyInfo in properties)
		{
			object value = propertyInfo.GetValue(obj, new object[0]);
			string value2 = ((value == null) ? "" : value.ToString());
			dictionary.Add(propertyInfo.Name, value2);
		}
		return dictionary;
	}
}
