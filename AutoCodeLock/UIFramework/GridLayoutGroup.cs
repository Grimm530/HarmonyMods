using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class GridLayoutGroup : BaseLayoutGroup
{
	public GridLayoutGroup(int columns, int rows, Axis axis)
		: base(columns, rows, axis)
	{
	}

	public GridLayoutGroup(Axis axis)
		: base(1, 1, axis)
	{
	}

	public override void ResizeContentToFit(RectTransformComponent transform, int numberOfItems)
	{
		m_Size = m_FixedSize;
		float num5 = default(float);
		while (true)
		{
			int num = 1158387817;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x20F3158)) % 11)
				{
				case 2u:
					break;
				case 1u:
				{
					int num8;
					int num9;
					if (!(m_Size.x > 0f))
					{
						num8 = 2056108001;
						num9 = num8;
					}
					else
					{
						num8 = 661645658;
						num9 = num8;
					}
					num = num8 ^ (int)(num2 * 355692899);
					continue;
				}
				case 7u:
					m_Area = new Area(0f - m_ViewportSize.x * 0.5f, 0f - num5 * 0.5f, m_ViewportSize.x * 0.5f, num5 * 0.5f);
					num = ((int)num2 * -1905014284) ^ -678625908;
					continue;
				case 0u:
					m_Columns = MaxInSpace(m_ViewportSize.x - (m_Padding.Left + m_Padding.Right), m_Size.x, m_Spacing.Horizontal);
					num = (int)(num2 * 24038656) ^ -208907049;
					continue;
				case 6u:
				{
					m_Columns = Mathf.CeilToInt((float)numberOfItems / (float)m_Rows);
					float num4 = Mathf.Max(m_ViewportSize.x, m_Padding.Horizontal + (float)m_Columns * m_Size.x + (float)Mathf.Max(m_Columns - 1, 0) * m_Spacing.Horizontal);
					transform.Set(new Offset(0f, 0f, num4 - m_ViewportSize.x, 0f));
					m_Area = new Area(0f - num4 * 0.5f, 0f - m_ViewportSize.y * 0.5f, num4 * 0.5f, m_ViewportSize.y * 0.5f);
					num = (int)(num2 * 218867830) ^ -545557704;
					continue;
				}
				case 5u:
					m_Rows = MaxInSpace(m_ViewportSize.y - (m_Padding.Top + m_Padding.Bottom), m_Size.y, m_Spacing.Vertical);
					num = 548679836;
					continue;
				case 9u:
				{
					int num6;
					int num7;
					if (m_Size.y > 0f)
					{
						num6 = 1328440646;
						num7 = num6;
					}
					else
					{
						num6 = 397876287;
						num7 = num6;
					}
					num = num6 ^ ((int)num2 * -130299395);
					continue;
				}
				case 4u:
					m_Rows = Mathf.CeilToInt((float)numberOfItems / (float)m_Columns);
					num5 = Mathf.Max(m_ViewportSize.y, m_Padding.Vertical + (float)m_Rows * m_Size.y + (float)Mathf.Max(m_Rows - 1, 0) * m_Spacing.Vertical);
					transform.Set(new Offset(0f, 0f - (num5 - m_ViewportSize.y), 0f, 0f));
					num = ((int)num2 * -328127548) ^ -1460264013;
					continue;
				case 8u:
					return;
				case 10u:
				{
					int num3;
					if (m_Axis != Axis.Horizontal)
					{
						num = 526822786;
						num3 = num;
					}
					else
					{
						num = 820526615;
						num3 = num;
					}
					continue;
				}
				default:
					m_Offset = new Vector2(m_Area.Width - (float)m_Columns * m_Size.x - (float)(m_Columns - 1) * m_Spacing.Horizontal - (m_Padding.Left + m_Padding.Right), 0f - (m_Area.Height - (float)m_Rows * m_Size.y - (float)(m_Rows - 1) * m_Spacing.Vertical - (m_Padding.Bottom + m_Padding.Top)));
					return;
				}
				break;
			}
		}
	}
}
