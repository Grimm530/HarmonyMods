using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace CustomGenerator.Utility;

internal static class ConfigSchema
{
	public static JObject Build(Type root, IContractResolver resolver, bool ru)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Expected O, but got Unknown
		JObject obj = Describe(root, resolver, ru, new HashSet<Type>());
		((JContainer)obj).AddFirst((object)new JProperty("$schema", (object)"http://json-schema.org/draft-07/schema#"));
		obj["title"] = JToken.op_Implicit("CustomGenerator.json");
		return obj;
	}

	private static JObject Describe(Type type, IContractResolver resolver, bool ru, HashSet<Type> stack)
	{
		//IL_021f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Expected O, but got Unknown
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected O, but got Unknown
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected O, but got Unknown
		//IL_038b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0390: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b3: Expected O, but got Unknown
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Expected O, but got Unknown
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Expected O, but got Unknown
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Expected O, but got Unknown
		//IL_0168: Expected O, but got Unknown
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Expected O, but got Unknown
		//IL_01c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f2: Expected O, but got Unknown
		//IL_020b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Expected O, but got Unknown
		//IL_02da: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e4: Expected O, but got Unknown
		//IL_034f: Unknown result type (might be due to invalid IL or missing references)
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!(type == typeof(string)))
		{
			if (!(type == typeof(bool)))
			{
				if (!(type == typeof(float)) && !(type == typeof(double)) && !(type == typeof(decimal)))
				{
					if (!(type == typeof(uint)) && !(type == typeof(ulong)))
					{
						if (!type.IsPrimitive)
						{
							if (type.IsEnum)
							{
								JObject val = new JObject { ["type"] = JToken.op_Implicit("string") };
								object[] names = Enum.GetNames(type);
								val["enum"] = (JToken)new JArray(names);
								return val;
							}
							Type[] array = FindGeneric(type, typeof(IDictionary<, >));
							if (array == null)
							{
								Type[] array2 = FindGeneric(type, typeof(IEnumerable<>));
								if (array2 == null)
								{
									JsonContract obj = resolver.ResolveContract(type);
									JsonObjectContract val2 = (JsonObjectContract)(object)((obj is JsonObjectContract) ? obj : null);
									if (val2 != null && stack.Add(type))
									{
										object obj2 = null;
										try
										{
											obj2 = Activator.CreateInstance(type);
										}
										catch
										{
										}
										JObject val3 = new JObject();
										foreach (JsonProperty item in (Collection<JsonProperty>)(object)val2.Properties)
										{
											if (item.Ignored)
											{
												continue;
											}
											Predicate<object> shouldSerialize = item.ShouldSerialize;
											if (shouldSerialize != null && !shouldSerialize(null))
											{
												continue;
											}
											JObject val4 = Describe(item.PropertyType, resolver, ru, stack);
											val4["x-name"] = JToken.op_Implicit(item.UnderlyingName);
											IAttributeProvider attributeProvider = item.AttributeProvider;
											IList<Attribute> source = ((attributeProvider != null) ? attributeProvider.GetAttributes(true) : null) ?? new List<Attribute>();
											DescAttribute descAttribute = source.OfType<DescAttribute>().FirstOrDefault();
											if (descAttribute != null)
											{
												((JContainer)val4).AddFirst((object)new JProperty("description", (object)(ru ? descAttribute.Ru : descAttribute.En)));
											}
											SchemaAttribute schemaAttribute = source.OfType<SchemaAttribute>().FirstOrDefault();
											if (schemaAttribute != null)
											{
												ApplyExtra(val4, schemaAttribute);
											}
											if (obj2 != null && item.ValueProvider != null && IsSimple(item.PropertyType))
											{
												object value = item.ValueProvider.GetValue(obj2);
												if (value != null)
												{
													val4["default"] = (JToken)((!item.PropertyType.IsEnum) ? ((object)JToken.FromObject(value)) : ((object)new JValue(value.ToString())));
												}
											}
											val3[item.PropertyName] = (JToken)(object)val4;
										}
										stack.Remove(type);
										return new JObject
										{
											["type"] = JToken.op_Implicit("object"),
											["properties"] = (JToken)(object)val3
										};
									}
									return new JObject();
								}
								return new JObject
								{
									["type"] = JToken.op_Implicit("array"),
									["items"] = (JToken)(object)Describe(array2[0], resolver, ru, stack)
								};
							}
							return new JObject
							{
								["type"] = JToken.op_Implicit("object"),
								["additionalProperties"] = (JToken)(object)Describe(array[1], resolver, ru, stack)
							};
						}
						return new JObject { ["type"] = JToken.op_Implicit("integer") };
					}
					return new JObject
					{
						["type"] = JToken.op_Implicit("integer"),
						["minimum"] = JToken.op_Implicit(0)
					};
				}
				return new JObject { ["type"] = JToken.op_Implicit("number") };
			}
			return new JObject { ["type"] = JToken.op_Implicit("boolean") };
		}
		return new JObject { ["type"] = JToken.op_Implicit("string") };
	}

	private static void ApplyExtra(JObject node, SchemaAttribute extra)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Expected O, but got Unknown
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Expected O, but got Unknown
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Expected O, but got Unknown
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Expected O, but got Unknown
		JToken obj = node["items"];
		JObject val = (JObject)(((obj is JObject) ? obj : null) ?? ((object)/*isinst with value type is only supported in some contexts*/) ?? ((object)node));
		string[] array = extra.Values ?? ((extra.Enum != null) ? Enum.GetNames(extra.Enum) : null);
		if (array != null)
		{
			val["enum"] = (JToken)new JArray((object)array.Distinct());
		}
		if (!double.IsNaN(extra.Min))
		{
			val["minimum"] = JToken.op_Implicit(extra.Min);
		}
		if (!double.IsNaN(extra.Max))
		{
			val["maximum"] = JToken.op_Implicit(extra.Max);
		}
		if (extra.ReadOnly)
		{
			node["readOnly"] = JToken.op_Implicit(true);
		}
		if (!string.IsNullOrEmpty(extra.ItemTitle))
		{
			object[] array2 = Split(extra.ItemTitle);
			node["x-itemTitle"] = (JToken)new JArray(array2);
		}
		if (extra.Addable)
		{
			node["x-addable"] = JToken.op_Implicit(true);
		}
		if (!string.IsNullOrEmpty(extra.Suggest))
		{
			node["x-suggest"] = JToken.op_Implicit(extra.Suggest);
		}
		if (!double.IsNaN(extra.SumTo))
		{
			JObject val2 = new JObject { ["target"] = JToken.op_Implicit(extra.SumTo) };
			if (!string.IsNullOrEmpty(extra.SumFields))
			{
				object[] array2 = Split(extra.SumFields);
				val2["fields"] = (JToken)new JArray(array2);
			}
			node["x-sum"] = (JToken)(object)val2;
		}
	}

	private static string[] Split(string list)
	{
		return (from x in list.Split(',')
			select x.Trim() into x
			where x.Length > 0
			select x).ToArray();
	}

	private static bool IsSimple(Type type)
	{
		if (!type.IsPrimitive && !type.IsEnum && !(type == typeof(string)))
		{
			return type == typeof(decimal);
		}
		return true;
	}

	private static Type[] FindGeneric(Type type, Type definition)
	{
		if (type == typeof(string))
		{
			return null;
		}
		if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
		{
			return type.GetGenericArguments();
		}
		return type.GetInterfaces().FirstOrDefault((Type x) => x.IsGenericType && x.GetGenericTypeDefinition() == definition)?.GetGenericArguments();
	}
}
