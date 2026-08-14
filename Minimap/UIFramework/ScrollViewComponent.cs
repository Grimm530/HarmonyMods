using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Ext.Chaos.UIFramework;

public class ScrollViewComponent : BaseCuiComponent
{
	[CompilerGenerated]
	private bool _202E_202E_202C_206E_202A_206B_202C_206F_200F_200F_206C_202C_202C_200E_202C_206A_202D_202E_206A_200B_200D_200F_200F_206A_202C_206B_200C_200E_200D_200D_202C_200B_202B_202E_200C_206E_202B_206A_206E_200F_202E;

	[CompilerGenerated]
	private bool _200F_200C_202E_200C_206C_202D_200F_206B_200E_206B_202D_206E_200B_202D_206B_202E_206D_202B_206E_206A_206F_202D_206C_206B_206D_206F_202D_202D_200B_206B_206F_200E_202B_202A_206D_200C_202C_202B_202C_206E_202E;

	[CompilerGenerated]
	private ScrollRect.MovementType _206E_206B_200B_202B_206F_202C_202B_202E_200F_202B_206F_202D_200F_202D_200D_200B_206A_200D_206D_206E_200F_202D_200F_200C_206D_206F_206A_200F_202E_206F_200B_200F_202D_206C_206D_206A_202E_206E_206F_202E;

	[CompilerGenerated]
	private float _200D_200E_200E_206B_200F_206E_206A_202D_200E_202C_206D_202B_206C_206D_200B_202A_200B_206C_206B_202A_206D_206D_206C_200D_200F_202C_206E_206C_206E_206F_206E_202E_206B_206B_202C_206E_206E_206C_206D_200D_202E = 0.1f;

	[CompilerGenerated]
	private bool _206C_200F_202E_206E_206E_202A_202D_206B_200E_206E_206C_206F_206B_206D_202C_206D_200E_200F_202C_202A_202C_202C_206D_202C_206E_202A_200E_202E_206C_200B_202A_202A_202D_200D_200B_200F_206F_200F_202D_202A_202E;

	[CompilerGenerated]
	private float _200D_202C_200F_200E_202A_206E_206A_206E_206A_206F_206D_206B_202A_200F_202E_206E_200F_200C_202A_200F_200C_202B_200D_202A_202E_202D_206B_206F_206A_202D_200E_200D_202B_206D_206F_206F_206C_200C_202A_200D_202E = 0.135f;

	[CompilerGenerated]
	private float _200E_206A_200E_202D_206C_202A_200B_202C_206B_200C_206A_206D_206D_200C_206F_202C_200C_202D_206B_202A_200B_206B_200D_202C_200C_202C_200C_202E_200E_200E_200E_202E_206C_206A_202B_202A_206F_202C_206E_206D_202E = 1f;

	[CompilerGenerated]
	private ScrollbarComponent _200F_206D_206D_200E_202C_206E_206C_202D_206F_202C_206C_206D_202D_206D_206B_200E_206F_202D_200E_200D_206E_200B_202D_206F_206F_200C_206A_202B_200C_200F_202B_200F_206D_200D_202C_202C_206B_206A_206C_200F_202E;

	[CompilerGenerated]
	private ScrollbarComponent _200F_206A_206C_202D_202C_206B_206C_206C_200D_202E_206A_200E_206F_202E_202C_206E_206C_200D_206A_202D_206B_206A_200E_200C_206C_200F_206E_200C_206D_200E_200F_202C_202B_206D_200F_206E_200E_206F_200B_206C_202E;

	[CompilerGenerated]
	private RectTransformComponent _202D_206A_206B_206E_200D_200D_206A_206E_206A_200E_206C_200E_202E_202D_202B_206A_202E_206F_206A_206B_206D_206F_206C_202B_202C_206D_202C_206E_206D_202C_206B_200C_206C_200E_200E_206D_200E_202B_206C_202C_202E;

	public bool Horizontal
	{
		[CompilerGenerated]
		get
		{
			return _202E_202E_202C_206E_202A_206B_202C_206F_200F_200F_206C_202C_202C_200E_202C_206A_202D_202E_206A_200B_200D_200F_200F_206A_202C_206B_200C_200E_200D_200D_202C_200B_202B_202E_200C_206E_202B_206A_206E_200F_202E;
		}
		[CompilerGenerated]
		set
		{
			_202E_202E_202C_206E_202A_206B_202C_206F_200F_200F_206C_202C_202C_200E_202C_206A_202D_202E_206A_200B_200D_200F_200F_206A_202C_206B_200C_200E_200D_200D_202C_200B_202B_202E_200C_206E_202B_206A_206E_200F_202E = value;
		}
	}

	public bool Vertical
	{
		[CompilerGenerated]
		get
		{
			return _200F_200C_202E_200C_206C_202D_200F_206B_200E_206B_202D_206E_200B_202D_206B_202E_206D_202B_206E_206A_206F_202D_206C_206B_206D_206F_202D_202D_200B_206B_206F_200E_202B_202A_206D_200C_202C_202B_202C_206E_202E;
		}
		[CompilerGenerated]
		set
		{
			_200F_200C_202E_200C_206C_202D_200F_206B_200E_206B_202D_206E_200B_202D_206B_202E_206D_202B_206E_206A_206F_202D_206C_206B_206D_206F_202D_202D_200B_206B_206F_200E_202B_202A_206D_200C_202C_202B_202C_206E_202E = value;
		}
	}

	public ScrollRect.MovementType MovementType
	{
		[CompilerGenerated]
		get
		{
			return _206E_206B_200B_202B_206F_202C_202B_202E_200F_202B_206F_202D_200F_202D_200D_200B_206A_200D_206D_206E_200F_202D_200F_200C_206D_206F_206A_200F_202E_206F_200B_200F_202D_206C_206D_206A_202E_206E_206F_202E;
		}
		[CompilerGenerated]
		set
		{
			_206E_206B_200B_202B_206F_202C_202B_202E_200F_202B_206F_202D_200F_202D_200D_200B_206A_200D_206D_206E_200F_202D_200F_200C_206D_206F_206A_200F_202E_206F_200B_200F_202D_206C_206D_206A_202E_206E_206F_202E = value;
		}
	}

	public float Elasticity
	{
		[CompilerGenerated]
		get
		{
			return _200D_200E_200E_206B_200F_206E_206A_202D_200E_202C_206D_202B_206C_206D_200B_202A_200B_206C_206B_202A_206D_206D_206C_200D_200F_202C_206E_206C_206E_206F_206E_202E_206B_206B_202C_206E_206E_206C_206D_200D_202E;
		}
		[CompilerGenerated]
		set
		{
			_200D_200E_200E_206B_200F_206E_206A_202D_200E_202C_206D_202B_206C_206D_200B_202A_200B_206C_206B_202A_206D_206D_206C_200D_200F_202C_206E_206C_206E_206F_206E_202E_206B_206B_202C_206E_206E_206C_206D_200D_202E = value;
		}
	}

	public bool Inertia
	{
		[CompilerGenerated]
		get
		{
			return _206C_200F_202E_206E_206E_202A_202D_206B_200E_206E_206C_206F_206B_206D_202C_206D_200E_200F_202C_202A_202C_202C_206D_202C_206E_202A_200E_202E_206C_200B_202A_202A_202D_200D_200B_200F_206F_200F_202D_202A_202E;
		}
		[CompilerGenerated]
		set
		{
			_206C_200F_202E_206E_206E_202A_202D_206B_200E_206E_206C_206F_206B_206D_202C_206D_200E_200F_202C_202A_202C_202C_206D_202C_206E_202A_200E_202E_206C_200B_202A_202A_202D_200D_200B_200F_206F_200F_202D_202A_202E = value;
		}
	}

	public float DecelerationRate
	{
		[CompilerGenerated]
		get
		{
			return _200D_202C_200F_200E_202A_206E_206A_206E_206A_206F_206D_206B_202A_200F_202E_206E_200F_200C_202A_200F_200C_202B_200D_202A_202E_202D_206B_206F_206A_202D_200E_200D_202B_206D_206F_206F_206C_200C_202A_200D_202E;
		}
		[CompilerGenerated]
		set
		{
			_200D_202C_200F_200E_202A_206E_206A_206E_206A_206F_206D_206B_202A_200F_202E_206E_200F_200C_202A_200F_200C_202B_200D_202A_202E_202D_206B_206F_206A_202D_200E_200D_202B_206D_206F_206F_206C_200C_202A_200D_202E = value;
		}
	}

	public float ScrollSensitivity
	{
		[CompilerGenerated]
		get
		{
			return _200E_206A_200E_202D_206C_202A_200B_202C_206B_200C_206A_206D_206D_200C_206F_202C_200C_202D_206B_202A_200B_206B_200D_202C_200C_202C_200C_202E_200E_200E_200E_202E_206C_206A_202B_202A_206F_202C_206E_206D_202E;
		}
		[CompilerGenerated]
		set
		{
			_200E_206A_200E_202D_206C_202A_200B_202C_206B_200C_206A_206D_206D_200C_206F_202C_200C_202D_206B_202A_200B_206B_200D_202C_200C_202C_200C_202E_200E_200E_200E_202E_206C_206A_202B_202A_206F_202C_206E_206D_202E = value;
		}
	}

	public ScrollbarComponent HorizontalScrollbar
	{
		[CompilerGenerated]
		get
		{
			return _200F_206D_206D_200E_202C_206E_206C_202D_206F_202C_206C_206D_202D_206D_206B_200E_206F_202D_200E_200D_206E_200B_202D_206F_206F_200C_206A_202B_200C_200F_202B_200F_206D_200D_202C_202C_206B_206A_206C_200F_202E;
		}
		[CompilerGenerated]
		set
		{
			_200F_206D_206D_200E_202C_206E_206C_202D_206F_202C_206C_206D_202D_206D_206B_200E_206F_202D_200E_200D_206E_200B_202D_206F_206F_200C_206A_202B_200C_200F_202B_200F_206D_200D_202C_202C_206B_206A_206C_200F_202E = value;
		}
	}

	public ScrollbarComponent VerticalScrollbar
	{
		[CompilerGenerated]
		get
		{
			return _200F_206A_206C_202D_202C_206B_206C_206C_200D_202E_206A_200E_206F_202E_202C_206E_206C_200D_206A_202D_206B_206A_200E_200C_206C_200F_206E_200C_206D_200E_200F_202C_202B_206D_200F_206E_200E_206F_200B_206C_202E;
		}
		[CompilerGenerated]
		set
		{
			_200F_206A_206C_202D_202C_206B_206C_206C_200D_202E_206A_200E_206F_202E_202C_206E_206C_200D_206A_202D_206B_206A_200E_200C_206C_200F_206E_200C_206D_200E_200F_202C_202B_206D_200F_206E_200E_206F_200B_206C_202E = value;
		}
	}

	public RectTransformComponent ContentTransform
	{
		[CompilerGenerated]
		get
		{
			return _202D_206A_206B_206E_200D_200D_206A_206E_206A_200E_206C_200E_202E_202D_202B_206A_202E_206F_206A_206B_206D_206F_206C_202B_202C_206D_202C_206E_206D_202C_206B_200C_206C_200E_200E_206D_200E_202B_206C_202C_202E;
		}
		[CompilerGenerated]
		set
		{
			_202D_206A_206B_206E_200D_200D_206A_206E_206A_200E_206C_200E_202E_202D_202B_206A_202E_206F_206A_206B_206D_206F_206C_202B_202C_206D_202C_206E_206D_202C_206B_200C_206C_200E_200E_206D_200E_202B_206C_202C_202E = value;
		}
	}

	public ScrollViewComponent()
	{
		base.IsConstant = true;
	}

	public void ValidateContentTransform()
	{
		if (ContentTransform == null)
		{
			ContentTransform = UIComponentPool.Get<RectTransformComponent>();
			ContentTransform.Set(Anchor.FullStretch, Offset.zero);
		}
	}

	public ScrollViewComponent WithContentTransform(Anchor anchor, Offset offset)
	{
		if (ContentTransform == null)
		{
			ContentTransform = UIComponentPool.Get<RectTransformComponent>();
		}
		ContentTransform.Set(anchor, offset);
		return this;
	}

	public ScrollViewComponent WithScrollbars(ScrollbarComponent.Style style)
	{
		WithHorizontalScrollbar(style);
		WithVerticalScrollbar(style);
		return this;
	}

	public ScrollViewComponent WithHorizontalScrollbar(ScrollbarComponent.Style style)
	{
		if (HorizontalScrollbar == null)
		{
			HorizontalScrollbar = UIComponentPool.Get<ScrollbarComponent>();
		}
		HorizontalScrollbar.WithStyle(style);
		return this;
	}

	public ScrollViewComponent WithVerticalScrollbar(ScrollbarComponent.Style style)
	{
		if (VerticalScrollbar == null)
		{
			VerticalScrollbar = UIComponentPool.Get<ScrollbarComponent>();
		}
		VerticalScrollbar.WithStyle(style);
		return this;
	}

	public void SnapContentToIndex(int indexOfSelected, int totalItems, VerticalLayoutGroup layoutGroup)
	{
		float num = (layoutGroup.ViewportSize.y - layoutGroup.Padding.Vertical) * 0.5f / (layoutGroup.FixedSize.y + layoutGroup.Spacing.Vertical);
		if ((float)indexOfSelected >= num || (float)indexOfSelected < (float)totalItems - num)
		{
			float num2 = Mathf.Abs(ContentTransform.OffsetMin.Y - ContentTransform.OffsetMax.Y);
			float x = ContentTransform.OffsetMin.X;
			float x2 = ContentTransform.OffsetMax.X;
			if ((float)indexOfSelected > (float)totalItems - num)
			{
				ContentTransform.Set(new Offset(x, 0f, x2, num2));
				return;
			}
			float t = ((float)indexOfSelected - num) / ((float)totalItems - num * 2f);
			float yMin = Mathf.Lerp(0f - num2, 0f, t);
			float yMax = Mathf.Lerp(0f, num2, t);
			ContentTransform.Set(new Offset(x, yMin, x2, yMax));
		}
	}

	public void SnapContentToIndex(int indexOfSelected, int totalItems, HorizontalLayoutGroup layoutGroup)
	{
		float num = (layoutGroup.ViewportSize.x - layoutGroup.Padding.Horizontal) * 0.5f / (layoutGroup.FixedSize.x + layoutGroup.Spacing.Horizontal);
		if ((float)indexOfSelected >= num || (float)indexOfSelected < (float)totalItems - num)
		{
			float num2 = Mathf.Abs(ContentTransform.OffsetMin.X - ContentTransform.OffsetMax.X);
			float y = ContentTransform.OffsetMin.Y;
			float y2 = ContentTransform.OffsetMax.Y;
			if ((float)indexOfSelected > (float)totalItems - num)
			{
				ContentTransform.Set(new Offset(0f, y, num2, y2));
				return;
			}
			float t = ((float)indexOfSelected - num) / ((float)totalItems - num * 2f);
			float xMin = Mathf.Lerp(0f - num2, 0f, t);
			float xMax = Mathf.Lerp(0f, num2, t);
			ContentTransform.Set(new Offset(xMin, y, xMax, y2));
		}
	}

	public override void CopyFrom<T>(T other)
	{
		if (!(other is ScrollViewComponent scrollViewComponent))
		{
			return;
		}
		if (scrollViewComponent.ContentTransform != null)
		{
			if (ContentTransform == null)
			{
				ContentTransform = UIComponentPool.Get<RectTransformComponent>();
			}
			ContentTransform.Set(scrollViewComponent.ContentTransform);
		}
		Horizontal = scrollViewComponent.Horizontal;
		Vertical = scrollViewComponent.Vertical;
		MovementType = scrollViewComponent.MovementType;
		Elasticity = scrollViewComponent.Elasticity;
		Inertia = scrollViewComponent.Inertia;
		DecelerationRate = scrollViewComponent.DecelerationRate;
		ScrollSensitivity = scrollViewComponent.ScrollSensitivity;
		if (scrollViewComponent.VerticalScrollbar != null)
		{
			if (VerticalScrollbar == null)
			{
				VerticalScrollbar = UIComponentPool.Get<ScrollbarComponent>();
			}
			VerticalScrollbar.CopyFrom(scrollViewComponent.VerticalScrollbar);
		}
		if (scrollViewComponent.HorizontalScrollbar != null)
		{
			if (HorizontalScrollbar == null)
			{
				HorizontalScrollbar = UIComponentPool.Get<ScrollbarComponent>();
			}
			HorizontalScrollbar.CopyFrom(scrollViewComponent.HorizontalScrollbar);
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.ScrollView");
		if (ContentTransform != null || IsFieldDirty("ContentTransform", dirtyFields))
		{
			jsonWriter.WritePropertyName("contentTransform");
			jsonWriter.WriteStartObject();
			if (ContentTransform.AnchorMin != Bounds.zero || IsFieldDirty("ContentTransform", dirtyFields))
			{
				jsonWriter.WritePropertyName("anchormin");
				jsonWriter.WriteValue(ContentTransform.AnchorMin.ToString("0 0"));
			}
			if (ContentTransform.AnchorMax != Bounds.one || IsFieldDirty("ContentTransform", dirtyFields))
			{
				jsonWriter.WritePropertyName("anchormax");
				jsonWriter.WriteValue(ContentTransform.AnchorMax.ToString("1 1"));
			}
			if (ContentTransform.OffsetMin != Bounds.zero || IsFieldDirty("ContentTransform", dirtyFields))
			{
				jsonWriter.WritePropertyName("offsetmin");
				jsonWriter.WriteValue(ContentTransform.OffsetMin.ToString("0 0"));
			}
			if (ContentTransform.OffsetMax != Bounds.one || IsFieldDirty("ContentTransform", dirtyFields))
			{
				jsonWriter.WritePropertyName("offsetmax");
				jsonWriter.WriteValue(ContentTransform.OffsetMax.ToString("1 1"));
			}
			jsonWriter.WriteEndObject();
		}
		if (Horizontal || IsFieldDirty("Horizontal", dirtyFields))
		{
			jsonWriter.WritePropertyName("horizontal");
			jsonWriter.WriteValue(Horizontal);
			if (HorizontalScrollbar != null)
			{
				jsonWriter.WritePropertyName("horizonalScrollbar");
				HorizontalScrollbar.WriteJson(jsonWriter, dirtyFields);
			}
		}
		if (Vertical || IsFieldDirty("Vertical", dirtyFields))
		{
			jsonWriter.WritePropertyName("vertical");
			jsonWriter.WriteValue(Vertical);
			if (VerticalScrollbar != null)
			{
				jsonWriter.WritePropertyName("verticalScrollbar");
				VerticalScrollbar.WriteJson(jsonWriter, dirtyFields);
			}
		}
		if (MovementType != ScrollRect.MovementType.Clamped || IsFieldDirty("MovementType", dirtyFields))
		{
			jsonWriter.WritePropertyName("movementType");
			jsonWriter.WriteValue(MovementType.ToString());
		}
		if (Elasticity != 0.1f || IsFieldDirty("Elasticity", dirtyFields))
		{
			jsonWriter.WritePropertyName("elasticity");
			jsonWriter.WriteValue(Elasticity);
		}
		if (Inertia || IsFieldDirty("Inertia", dirtyFields))
		{
			jsonWriter.WritePropertyName("inertia");
			jsonWriter.WriteValue(Inertia);
		}
		if (DecelerationRate != 0.135f || IsFieldDirty("DecelerationRate", dirtyFields))
		{
			jsonWriter.WritePropertyName("decelerationRate");
			jsonWriter.WriteValue(DecelerationRate);
		}
		if (ScrollSensitivity != 1f || IsFieldDirty("ScrollSensitivity", dirtyFields))
		{
			jsonWriter.WritePropertyName("scrollSensitivity");
			jsonWriter.WriteValue(ScrollSensitivity);
		}
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		base.OnEnterPool();
		Horizontal = false;
		Vertical = false;
		MovementType = ScrollRect.MovementType.Elastic;
		Elasticity = 0.1f;
		Inertia = false;
		DecelerationRate = 0.135f;
		ScrollSensitivity = 1f;
		if (ContentTransform != null)
		{
			RectTransformComponent t = ContentTransform;
			UIComponentPool.Free(ref t);
			ContentTransform = null;
		}
		if (HorizontalScrollbar != null)
		{
			ScrollbarComponent t2 = HorizontalScrollbar;
			UIComponentPool.Free(ref t2);
			HorizontalScrollbar = null;
		}
		if (VerticalScrollbar != null)
		{
			ScrollbarComponent t3 = VerticalScrollbar;
			UIComponentPool.Free(ref t3);
			VerticalScrollbar = null;
		}
	}
}
