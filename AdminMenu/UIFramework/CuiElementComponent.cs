using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class CuiElementComponent : BaseCuiComponent
{
	[CompilerGenerated]
	private readonly List<BaseCuiComponent> _202C_202E_206C_206B_200D_200E_200C_202D_200C_202B_200E_206A_206E_202C_202D_206B_206A_202C_202C_200D_202A_202E_200E_206D_202B_206D_206C_202A_200B_206D_200F_206E_206D_206D_202A_206B_206A_202E_206F_202C_202E = new List<BaseCuiComponent>();

	[CompilerGenerated]
	private bool _202B_206C_206D_202B_206C_202E_206C_202D_206F_200D_202E_202E_200E_202C_206D_206C_206C_202D_200E_200B_206A_202D_202E_206F_202A_206F_202B_206C_206B_200D_202A_206F_206C_200B_200D_202A_206E_206E_200C_206F_202E = true;

	[CompilerGenerated]
	private float _206B_206B_200D_206C_202E_206C_200C_206F_200D_202E_202A_202A_202D_202E_200D_200C_206F_206F_202E_200B_206F_206F_206D_206C_206B_202A_206B_202E_206D_200D_206E_206A_202C_206A_202E_206F_202D_200F_200B_200C_202E;

	[CompilerGenerated]
	private string _200F_202C_206B_206B_202B_206A_200F_202D_202D_206C_200E_200F_200B_202C_206A_206B_206E_202E_200F_206E_206D_202C_202E_200D_202D_202E_202C_206E_206F_202A_200E_200B_206A_200D_200B_202E_200F_200E_202C_200E_202E;

	[CompilerGenerated]
	private string _200F_202B_202C_200D_206D_200E_200E_200C_200B_206F_202E_202C_202E_200D_206F_202A_202E_202A_200B_206E_206A_202C_200C_200F_200C_200B_202A_206C_202E_202B_202A_202A_200E_200D_202D_202D_206B_206A_206E_202D_202E;

	[CompilerGenerated]
	private string _206B_200D_206E_200C_206F_206F_202D_200E_202E_200D_202A_206A_206B_206B_202C_202E_200D_202B_206E_202E_206B_206B_206F_206C_206E_200C_202D_206C_206D_200C_200B_200E_200C_206C_206E_206D_202C_200B_206B_202C_202E;

	[CompilerGenerated]
	private bool _206A_206B_202C_206F_202A_206F_206F_206F_202D_206E_200F_206B_200D_206B_206C_202D_206B_202D_200C_202E_206F_202A_206D_206D_206B_202C_202E_200D_206D_202B_202C_202C_206F_200C_200B_202B_202E_200C_200C_202D_202E;

	[CompilerGenerated]
	private bool _202D_206A_206D_202E_200D_206B_206D_202A_202B_206F_200C_200B_200F_206F_200C_200E_206F_200D_202C_200F_202B_200C_200E_206D_200C_206A_200C_206B_200B_200E_206C_206F_200C_202B_202B_200D_206B_202D_202A_200E_202E;

	public List<BaseCuiComponent> Components
	{
		[CompilerGenerated]
		get
		{
			return _202C_202E_206C_206B_200D_200E_200C_202D_200C_202B_200E_206A_206E_202C_202D_206B_206A_202C_202C_200D_202A_202E_200E_206D_202B_206D_206C_202A_200B_206D_200F_206E_206D_206D_202A_206B_206A_202E_206F_202C_202E;
		}
	}

	public bool ActiveSelf
	{
		[CompilerGenerated]
		get
		{
			return _202B_206C_206D_202B_206C_202E_206C_202D_206F_200D_202E_202E_200E_202C_206D_206C_206C_202D_200E_200B_206A_202D_202E_206F_202A_206F_202B_206C_206B_200D_202A_206F_206C_200B_200D_202A_206E_206E_200C_206F_202E;
		}
		[CompilerGenerated]
		set
		{
			_202B_206C_206D_202B_206C_202E_206C_202D_206F_200D_202E_202E_200E_202C_206D_206C_206C_202D_200E_200B_206A_202D_202E_206F_202A_206F_202B_206C_206B_200D_202A_206F_206C_200B_200D_202A_206E_206E_200C_206F_202E = value;
		}
	}

	public float FadeOut
	{
		[CompilerGenerated]
		get
		{
			return _206B_206B_200D_206C_202E_206C_200C_206F_200D_202E_202A_202A_202D_202E_200D_200C_206F_206F_202E_200B_206F_206F_206D_206C_206B_202A_206B_202E_206D_200D_206E_206A_202C_206A_202E_206F_202D_200F_200B_200C_202E;
		}
		[CompilerGenerated]
		set
		{
			_206B_206B_200D_206C_202E_206C_200C_206F_200D_202E_202A_202A_202D_202E_200D_200C_206F_206F_202E_200B_206F_206F_206D_206C_206B_202A_206B_202E_206D_200D_206E_206A_202C_206A_202E_206F_202D_200F_200B_200C_202E = value;
		}
	}

	public string Name
	{
		[CompilerGenerated]
		get
		{
			return _200F_202C_206B_206B_202B_206A_200F_202D_202D_206C_200E_200F_200B_202C_206A_206B_206E_202E_200F_206E_206D_202C_202E_200D_202D_202E_202C_206E_206F_202A_200E_200B_206A_200D_200B_202E_200F_200E_202C_200E_202E;
		}
		[CompilerGenerated]
		set
		{
			_200F_202C_206B_206B_202B_206A_200F_202D_202D_206C_200E_200F_200B_202C_206A_206B_206E_202E_200F_206E_206D_202C_202E_200D_202D_202E_202C_206E_206F_202A_200E_200B_206A_200D_200B_202E_200F_200E_202C_200E_202E = value;
		}
	}

	public string Destroy
	{
		[CompilerGenerated]
		get
		{
			return _200F_202B_202C_200D_206D_200E_200E_200C_200B_206F_202E_202C_202E_200D_206F_202A_202E_202A_200B_206E_206A_202C_200C_200F_200C_200B_202A_206C_202E_202B_202A_202A_200E_200D_202D_202D_206B_206A_206E_202D_202E;
		}
		[CompilerGenerated]
		set
		{
			_200F_202B_202C_200D_206D_200E_200E_200C_200B_206F_202E_202C_202E_200D_206F_202A_202E_202A_200B_206E_206A_202C_200C_200F_200C_200B_202A_206C_202E_202B_202A_202A_200E_200D_202D_202D_206B_206A_206E_202D_202E = value;
		}
	}

	public string Parent
	{
		[CompilerGenerated]
		get
		{
			return _206B_200D_206E_200C_206F_206F_202D_200E_202E_200D_202A_206A_206B_206B_202C_202E_200D_202B_206E_202E_206B_206B_206F_206C_206E_200C_202D_206C_206D_200C_200B_200E_200C_206C_206E_206D_202C_200B_206B_202C_202E;
		}
		[CompilerGenerated]
		set
		{
			_206B_200D_206E_200C_206F_206F_202D_200E_202E_200D_202A_206A_206B_206B_202C_202E_200D_202B_206E_202E_206B_206B_206F_206C_206E_200C_202D_206C_206D_200C_200B_200E_200C_206C_206E_206D_202C_200B_206B_202C_202E = value;
		}
	}

	public bool Update
	{
		[CompilerGenerated]
		get
		{
			return _206A_206B_202C_206F_202A_206F_206F_206F_202D_206E_200F_206B_200D_206B_206C_202D_206B_202D_200C_202E_206F_202A_206D_206D_206B_202C_202E_200D_206D_202B_202C_202C_206F_200C_200B_202B_202E_200C_200C_202D_202E;
		}
		[CompilerGenerated]
		set
		{
			_206A_206B_202C_206F_202A_206F_206F_206F_202D_206E_200F_206B_200D_206B_206C_202D_206B_202D_200C_202E_206F_202A_206D_206D_206B_202C_202E_200D_206D_202B_202C_202C_206F_200C_200B_202B_202E_200C_200C_202D_202E = value;
		}
	}

	public bool DummyComponent
	{
		[CompilerGenerated]
		get
		{
			return _202D_206A_206D_202E_200D_206B_206D_202A_202B_206F_200C_200B_200F_206F_200C_200E_206F_200D_202C_200F_202B_200C_200E_206D_200C_206A_200C_206B_200B_200E_206C_206F_200C_202B_202B_200D_206B_202D_202A_200E_202E;
		}
		[CompilerGenerated]
		set
		{
			_202D_206A_206D_202E_200D_206B_206D_202A_202B_206F_200C_200B_200F_206F_200C_200E_206F_200D_202C_200F_202B_200C_200E_206D_200C_206A_200C_206B_200B_200E_206C_206F_200C_202B_202B_200D_206B_202D_202A_200E_202E = value;
		}
	}

	public void Initialize(string name, BaseContainer parent)
	{
		Name = name;
		Parent = parent.Element.Name;
	}

	public void Initialize(string name, Layer layer)
	{
		Name = name;
		Parent = EnumConverters.ToJson(layer);
	}

	public override void CopyFrom<T>(T other)
	{
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		if (DummyComponent)
		{
			return;
		}
		if (string.IsNullOrEmpty(Name))
		{
			Debug.Log("[UIFramework] Attempting to serialize a CuiElemement with out a name. Skipping element");
			return;
		}
		if (string.IsNullOrEmpty(Parent))
		{
			Debug.Log("[UIFramework] Attempting to serialize a CuiElemement with out a parent. Skipping element");
			return;
		}
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("name");
		jsonWriter.WriteValue(Name);
		jsonWriter.WritePropertyName("parent");
		jsonWriter.WriteValue(Parent);
		if (!ActiveSelf || IsFieldDirty("ActiveSelf", dirtyFields))
		{
			jsonWriter.WritePropertyName("activeSelf");
			jsonWriter.WriteValue(ActiveSelf);
		}
		if (!string.IsNullOrEmpty(Destroy) || IsFieldDirty("Destroy", dirtyFields))
		{
			jsonWriter.WritePropertyName("destroyUi");
			jsonWriter.WriteValue(Destroy);
		}
		if (Update || IsFieldDirty("Update", dirtyFields))
		{
			jsonWriter.WritePropertyName("update");
			jsonWriter.WriteValue(Update);
		}
		if (FadeOut > 0f || IsFieldDirty("FadeOut", dirtyFields))
		{
			jsonWriter.WritePropertyName("fadeOut");
			jsonWriter.WriteValue(FadeOut);
		}
		jsonWriter.WritePropertyName("components");
		jsonWriter.WriteStartArray();
		for (int i = 0; i < Components.Count; i++)
		{
			Components[i].WriteJson(jsonWriter, dirtyFields);
		}
		jsonWriter.WriteEndArray();
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		base.OnEnterPool();
		if (Components != null)
		{
			for (int i = 0; i < Components.Count; i++)
			{
				BaseCuiComponent t = Components[i];
				if (!t.IsConstant)
				{
					UIComponentPool.Free(ref t);
				}
			}
			Components.Clear();
		}
		ActiveSelf = true;
		FadeOut = 0f;
		Name = null;
		Destroy = null;
		Parent = null;
		Update = false;
		DummyComponent = false;
	}
}
