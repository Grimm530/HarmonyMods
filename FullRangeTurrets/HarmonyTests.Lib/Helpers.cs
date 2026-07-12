using System.Reflection;

namespace HarmonyTests.Lib;

public class Helpers
{
	public static T GetFieldValue<T>(object obj, string name)
	{
		if (obj == null) return default;
		FieldInfo field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field == null) return default;
		object value = field.GetValue(obj);
		if (value == null && typeof(T).IsValueType) return default;
		return (T)value;
	}

	public static void SetFieldValue(object obj, string name, object value)
	{
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		obj.GetType().GetField(name, bindingAttr)?.SetValue(obj, value);
	}
}
