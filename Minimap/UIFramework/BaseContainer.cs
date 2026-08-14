using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Oxide.Ext.Chaos.Pooling;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class BaseContainer : IPoolable
{
	protected CuiElementComponent m_Element;

	internal RectTransformComponent m_Transform;

	internal List<BaseContainer> m_Children;

	protected BaseContainer m_Parent;

	private StylePreset m_StylePreset;

	public static readonly NeedsCursorComponent NeedsCursorComponent = new NeedsCursorComponent();

	public static readonly NeedsKeyboardComponent NeedsKeyboardComponent = new NeedsKeyboardComponent();

	public string Name
	{
		get
		{
			return m_Element.Name;
		}
		set
		{
			m_Element.Name = value;
		}
	}

	internal CuiElementComponent Element => m_Element;

	internal List<BaseContainer> Children => m_Children;

	public BaseContainer RootContainer
	{
		get
		{
			if (m_Parent == null)
				return this;
			return m_Parent.RootContainer;
		}
	}

	public StylePreset StylePreset
	{
		get
		{
			if (m_StylePreset == null)
			{
				while (true)
				{
					int num = 1405645272;
					while (true)
					{
						uint num2;
						switch ((num2 = (uint)(num ^ 0x6DD74829)) % 5)
						{
						case 4u:
							break;
						case 1u:
						{
							int num3;
							int num4;
							if (m_Parent != null)
							{
								num3 = 340813327;
								num4 = num3;
							}
							else
							{
								num3 = 349018347;
								num4 = num3;
							}
							num = num3 ^ ((int)num2 * -831968198);
							continue;
						}
						case 3u:
							return m_Parent.StylePreset;
						case 2u:
							return ChaosStyle.Preset;
						default:
							goto end_IL_0008;
						}
						break;
					}
					continue;
					end_IL_0008:
					break;
				}
			}
			return m_StylePreset;
		}
	}

	protected void Initialize(string name, Layer layer, Anchor anchor, Offset offset)
	{
		m_Element = UIComponentPool.Get<CuiElementComponent>();
		while (true)
		{
			int num = -2048431625;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -465528354)) % 4)
				{
				case 2u:
					break;
				default:
					return;
				case 1u:
					m_Element.Initialize(name, layer);
					num = (int)(num2 * 1932091309) ^ -2025932009;
					continue;
				case 0u:
					m_Element.Components.Add(m_Transform = UIComponentPool.Get<RectTransformComponent>());
					m_Transform.Set(anchor, offset);
					m_Children = Pool.Get<List<BaseContainer>>();
					m_Children.Add(this);
					num = ((int)num2 * -1637683390) ^ -1473554763;
					continue;
				case 3u:
					return;
				}
				break;
			}
		}
	}

	protected void Initialize(string name, BaseContainer parent, Anchor anchor, Offset offset)
	{
		m_Parent = parent;
		m_Element = UIComponentPool.Get<CuiElementComponent>();
		m_Element.Initialize(name, parent);
		m_Element.Components.Add(m_Transform = UIComponentPool.Get<RectTransformComponent>());
		while (true)
		{
			int num = -123621275;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -1740074055)) % 5)
				{
				case 0u:
					break;
				default:
					return;
				case 2u:
					RootContainer.m_Children.Add(this);
					num = (int)(num2 * 920665788) ^ -1289864378;
					continue;
				case 1u:
				{
					int num3;
					int num4;
					if (parent != null)
					{
						num3 = -378597304;
						num4 = num3;
					}
					else
					{
						num3 = -1246270012;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 622940470);
					continue;
				}
				case 4u:
					m_Transform.Set(anchor, offset);
					num = ((int)num2 * -1676856463) ^ -136824596;
					continue;
				case 3u:
					return;
				}
				break;
			}
		}
	}

	protected virtual void Initialize(string name, BaseContainer parent, RectTransformComponent transform)
	{
		m_Parent = parent;
		while (true)
		{
			int num = -632202633;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -1804226414)) % 7)
				{
				case 0u:
					break;
				default:
					return;
				case 6u:
					m_Element = UIComponentPool.Get<CuiElementComponent>();
					num = (int)((num2 * 712643287) ^ 0x6AFDBEF);
					continue;
				case 2u:
				{
					int num3;
					int num4;
					if (parent != null)
					{
						num3 = 1501035207;
						num4 = num3;
					}
					else
					{
						num3 = 622851632;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 1368449832);
					continue;
				}
				case 4u:
					m_Transform.Set(transform);
					num = (int)((num2 * 1674018735) ^ 0x7BC422CD);
					continue;
				case 3u:
					RootContainer.m_Children.Add(this);
					num = (int)((num2 * 1415335143) ^ 0x1A983ED3);
					continue;
				case 1u:
					m_Element.Initialize(name, parent);
					m_Element.Components.Add(m_Transform = UIComponentPool.Get<RectTransformComponent>());
					num = ((int)num2 * -798499321) ^ 0x101EC1D2;
					continue;
				case 5u:
					return;
				}
				break;
			}
		}
	}

	public static BaseContainer Create(BaseContainer parent)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(CuiHelper.GetGuid(), parent, Anchor.FullStretch, Offset.Default);
		return baseContainer;
	}

	public static BaseContainer Create(BaseContainer parent, Anchor anchor)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(CuiHelper.GetGuid(), parent, anchor, Offset.Default);
		return baseContainer;
	}

	public static BaseContainer Create(BaseContainer parent, Offset offset)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(CuiHelper.GetGuid(), parent, Anchor.FullStretch, offset);
		return baseContainer;
	}

	public static BaseContainer Create(BaseContainer parent, Anchor anchor, Offset offset)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(CuiHelper.GetGuid(), parent, anchor, offset);
		return baseContainer;
	}

	public static BaseContainer Create(string name, BaseContainer parent)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, parent, Anchor.FullStretch, Offset.Default);
		return baseContainer;
	}

	public static BaseContainer Create(string name, Layer layer)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, layer, Anchor.FullStretch, Offset.Default);
		return baseContainer;
	}

	public static BaseContainer Create(string name, BaseContainer parent, Anchor anchor)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, parent, anchor, Offset.Default);
		return baseContainer;
	}

	public static BaseContainer Create(string name, Layer layer, Anchor anchor)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, layer, anchor, Offset.Default);
		return baseContainer;
	}

	public static BaseContainer Create(string name, BaseContainer parent, Offset offset)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, parent, Anchor.FullStretch, offset);
		return baseContainer;
	}

	public static BaseContainer Create(string name, Layer layer, Offset offset)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, layer, Anchor.FullStretch, offset);
		return baseContainer;
	}

	public static BaseContainer Create(string name, BaseContainer parent, Anchor anchor, Offset offset)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, parent, anchor, offset);
		return baseContainer;
	}

	public static BaseContainer Create(string name, Layer layer, Anchor anchor, Offset offset)
	{
		BaseContainer baseContainer = UIContainerPool.Get<BaseContainer>();
		baseContainer.Initialize(name, layer, anchor, offset);
		return baseContainer;
	}

	public BaseContainer WithParent(string parent)
	{
		m_Element.Parent = parent;
		List<BaseContainer> children = RootContainer.m_Children;
		if (!children.Contains(this))
		{
			while (true)
			{
				int num = -208202003;
				while (true)
				{
					uint num2;
					switch ((num2 = (uint)(num ^ -49472104)) % 3)
					{
					case 0u:
						break;
					case 2u:
						children.Add(this);
						num = ((int)num2 * -1557128707) ^ -1096258144;
						continue;
					default:
						goto end_IL_0021;
					}
					break;
				}
				continue;
				end_IL_0021:
				break;
			}
		}
		return this;
	}

	public BaseContainer WithParent(BaseContainer parent)
	{
		m_Element.Parent = parent.m_Element.Name;
		m_Parent = parent;
		List<BaseContainer> children = RootContainer.m_Children;
		if (!children.Contains(this))
		{
			while (true)
			{
				int num = 782652366;
				while (true)
				{
					uint num2;
					switch ((num2 = (uint)(num ^ 0x477314CC)) % 3)
					{
					case 0u:
						break;
					case 1u:
						children.Add(this);
						num = (int)(num2 * 1509993651) ^ -1008182995;
						continue;
					default:
						goto end_IL_0032;
					}
					break;
				}
				continue;
				end_IL_0032:
				break;
			}
		}
		return this;
	}

	public BaseContainer WithChildren(Action<BaseContainer> createAction)
	{
		createAction(this);
		return this;
	}

	public BaseContainer WithName(string name)
	{
		Name = name;
		return this;
	}

	public BaseContainer WithParent(Layer layer)
	{
		m_Element.Parent = EnumConverters.ToJson(layer);
		m_Children = Pool.Get<List<BaseContainer>>();
		m_Children.Add(this);
		return this;
	}

	public BaseContainer WithAnchor(Anchor anchor)
	{
		m_Transform.AnchorMin = anchor.Min;
		m_Transform.AnchorMax = anchor.Max;
		return this;
	}

	public BaseContainer WithOffset(Offset offset)
	{
		m_Transform.OffsetMin = offset.Min;
		while (true)
		{
			int num = -1958744266;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -824699156)) % 3)
				{
				case 0u:
					break;
				case 2u:
					goto IL_0033;
				default:
					return this;
				}
				break;
				IL_0033:
				m_Transform.OffsetMax = offset.Max;
				num = (int)((num2 * 1196185935) ^ 0x978A301);
			}
		}
	}

	public BaseContainer FromTransform(RectTransformComponent transform)
	{
		m_Transform.AnchorMin = transform.AnchorMin;
		while (true)
		{
			int num = 1540943610;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0xD05C125)) % 4)
				{
				case 0u:
					break;
				case 3u:
					m_Transform.AnchorMax = transform.AnchorMax;
					num = (int)(num2 * 2065009808) ^ -1766731429;
					continue;
				case 2u:
					m_Transform.OffsetMin = transform.OffsetMin;
					num = (int)(num2 * 1002762579) ^ -1534051410;
					continue;
				default:
					m_Transform.OffsetMax = transform.OffsetMax;
					return this;
				}
				break;
			}
		}
	}

	public BaseContainer SetActive(bool active)
	{
		m_Element.ActiveSelf = active;
		return this;
	}

	public BaseContainer WithFadeOut(float fadeOut)
	{
		m_Element.FadeOut = fadeOut;
		return this;
	}

	public BaseContainer Destroy(string panel)
	{
		m_Element.Destroy = panel;
		return this;
	}

	public BaseContainer DestroyExisting()
	{
		m_Element.Destroy = Name;
		return this;
	}

	public BaseContainer NeedsCursor()
	{
		m_Element.Components.Add(NeedsCursorComponent);
		return this;
	}

	public BaseContainer NeedsKeyboard()
	{
		m_Element.Components.Add(NeedsKeyboardComponent);
		return this;
	}

	public BaseContainer AsUpdate()
	{
		m_Element.Update = true;
		return this;
	}

	public BaseContainer WithOutline(OutlineComponent outlineComponent)
	{
		m_Element.Components.Add(outlineComponent);
		return this;
	}

	public BaseContainer WithCountdown(CountdownComponent countdownComponent)
	{
		m_Element.Components.Add(countdownComponent);
		return this;
	}

	public ScrollContentContainer WithScrollView(ScrollViewComponent scrollViewComponent)
	{
		m_Element.Components.Add(scrollViewComponent);
		return ScrollContentContainer.Create(Name, this, scrollViewComponent);
	}

	public BaseContainer WithStylePreset(StylePreset stylePreset)
	{
		m_StylePreset = stylePreset;
		return this;
	}

	public BaseContainer WithLayoutGroup<T1, T2>(T1 layoutGroup, List<T2> elements, int page, Action<int, T2, BaseContainer, Anchor, Offset> createElementAction) where T1 : BaseLayoutGroup
	{
		layoutGroup.RecalculateSize();
		int num = Mathf.Min(elements.Count, (page + 1) * layoutGroup.PerPage);
		int num2 = 0;
		int num5 = default(int);
		while (true)
		{
			int num3 = -1345366004;
			while (true)
			{
				uint num4;
				switch ((num4 = (uint)(num3 ^ -88178086)) % 7)
				{
				case 6u:
					break;
				case 3u:
				{
					int num6;
					if (num5 < num)
					{
						num3 = -1028604667;
						num6 = num3;
					}
					else
					{
						num3 = -972091317;
						num6 = num3;
					}
					continue;
				}
				case 4u:
				{
					layoutGroup.Evaluate(num2, out var anchor, out var offset);
					createElementAction(num2, elements[num5], this, anchor, offset);
					num3 = -1482293805;
					continue;
				}
				case 2u:
					num5 = page * layoutGroup.PerPage;
					num3 = (int)((num4 * 618148475) ^ 0x687DC34C);
					continue;
				case 5u:
					num5++;
					num3 = ((int)num4 * -431981082) ^ 0x1D5E5A94;
					continue;
				case 1u:
					num2++;
					num3 = ((int)num4 * -1069430203) ^ 0x3068E350;
					continue;
				default:
					return this;
				}
				break;
			}
		}
	}

	public BaseContainer WithLayoutGroup<T1, T2>(T1 layoutGroup, T2[] elements, int page, Action<int, T2, BaseContainer, Anchor, Offset> createElementAction) where T1 : BaseLayoutGroup
	{
		layoutGroup.RecalculateSize();
		int num5 = default(int);
		int num3 = default(int);
		int num4 = default(int);
		Anchor anchor = default(Anchor);
		Offset offset = default(Offset);
		while (true)
		{
			int num = -1216226794;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -384644748)) % 8)
				{
				case 7u:
					break;
				case 6u:
				{
					int num6;
					if (num5 >= num3)
					{
						num = -827387993;
						num6 = num;
					}
					else
					{
						num = -1368855872;
						num6 = num;
					}
					continue;
				}
				case 0u:
					num4++;
					num5++;
					num = ((int)num2 * -2012325227) ^ -711362734;
					continue;
				case 1u:
					num5 = page * layoutGroup.PerPage;
					num = ((int)num2 * -759060058) ^ -2014554660;
					continue;
				case 4u:
					layoutGroup.Evaluate(num4, out anchor, out offset);
					num = -991921359;
					continue;
				case 5u:
					createElementAction(num4, elements[num5], this, anchor, offset);
					num = (int)(num2 * 1271959911) ^ -2096879073;
					continue;
				case 2u:
					num3 = Mathf.Min(elements.Length, (page + 1) * layoutGroup.PerPage);
					num4 = 0;
					num = ((int)num2 * -689776626) ^ -58367143;
					continue;
				default:
					return this;
				}
				break;
			}
		}
	}

	public BaseContainer WithLayoutGroup<T1, T2>(T1 layoutGroup, IEnumerable<T2> elements, int page, Action<int, T2, BaseContainer, Anchor, Offset> createElementAction) where T1 : BaseLayoutGroup
	{
		layoutGroup.RecalculateSize();
		int num5 = default(int);
		int num3 = default(int);
		int num4 = default(int);
		Anchor anchor = default(Anchor);
		Offset offset = default(Offset);
		while (true)
		{
			int num = -109352215;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -1566558810)) % 11)
				{
				case 3u:
					break;
				case 0u:
				{
					int num6;
					if (num5 < num3)
					{
						num = -1713113661;
						num6 = num;
					}
					else
					{
						num = -376682133;
						num6 = num;
					}
					continue;
				}
				case 4u:
					num4++;
					num = ((int)num2 * -330901090) ^ 0x5FD5C535;
					continue;
				case 8u:
					num = (int)(num2 * 1289286599) ^ -1231563170;
					continue;
				case 2u:
					layoutGroup.Evaluate(num4, out anchor, out offset);
					num = -1917944327;
					continue;
				case 5u:
					num5++;
					num = ((int)num2 * -1702784256) ^ 0x747E882B;
					continue;
				case 10u:
					num4 = 0;
					num = ((int)num2 * -647780215) ^ 0x8A2F6EF;
					continue;
				case 9u:
					num5 = page * layoutGroup.PerPage;
					num = (int)((num2 * 1253649984) ^ 0x36886D05);
					continue;
				case 1u:
					createElementAction(num4, elements.ElementAt(num5), this, anchor, offset);
					num = ((int)num2 * -1108831083) ^ -260300301;
					continue;
				case 7u:
					num3 = Mathf.Min(elements.Count(), (page + 1) * layoutGroup.PerPage);
					num = (int)(num2 * 1963949620) ^ -844753534;
					continue;
				default:
					return this;
				}
				break;
			}
		}
	}

	public virtual void OnEnterPool()
	{
		m_StylePreset = null;
		if (m_Children != null)
		{
			for (int i = 0; i < m_Children.Count; i++)
			{
				BaseContainer t = m_Children[i];
				if (t != null && t != this)
				{
					UIContainerPool.Free(ref t);
				}
			}
			Pool.FreeUnmanaged(ref m_Children);
			m_Children = null;
		}
		if (m_Transform != null)
		{
			UIComponentPool.Free(ref m_Transform);
			m_Transform = null;
		}
		if (m_Element != null)
		{
			UIComponentPool.Free(ref m_Element);
			m_Element = null;
		}
		m_Parent = null;
	}

	void IPoolable.OnEnterPool()
	{
		//ILSpy generated this explicit interface implementation from .override directive in OnEnterPool
		this.OnEnterPool();
	}

	public virtual void OnLeavePool()
	{
	}

	void IPoolable.OnLeavePool()
	{
		//ILSpy generated this explicit interface implementation from .override directive in OnLeavePool
		this.OnLeavePool();
	}
}
