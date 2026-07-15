using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Oxide.Ext.Chaos.UIFramework;

public class RectTransformComponent : BaseCuiComponent
{
	[CompilerGenerated]
	private Bounds _202D_202C_206E_200B_200C_202C_200F_200B_202E_202D_200E_206C_202E_200E_206F_206B_202E_200E_200E_206F_202A_206E_200C_200D_200D_206F_206A_202E_200F_202E_200F_206E_200E_206F_206B_202E_206B_200E_200D_206D_202E = Bounds.one;

	[CompilerGenerated]
	private Bounds _206A_202A_200F_200F_200F_202D_206A_202B_202A_202A_200E_206F_200D_202A_202C_206C_200D_206C_202D_200D_206B_202D_206D_200E_200F_206A_200C_200B_206E_200D_202A_206E_206E_202A_200E_206C_200B_202C_202B_202A_202E = Bounds.zero;

	[CompilerGenerated]
	private Bounds _200F_206E_200D_200C_202A_206D_200D_200E_202D_202C_202B_200D_202C_202C_202A_206E_202C_206B_206D_202A_202D_206F_200B_200D_202A_202D_206E_202E_200F_202A_202A_202C_200D_206A_206D_202E_202B_200B_202C_200D_202E = Bounds.one;

	[CompilerGenerated]
	private Bounds _202E_206A_200D_206C_206C_206A_206F_206C_206A_206C_206C_206D_206D_206A_206C_206B_200B_206D_202E_202B_200B_206D_200C_200B_200E_206F_202C_202A_200B_200F_206C_206C_200C_200D_202B_200F_200D_200D_202B_206F_202E = Bounds.zero;

	[CompilerGenerated]
	private float _202D_200B_200F_206C_206B_202B_206E_202E_206E_206D_200D_200D_200C_206F_200B_206C_206D_200F_200C_202A_202D_206B_206B_200C_202C_202D_200F_206A_206C_202B_206A_202B_200B_202B_206D_200C_202D_206F_202C_202B_202E;

	[CompilerGenerated]
	private string _200C_206E_202D_206C_200D_200B_202E_206F_202A_200E_202C_206F_200F_202A_200D_206E_206D_200E_206B_206A_206D_202E_206A_200B_200C_202A_206A_202E_206C_202B_202A_206A_206E_202C_200D_206E_200F_200B_206A_200E_202E = string.Empty;

	[CompilerGenerated]
	private int _206B_200F_200B_206B_206F_206D_202E_202D_206A_202B_206E_202A_200C_206B_206D_206F_200B_206E_200D_202D_202B_206E_200D_206A_200D_206C_200C_206C_202B_202D_200F_200B_202C_206F_202C_206C_200C_202D_202B_206E_202E = -1;

	public Bounds AnchorMax
	{
		[CompilerGenerated]
		get
		{
			return _202D_202C_206E_200B_200C_202C_200F_200B_202E_202D_200E_206C_202E_200E_206F_206B_202E_200E_200E_206F_202A_206E_200C_200D_200D_206F_206A_202E_200F_202E_200F_206E_200E_206F_206B_202E_206B_200E_200D_206D_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_202D_202C_206E_200B_200C_202C_200F_200B_202E_202D_200E_206C_202E_200E_206F_206B_202E_200E_200E_206F_202A_206E_200C_200D_200D_206F_206A_202E_200F_202E_200F_206E_200E_206F_206B_202E_206B_200E_200D_206D_202E = value;
		}
	}

	public Bounds AnchorMin
	{
		[CompilerGenerated]
		get
		{
			return _206A_202A_200F_200F_200F_202D_206A_202B_202A_202A_200E_206F_200D_202A_202C_206C_200D_206C_202D_200D_206B_202D_206D_200E_200F_206A_200C_200B_206E_200D_202A_206E_206E_202A_200E_206C_200B_202C_202B_202A_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_206A_202A_200F_200F_200F_202D_206A_202B_202A_202A_200E_206F_200D_202A_202C_206C_200D_206C_202D_200D_206B_202D_206D_200E_200F_206A_200C_200B_206E_200D_202A_206E_206E_202A_200E_206C_200B_202C_202B_202A_202E = value;
		}
	}

	public Bounds OffsetMax
	{
		[CompilerGenerated]
		get
		{
			return _200F_206E_200D_200C_202A_206D_200D_200E_202D_202C_202B_200D_202C_202C_202A_206E_202C_206B_206D_202A_202D_206F_200B_200D_202A_202D_206E_202E_200F_202A_202A_202C_200D_206A_206D_202E_202B_200B_202C_200D_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_200F_206E_200D_200C_202A_206D_200D_200E_202D_202C_202B_200D_202C_202C_202A_206E_202C_206B_206D_202A_202D_206F_200B_200D_202A_202D_206E_202E_200F_202A_202A_202C_200D_206A_206D_202E_202B_200B_202C_200D_202E = value;
		}
	}

	public Bounds OffsetMin
	{
		[CompilerGenerated]
		get
		{
			return _202E_206A_200D_206C_206C_206A_206F_206C_206A_206C_206C_206D_206D_206A_206C_206B_200B_206D_202E_202B_200B_206D_200C_200B_200E_206F_202C_202A_200B_200F_206C_206C_200C_200D_202B_200F_200D_200D_202B_206F_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_202E_206A_200D_206C_206C_206A_206F_206C_206A_206C_206C_206D_206D_206A_206C_206B_200B_206D_202E_202B_200B_206D_200C_200B_200E_206F_202C_202A_200B_200F_206C_206C_200C_200D_202B_200F_200D_200D_202B_206F_202E = value;
		}
	}

	public float Rotation
	{
		[CompilerGenerated]
		get
		{
			return _202D_200B_200F_206C_206B_202B_206E_202E_206E_206D_200D_200D_200C_206F_200B_206C_206D_200F_200C_202A_202D_206B_206B_200C_202C_202D_200F_206A_206C_202B_206A_202B_200B_202B_206D_200C_202D_206F_202C_202B_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_202D_200B_200F_206C_206B_202B_206E_202E_206E_206D_200D_200D_200C_206F_200B_206C_206D_200F_200C_202A_202D_206B_206B_200C_202C_202D_200F_206A_206C_202B_206A_202B_200B_202B_206D_200C_202D_206F_202C_202B_202E = value;
		}
	}

	public string SetParent
	{
		[CompilerGenerated]
		get
		{
			return _200C_206E_202D_206C_200D_200B_202E_206F_202A_200E_202C_206F_200F_202A_200D_206E_206D_200E_206B_206A_206D_202E_206A_200B_200C_202A_206A_202E_206C_202B_202A_206A_206E_202C_200D_206E_200F_200B_206A_200E_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_200C_206E_202D_206C_200D_200B_202E_206F_202A_200E_202C_206F_200F_202A_200D_206E_206D_200E_206B_206A_206D_202E_206A_200B_200C_202A_206A_202E_206C_202B_202A_206A_206E_202C_200D_206E_200F_200B_206A_200E_202E = value;
		}
	}

	public int SetTransformIndex
	{
		[CompilerGenerated]
		get
		{
			return _206B_200F_200B_206B_206F_206D_202E_202D_206A_202B_206E_202A_200C_206B_206D_206F_200B_206E_200D_202D_202B_206E_200D_206A_200D_206C_200C_206C_202B_202D_200F_200B_202C_206F_202C_206C_200C_202D_202B_206E_202E;
		}
		[CompilerGenerated]
		internal set
		{
			_206B_200F_200B_206B_206F_206D_202E_202D_206A_202B_206E_202A_200C_206B_206D_206F_200B_206E_200D_202D_202B_206E_200D_206A_200D_206C_200C_206C_202B_202D_200F_200B_202C_206F_202C_206C_200C_202D_202B_206E_202E = value;
		}
	}

	public void Set()
	{
		AnchorMin = Bounds.zero;
		while (true)
		{
			int num = 1148937186;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x4B7827F3)) % 6)
				{
				case 4u:
					break;
				default:
					return;
				case 1u:
					AnchorMax = Bounds.one;
					num = ((int)num2 * -688612174) ^ -1924462778;
					continue;
				case 3u:
					OffsetMin = Bounds.zero;
					num = (int)((num2 * 1109582488) ^ 0x53BA3A5F);
					continue;
				case 2u:
					OffsetMax = Bounds.one;
					num = ((int)num2 * -1578144980) ^ 0x5EBAC94B;
					continue;
				case 0u:
					Rotation = 0f;
					num = ((int)num2 * -2085976323) ^ 0x6EF706BC;
					continue;
				case 5u:
					return;
				}
				break;
			}
		}
	}

	public void Set(float2 anchorMin, float2 anchorMax, float2 offsetMin, float2 offsetMax, float rotation)
	{
		AnchorMin = new Bounds(anchorMin);
		while (true)
		{
			int num = -1476964281;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -1732973546)) % 4)
				{
				case 0u:
					break;
				case 1u:
					AnchorMax = new Bounds(anchorMax);
					OffsetMin = new Bounds(offsetMin);
					num = ((int)num2 * -1132272723) ^ 0x7BEAE1D8;
					continue;
				case 3u:
					OffsetMax = new Bounds(offsetMax);
					num = ((int)num2 * -1170038510) ^ 0x9241CCA;
					continue;
				default:
					Rotation = rotation;
					return;
				}
				break;
			}
		}
	}

	public void Set(Bounds anchorMin, Bounds anchorMax, Bounds offsetMin, Bounds offsetMax, float rotation)
	{
		AnchorMin = anchorMin;
		while (true)
		{
			int num = 1236011665;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x51660E3C)) % 4)
				{
				case 3u:
					break;
				case 1u:
					AnchorMax = anchorMax;
					num = (int)(num2 * 1940493285) ^ -247566001;
					continue;
				case 2u:
					OffsetMin = offsetMin;
					num = (int)(num2 * 1134293106) ^ -1402718732;
					continue;
				default:
					OffsetMax = offsetMax;
					Rotation = rotation;
					return;
				}
				break;
			}
		}
	}

	public void Set(RectTransformComponent other)
	{
		AnchorMin = other.AnchorMin;
		while (true)
		{
			int num = 1914936372;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x396E0049)) % 3)
				{
				case 0u:
					break;
				case 1u:
					goto IL_002e;
				default:
					OffsetMin = other.OffsetMin;
					OffsetMax = other.OffsetMax;
					Rotation = other.Rotation;
					return;
				}
				break;
				IL_002e:
				AnchorMax = other.AnchorMax;
				num = (int)(num2 * 703981499) ^ -1509429375;
			}
		}
	}

	public void Set(Anchor anchor)
	{
		AnchorMin = anchor.Min;
		while (true)
		{
			int num = 1695694344;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x5EE475E1)) % 4)
				{
				case 0u:
					break;
				case 1u:
					AnchorMax = anchor.Max;
					num = (int)((num2 * 594854120) ^ 0x1D690BBB);
					continue;
				case 2u:
					OffsetMin = Bounds.zero;
					num = (int)((num2 * 2009756777) ^ 0x279368E0);
					continue;
				default:
					OffsetMax = Bounds.one;
					return;
				}
				break;
			}
		}
	}

	public void Set(Offset offset)
	{
		AnchorMin = Bounds.zero;
		AnchorMax = Bounds.one;
		while (true)
		{
			int num = 772088842;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x258E47F7)) % 4)
				{
				case 3u:
					break;
				default:
					return;
				case 1u:
					OffsetMin = offset.Min;
					num = (int)((num2 * 1066454808) ^ 0x7CECE6ED);
					continue;
				case 2u:
					OffsetMax = offset.Max;
					num = ((int)num2 * -1470230581) ^ 0x4C65F00D;
					continue;
				case 0u:
					return;
				}
				break;
			}
		}
	}

	public void Set(Anchor anchor, Offset offset, float rotation = 0f)
	{
		AnchorMin = anchor.Min;
		AnchorMax = anchor.Max;
		while (true)
		{
			int num = -1773841895;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -1404855712)) % 3)
				{
				case 2u:
					break;
				case 1u:
					goto IL_003a;
				default:
					Rotation = rotation;
					return;
				}
				break;
				IL_003a:
				OffsetMin = offset.Min;
				OffsetMax = offset.Max;
				num = ((int)num2 * -1312258872) ^ -181917060;
			}
		}
	}

	public void SetRotation(float rotation)
	{
		Rotation = rotation;
	}

	public void SetTransformParent(string parentName)
	{
		SetParent = parentName;
	}

	public void SetSiblingIndex(int index)
	{
		SetTransformIndex = index;
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is RectTransformComponent rectTransformComponent)
		{
			AnchorMin = rectTransformComponent.AnchorMin;
			AnchorMax = rectTransformComponent.AnchorMax;
			OffsetMin = rectTransformComponent.OffsetMin;
			OffsetMax = rectTransformComponent.OffsetMax;
			Rotation = rectTransformComponent.Rotation;
			SetParent = rectTransformComponent.SetParent;
			SetTransformIndex = rectTransformComponent.SetTransformIndex;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("RectTransform");
		if (AnchorMin != Bounds.zero || IsFieldDirty("AnchorMin", dirtyFields))
		{
			jsonWriter.WritePropertyName("anchormin");
			jsonWriter.WriteValue(AnchorMin.ToString("0 0"));
		}
		if (AnchorMax != Bounds.one || IsFieldDirty("AnchorMax", dirtyFields))
		{
			jsonWriter.WritePropertyName("anchormax");
			jsonWriter.WriteValue(AnchorMax.ToString("1 1"));
		}
		if (OffsetMin != Bounds.zero || IsFieldDirty("OffsetMin", dirtyFields))
		{
			jsonWriter.WritePropertyName("offsetmin");
			jsonWriter.WriteValue(OffsetMin.ToString("0 0"));
		}
		if (OffsetMax != Bounds.one || IsFieldDirty("OffsetMax", dirtyFields))
		{
			jsonWriter.WritePropertyName("offsetmax");
			jsonWriter.WriteValue(OffsetMax.ToString("1 1"));
		}
		if (Rotation != 0f || IsFieldDirty("Rotation", dirtyFields))
		{
			jsonWriter.WritePropertyName("rotation");
			jsonWriter.WriteValue(Rotation);
		}
		if (!string.IsNullOrEmpty(SetParent) || IsFieldDirty("SetParent", dirtyFields))
		{
			jsonWriter.WritePropertyName("setParent");
			jsonWriter.WriteValue(SetParent);
		}
		if (SetTransformIndex != -1 || IsFieldDirty("SetTransformIndex", dirtyFields))
		{
			jsonWriter.WritePropertyName("setTransformIndex");
			jsonWriter.WriteValue(SetTransformIndex);
		}
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		base.OnEnterPool();
		AnchorMax = Bounds.one;
		AnchorMin = Bounds.zero;
		OffsetMax = Bounds.one;
		OffsetMin = Bounds.zero;
		Rotation = 0f;
		SetParent = string.Empty;
		SetTransformIndex = -1;
	}
}
