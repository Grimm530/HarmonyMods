using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CustomGenerator;

internal sealed class LocalizedContractResolver : DefaultContractResolver
{
	private readonly bool _ru;

	public LocalizedContractResolver(string language)
	{
		_ru = language == "ru";
	}

	protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		IList<JsonProperty> list = ((DefaultContractResolver)this).CreateProperties(type, memberSerialization);
		List<JsonProperty> list2 = new List<JsonProperty>();
		foreach (JsonProperty item in list)
		{
			IAttributeProvider attributeProvider = item.AttributeProvider;
			LocAttribute locAttribute = ((attributeProvider != null) ? attributeProvider.GetAttributes(typeof(LocAttribute), true).OfType<LocAttribute>().FirstOrDefault() : null);
			if (locAttribute != null)
			{
				item.PropertyName = (_ru ? locAttribute.Ru : locAttribute.En);
				JsonProperty val = ((DefaultContractResolver)this).CreateProperty(type.GetMember(item.UnderlyingName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0], memberSerialization);
				val.PropertyName = (_ru ? locAttribute.En : locAttribute.Ru);
				val.ShouldSerialize = (object _) => false;
				list2.Add(val);
			}
		}
		foreach (JsonProperty item2 in list2)
		{
			list.Add(item2);
		}
		return list;
	}
}
